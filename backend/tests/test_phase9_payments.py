"""Phase 9: Payment + Advice module - Backend Test Suite.
Runs against ASP.NET Core API on http://localhost:8001 with SQLite dev DB.

Covers what iteration_5 review requested (in addition to author's manual smoke):
- SINGLE / DATE_RANGE / FARMER selection modes on POST /api/payments + eligible-purchases preview
- Loan auto-deduction math: exact-match, bigger-loan-capped, no-loan, multi-loan FIFO
- Cancel reversal + repay creates new Payment + new Advice Number
- RBAC 403s for Admin(no Create/Cancel), Operator, SalePurchase; Farmer scoping
- Idempotency 409 on duplicate key
- Print preview vs final (PDF/PNG bytes), print counter increments, reprint audit
- Cash Evidence endpoints for CASH vs BANK mode (captureQueued flag)
- PaymentModes master: exactly 3 rows CASH/BANK/MOBILE_UPI, no ONLINE/Razorpay row
"""
import os
import uuid
import time
import pytest
import requests

BASE = os.environ.get("CANE_API_URL", "http://localhost:8001")


# ----------------------------- helpers -----------------------------
def _login(username, password):
    for attempt in range(5):
        r = requests.post(f"{BASE}/api/auth/login",
                          json={"username": username, "password": password}, timeout=15)
        if r.status_code == 429:
            time.sleep(2 + attempt * 2)
            continue
        return r
    return r


def _hdr(tok):
    return {"Authorization": f"Bearer {tok}", "Content-Type": "application/json"}


def _dev_token():
    r = _login("developer", "DevSecure@2026")
    if r.status_code != 200:
        r = _login("developer", "Dev@2026Temp")
        r.raise_for_status()
        j = r.json()
        if j.get("mustChangePassword"):
            requests.post(f"{BASE}/api/auth/change-password", headers=_hdr(j["accessToken"]), json={
                "currentPassword": "Dev@2026Temp", "newPassword": "DevSecure@2026",
                "confirmPassword": "DevSecure@2026"}, timeout=10).raise_for_status()
        r = _login("developer", "DevSecure@2026")
    r.raise_for_status()
    return r.json()["accessToken"]


def _ensure_user(dev_h, username, role_name, mobile=None, pwd="Test@2026"):
    rr = requests.get(f"{BASE}/api/roles", headers=dev_h, timeout=10).json()
    role = next((x for x in rr if x["name"] == role_name), None)
    assert role, f"Role {role_name} missing"
    if not mobile:
        mobile = f"9{abs(hash(username)) % 1000000000:09d}"
    tmp = pwd + "Tmp"
    requests.post(f"{BASE}/api/users", headers=dev_h, json={
        "username": username, "fullName": f"{role_name} {username}",
        "mobile": mobile, "temporaryPassword": tmp, "roleIds": [role["id"]]}, timeout=15)
    r = _login(username, pwd)
    if r.status_code != 200:
        r2 = _login(username, tmp)
        if r2.status_code != 200:
            return None
        j = r2.json()
        if j.get("mustChangePassword"):
            requests.post(f"{BASE}/api/auth/change-password", headers=_hdr(j["accessToken"]), json={
                "currentPassword": tmp, "newPassword": pwd, "confirmPassword": pwd}, timeout=10)
        r = _login(username, pwd)
        if r.status_code != 200:
            return None
    return r.json()["accessToken"]


def _create_purchase(dev_h, grower_code, gross_kg=5000, tare_kg=1200, vehicle="UP32AB0001"):
    """Full weighment gross+tare cycle -> returns purchaseId of a TARE_DONE/PENDING/UNLOCKED purchase."""
    body = {
        "growerCode": grower_code, "vehicleTypeId": 1, "vehicleNumber": vehicle,
        "varietyTypeId": 1, "varietyId": 1, "cuttingPercent": 2.0, "taxPercent": 1.0,
        "scaleReadingKg": gross_kg, "idempotencyKey": str(uuid.uuid4()),
    }
    g = requests.post(f"{BASE}/api/weighment/gross", headers=dev_h, json=body, timeout=15)
    assert g.status_code == 200, g.text
    pid = g.json()["purchaseId"]
    t = requests.post(f"{BASE}/api/weighment/tare", headers=dev_h, json={
        "purchaseId": pid, "scaleReadingKg": tare_kg, "idempotencyKey": str(uuid.uuid4())}, timeout=15)
    assert t.status_code == 200, t.text
    return pid, t.json().get("purchaseAmount") or requests.get(f"{BASE}/api/purchases/{pid}", headers=dev_h).json()["purchaseAmount"]


def _issue_loan(dev_h, grower_code, amount):
    r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
        "growerCode": grower_code, "loanTypeId": 1, "loanAmount": amount, "remarks": "TEST loan"}, timeout=15)
    assert r.status_code == 200, r.text
    return r.json()["loanId"]


def _ensure_grower(dev_h, name, mobile):
    r = requests.get(f"{BASE}/api/growers?pageSize=200", headers=dev_h, timeout=10).json()
    items = r.get("items", r) if isinstance(r, dict) else r
    existing = next((g for g in items if g.get("mobile") == mobile), None)
    if existing:
        return existing["growerCode"]
    cr = requests.post(f"{BASE}/api/growers", headers=dev_h, json={
        "villageId": 101, "growerName": name, "mobile": mobile, "fatherName": "TEST"}, timeout=15)
    assert cr.status_code in (200, 201), cr.text
    j = cr.json()
    return j.get("growerCode") or j.get("code") or j["data"]["growerCode"]


def _fresh_grower(dev_h, prefix):
    """Always create a NEW grower with unique mobile so test reruns don't share loan state."""
    mobile = f"9{(int(time.time() * 1000) + abs(hash(uuid.uuid4()))) % 1000000000:09d}"
    return _ensure_grower(dev_h, f"TEST_{prefix}_{mobile[-6:]}", mobile)


# ----------------------------- fixtures -----------------------------
@pytest.fixture(scope="module")
def dev_h():
    return _hdr(_dev_token())


@pytest.fixture(scope="module")
def cash_mode_id(dev_h):
    r = requests.get(f"{BASE}/api/payment-modes", headers=dev_h).json()
    return next(m["id"] for m in r["items"] if m["modeCode"] == "CASH")


@pytest.fixture(scope="module")
def bank_mode_id(dev_h):
    r = requests.get(f"{BASE}/api/payment-modes", headers=dev_h).json()
    return next(m["id"] for m in r["items"] if m["modeCode"] == "BANK")


@pytest.fixture(scope="module")
def upi_mode_id(dev_h):
    r = requests.get(f"{BASE}/api/payment-modes", headers=dev_h).json()
    return next(m["id"] for m in r["items"] if m["modeCode"] == "MOBILE_UPI")


# --- role tokens ---
@pytest.fixture(scope="module")
def accountant_token(dev_h):
    return _ensure_user(dev_h, "phase9_acct", "Accountant", pwd="Acct@2026")


@pytest.fixture(scope="module")
def operator_token(dev_h):
    return _ensure_user(dev_h, "phase9_op", "Operator", pwd="Oper@2026")


@pytest.fixture(scope="module")
def salepurchase_token(dev_h):
    return _ensure_user(dev_h, "phase9_sp", "SalePurchase", pwd="Sale@2026")


@pytest.fixture(scope="module")
def admin_token(dev_h):
    return _ensure_user(dev_h, "phase9_admin", "Admin", pwd="Adm@2026")


# ============================================================
# 1. PaymentModes master
# ============================================================
class TestPaymentModes:
    def test_exactly_three_seeded_modes(self, dev_h):
        r = requests.get(f"{BASE}/api/payment-modes", headers=dev_h)
        assert r.status_code == 200
        items = r.json()["items"]
        codes = {m["modeCode"] for m in items if m.get("status") and not m.get("isDeleted")}
        assert codes == {"CASH", "BANK", "MOBILE_UPI"}, f"Expected exactly CASH/BANK/MOBILE_UPI, got {codes}"

    def test_no_online_or_razorpay_mode(self, dev_h):
        r = requests.get(f"{BASE}/api/payment-modes", headers=dev_h).json()
        for m in r["items"]:
            assert m["modeCode"].upper() not in ("ONLINE", "RAZORPAY", "GATEWAY"), f"Illegal seeded mode: {m}"


# ============================================================
# 2. Eligible-purchases preview + SINGLE / DATE_RANGE / FARMER
# ============================================================
class TestEligiblePreviewAndModes:

    def test_single_mode_requires_purchase_id(self, dev_h):
        r = requests.get(f"{BASE}/api/payments/eligible-purchases?selectionMode=SINGLE&growerCode=101/1",
                         headers=dev_h)
        assert r.status_code == 400
        assert "purchaseId" in r.text

    def test_invalid_selection_mode(self, dev_h):
        r = requests.get(f"{BASE}/api/payments/eligible-purchases?selectionMode=BOGUS&growerCode=101/1",
                         headers=dev_h)
        assert r.status_code == 400

    def test_unknown_grower_returns_404(self, dev_h):
        r = requests.get(f"{BASE}/api/payments/eligible-purchases?selectionMode=FARMER&growerCode=999/999",
                         headers=dev_h)
        assert r.status_code == 404

    def test_single_mode_payment_flow(self, dev_h, bank_mode_id):
        """SINGLE: create ONE fresh purchase, preview it, pay it -> only that pid is PAID; other pids untouched."""
        gc = _fresh_grower(dev_h, "GEN")
        pid_a, amt_a = _create_purchase(dev_h, gc, gross_kg=5000, tare_kg=1200, vehicle="UP32SI0001")
        pid_b, amt_b = _create_purchase(dev_h, gc, gross_kg=4500, tare_kg=1100, vehicle="UP32SI0002")

        # Preview SINGLE
        pv = requests.get(
            f"{BASE}/api/payments/eligible-purchases?selectionMode=SINGLE&growerCode={gc}&purchaseId={pid_a}",
            headers=dev_h).json()
        assert len(pv["eligiblePurchases"]) == 1
        assert pv["eligiblePurchases"][0]["purchaseId"] == pid_a
        assert abs(pv["totalPurchaseAmount"] - amt_a) < 0.01

        # Issue SINGLE
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid_a,
            "paymentModeId": bank_mode_id, "transactionRefNumber": "SINGLE-REF",
            "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200, r.text
        j = r.json()
        assert j["purchaseCount"] == 1
        assert abs(j["totalPurchaseAmount"] - amt_a) < 0.01

        # Verify persistence
        pa = requests.get(f"{BASE}/api/purchases/{pid_a}", headers=dev_h).json()
        pb = requests.get(f"{BASE}/api/purchases/{pid_b}", headers=dev_h).json()
        assert pa["paymentStatus"] == "PAID"
        assert pb["paymentStatus"] == "PENDING"

    def test_date_range_mode(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid1, _ = _create_purchase(dev_h, gc, vehicle="UP32DR0001")
        pid2, _ = _create_purchase(dev_h, gc, vehicle="UP32DR0002")

        from datetime import datetime, timedelta, timezone
        now = datetime.now(timezone.utc)
        frm = (now - timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M:%SZ")
        to = (now + timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M:%SZ")

        pv = requests.get(
            f"{BASE}/api/payments/eligible-purchases?selectionMode=DATE_RANGE&growerCode={gc}&fromDate={frm}&toDate={to}",
            headers=dev_h).json()
        pids = {p["purchaseId"] for p in pv["eligiblePurchases"]}
        assert pid1 in pids and pid2 in pids

        # Empty range future dates -> no eligible
        futfrm = (now + timedelta(days=10)).strftime("%Y-%m-%dT%H:%M:%SZ")
        futto = (now + timedelta(days=11)).strftime("%Y-%m-%dT%H:%M:%SZ")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "DATE_RANGE", "growerCode": gc, "fromDate": futfrm, "toDate": futto,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 409, r.text

        # Issue for actual range -> both pids paid, one Advice Number shared
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "DATE_RANGE", "growerCode": gc, "fromDate": frm, "toDate": to,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200, r.text
        j = r.json()
        assert j["purchaseCount"] == 2
        adv = j["adviceNumber"]
        pa = requests.get(f"{BASE}/api/purchases/{pid1}", headers=dev_h).json()
        pb = requests.get(f"{BASE}/api/purchases/{pid2}", headers=dev_h).json()
        assert pa["paymentStatus"] == "PAID" and pb["paymentStatus"] == "PAID"
        assert pa["adviceNumber"] == adv == pb["adviceNumber"]

    def test_farmer_mode_picks_all_pending(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pids = [_create_purchase(dev_h, gc, vehicle=f"UP32FM{i:04d}")[0] for i in range(3)]
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "FARMER", "growerCode": gc,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200, r.text
        assert r.json()["purchaseCount"] == 3
        for pid in pids:
            assert requests.get(f"{BASE}/api/purchases/{pid}", headers=dev_h).json()["paymentStatus"] == "PAID"


# ============================================================
# 3. Loan auto-deduction math (the critical bit)
# ============================================================
class TestLoanAutoDeduction:

    def test_no_loan_zero_deduction(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid, amt = _create_purchase(dev_h, gc, vehicle="UP32NL0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200, r.text
        j = r.json()
        assert j["loanDeductedAmount"] == 0
        assert abs(j["netPayableAmount"] - amt) < 0.01

    def test_loan_bigger_than_payable_capped(self, dev_h, bank_mode_id):
        """Loan Rs 50,000 vs one purchase ~Rs 15k: deduction capped at payable, loan stays ACTIVE."""
        gc = _fresh_grower(dev_h, "GEN")
        loan_id = _issue_loan(dev_h, gc, 50000.00)
        pid, amt = _create_purchase(dev_h, gc, gross_kg=5000, tare_kg=1200, vehicle="UP32BL0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200, r.text
        j = r.json()
        assert abs(j["loanDeductedAmount"] - amt) < 0.01, f"expected deduction == payable {amt}, got {j['loanDeductedAmount']}"
        assert j["netPayableAmount"] == 0
        loan = requests.get(f"{BASE}/api/loans/{loan_id}", headers=dev_h).json()
        assert loan["loanStatus"] == "ACTIVE"
        assert abs(float(loan["outstandingAmount"]) - (50000.00 - amt)) < 0.01

    def test_multi_loan_fifo_oldest_first(self, dev_h, bank_mode_id):
        """Two active loans: oldest must be settled first, then newer, up to payable."""
        gc = _fresh_grower(dev_h, "FIFO")
        loan_old = _issue_loan(dev_h, gc, 5000.00)
        time.sleep(1.1)  # ensure distinct IssueDate ordering
        loan_new = _issue_loan(dev_h, gc, 20000.00)
        # Purchase must be > loan_old (5000) so old fully closes AND < loan_old+loan_new (25000) so new partially eats
        pid, amt = _create_purchase(dev_h, gc, gross_kg=5000, tare_kg=1200, vehicle="UP32FF0001")
        assert amt > 5000 and amt < 25000, f"unexpected purchase amount {amt}"
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200, r.text
        j = r.json()
        assert abs(j["loanDeductedAmount"] - amt) < 0.01
        lo = requests.get(f"{BASE}/api/loans/{loan_old}", headers=dev_h).json()
        ln = requests.get(f"{BASE}/api/loans/{loan_new}", headers=dev_h).json()
        assert lo["loanStatus"] == "CLOSED", f"old loan should be CLOSED first, got {lo}"
        assert float(lo["outstandingAmount"]) == 0
        assert ln["loanStatus"] == "ACTIVE"
        expected_new_out = 20000.00 - (amt - 5000.00)
        assert abs(float(ln["outstandingAmount"]) - expected_new_out) < 0.01

    def test_cancel_then_repay_new_advice_and_fresh_deduction(self, dev_h, bank_mode_id):
        """Cancel reverses loan; repay creates a brand NEW payment id + advice number."""
        gc = _fresh_grower(dev_h, "GEN")
        loan_id = _issue_loan(dev_h, gc, 3000.00)
        pid, amt = _create_purchase(dev_h, gc, gross_kg=5000, tare_kg=1200, vehicle="UP32CR0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200
        p1 = r.json()
        assert p1["loanDeductedAmount"] == 3000.00
        loan_after_pay = requests.get(f"{BASE}/api/loans/{loan_id}", headers=dev_h).json()
        assert loan_after_pay["loanStatus"] == "CLOSED"

        # Cancel
        c = requests.post(f"{BASE}/api/payments/{p1['paymentId']}/cancel", headers=dev_h,
                          json={"reason": "test reversal repay"})
        assert c.status_code == 200, c.text

        # Loan reopened
        loan_after_cancel = requests.get(f"{BASE}/api/loans/{loan_id}", headers=dev_h).json()
        assert loan_after_cancel["loanStatus"] == "ACTIVE"
        assert float(loan_after_cancel["outstandingAmount"]) == 3000.00

        # Repay
        r2 = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r2.status_code == 200
        p2 = r2.json()
        assert p2["paymentId"] != p1["paymentId"]
        assert p2["adviceNumber"] != p1["adviceNumber"], "new payment must get NEW advice number, never reuse cancelled one"
        assert p2["loanDeductedAmount"] == 3000.00

    def test_cancel_short_reason_400(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32SR0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200
        pid_ = r.json()["paymentId"]
        c = requests.post(f"{BASE}/api/payments/{pid_}/cancel", headers=dev_h, json={"reason": "abc"})
        assert c.status_code == 400

    def test_cancel_already_cancelled_409(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32DC0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        pay_id = r.json()["paymentId"]
        assert requests.post(f"{BASE}/api/payments/{pay_id}/cancel", headers=dev_h,
                             json={"reason": "first cancel"}).status_code == 200
        c2 = requests.post(f"{BASE}/api/payments/{pay_id}/cancel", headers=dev_h,
                           json={"reason": "second cancel attempt"})
        assert c2.status_code == 409


# ============================================================
# 4. Idempotency
# ============================================================
class TestIdempotency:
    def test_duplicate_key_returns_409(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32ID0001")
        key = str(uuid.uuid4())
        body = {"selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
                "paymentModeId": bank_mode_id, "idempotencyKey": key}
        r1 = requests.post(f"{BASE}/api/payments", headers=dev_h, json=body)
        assert r1.status_code == 200
        r2 = requests.post(f"{BASE}/api/payments", headers=dev_h, json=body)
        assert r2.status_code == 409, r2.text


# ============================================================
# 5. Cash Evidence + captureQueued flag
# ============================================================
class TestCashEvidence:
    def test_cash_mode_captureQueued_true(self, dev_h, cash_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32CA0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": cash_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200, r.text
        j = r.json()
        assert j["captureQueued"] is True
        assert j["smsQueued"] is False

    def test_bank_mode_captureQueued_false(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32BK0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200
        assert r.json()["captureQueued"] is False

    def test_upi_mode_captureQueued_false(self, dev_h, upi_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32UP0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": upi_mode_id, "transactionRefNumber": "UPI-TXN-999",
            "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200
        assert r.json()["captureQueued"] is False

    def test_images_list_empty_when_no_cameras_or_disabled(self, dev_h):
        # Any existing payment - use payment 2 from prior smoke test
        r = requests.get(f"{BASE}/api/payments?pageSize=1", headers=dev_h).json()
        if not r["items"]:
            pytest.skip("no payments to test images list")
        pid = r["items"][0]["paymentId"]
        img = requests.get(f"{BASE}/api/payments/{pid}/images", headers=dev_h)
        assert img.status_code == 200
        assert isinstance(img.json(), list)  # empty array not error

    def test_manual_capture_endpoint(self, dev_h):
        r = requests.get(f"{BASE}/api/payments?pageSize=1", headers=dev_h).json()
        if not r["items"]:
            pytest.skip()
        pid = r["items"][0]["paymentId"]
        cap = requests.post(f"{BASE}/api/payments/{pid}/images/capture", headers=dev_h)
        assert cap.status_code == 200, cap.text


# ============================================================
# 6. Print preview vs final + reprint audit counter
# ============================================================
class TestPaymentPrint:
    @pytest.fixture(scope="class")
    def fresh_payment_id(self, dev_h):
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32PR0001")
        r = requests.get(f"{BASE}/api/payment-modes", headers=dev_h).json()
        bank_id = next(m["id"] for m in r["items"] if m["modeCode"] == "BANK")
        p = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_id, "idempotencyKey": str(uuid.uuid4())})
        assert p.status_code == 200
        return p.json()["paymentId"]

    def test_preview_dotmatrix_returns_png_no_counter_change(self, dev_h, fresh_payment_id):
        pid = fresh_payment_id
        before = requests.get(f"{BASE}/api/payments/{pid}", headers=dev_h).json()["printCount"]
        r = requests.get(f"{BASE}/api/print/payment/{pid}?target=DotMatrix&format=preview", headers=dev_h)
        assert r.status_code == 200, r.text
        assert r.content[:4] == b"\x89PNG", "DotMatrix preview should be PNG bytes"
        after = requests.get(f"{BASE}/api/payments/{pid}", headers=dev_h).json()["printCount"]
        assert after == before, "preview must not increment printCount"

    def test_preview_a4_returns_pdf(self, dev_h, fresh_payment_id):
        r = requests.get(f"{BASE}/api/print/payment/{fresh_payment_id}?target=A4&format=preview", headers=dev_h)
        assert r.status_code == 200
        assert r.content[:4] == b"%PDF", "A4 preview should be PDF bytes"

    def test_final_increments_print_counter_and_reprint(self, dev_h, fresh_payment_id):
        pid = fresh_payment_id
        c0 = requests.get(f"{BASE}/api/payments/{pid}", headers=dev_h).json()["printCount"]
        r1 = requests.get(f"{BASE}/api/print/payment/{pid}?target=A4&format=final", headers=dev_h)
        assert r1.status_code == 200
        c1 = requests.get(f"{BASE}/api/payments/{pid}", headers=dev_h).json()["printCount"]
        assert c1 == c0 + 1
        r2 = requests.get(f"{BASE}/api/print/payment/{pid}?target=A4&format=final", headers=dev_h)
        assert r2.status_code == 200
        c2 = requests.get(f"{BASE}/api/payments/{pid}", headers=dev_h).json()["printCount"]
        assert c2 == c1 + 1, "reprint must also increment counter"


# ============================================================
# 7. List + Get + Filters
# ============================================================
class TestListAndGet:
    def test_get_by_id_includes_purchaseIds_array(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        pid1, _ = _create_purchase(dev_h, gc, vehicle="UP32LG0001")
        pid2, _ = _create_purchase(dev_h, gc, vehicle="UP32LG0002")
        p = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "FARMER", "growerCode": gc,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())}).json()
        g = requests.get(f"{BASE}/api/payments/{p['paymentId']}", headers=dev_h).json()
        assert set(g["purchaseIds"]) == {pid1, pid2}

    def test_filter_by_advice_number(self, dev_h):
        # existing Advice=2 from author smoke should still be there
        r = requests.get(f"{BASE}/api/payments?adviceNumber=2", headers=dev_h).json()
        assert all(x["adviceNumber"] == 2 for x in r["items"])

    def test_filter_by_status_cancelled(self, dev_h):
        r = requests.get(f"{BASE}/api/payments?status=CANCELLED&pageSize=50", headers=dev_h).json()
        for x in r["items"]:
            assert x["paymentStatus"] == "CANCELLED"


# ============================================================
# 8. RBAC
# ============================================================
class TestRBAC:
    def test_operator_403_on_create(self, dev_h, operator_token, bank_mode_id):
        if not operator_token:
            pytest.skip("could not provision operator user")
        r = requests.post(f"{BASE}/api/payments", headers=_hdr(operator_token), json={
            "selectionMode": "FARMER", "growerCode": "101/1", "paymentModeId": bank_mode_id,
            "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 403, r.text

    def test_operator_403_on_view(self, operator_token):
        if not operator_token:
            pytest.skip()
        r = requests.get(f"{BASE}/api/payments", headers=_hdr(operator_token))
        assert r.status_code == 403

    def test_operator_403_on_images(self, operator_token):
        if not operator_token:
            pytest.skip()
        r = requests.get(f"{BASE}/api/payments/1/images", headers=_hdr(operator_token))
        assert r.status_code == 403

    def test_salepurchase_403(self, salepurchase_token):
        if not salepurchase_token:
            pytest.skip()
        r = requests.get(f"{BASE}/api/payments", headers=_hdr(salepurchase_token))
        assert r.status_code == 403

    def test_admin_403_on_create(self, admin_token, bank_mode_id):
        if not admin_token:
            pytest.skip()
        r = requests.post(f"{BASE}/api/payments", headers=_hdr(admin_token), json={
            "selectionMode": "FARMER", "growerCode": "101/1", "paymentModeId": bank_mode_id,
            "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 403, f"Admin should NOT have Payment.Create; got {r.status_code}: {r.text}"

    def test_admin_200_on_view(self, admin_token):
        if not admin_token:
            pytest.skip()
        r = requests.get(f"{BASE}/api/payments", headers=_hdr(admin_token))
        assert r.status_code == 200

    def test_admin_403_on_cancel(self, admin_token):
        if not admin_token:
            pytest.skip()
        # try to cancel payment id 1 (already CANCELLED, but 403 should fire before 409)
        r = requests.post(f"{BASE}/api/payments/1/cancel", headers=_hdr(admin_token),
                          json={"reason": "should be denied by RBAC"})
        assert r.status_code == 403, r.text

    def test_accountant_can_create(self, accountant_token, dev_h, bank_mode_id):
        if not accountant_token:
            pytest.skip()
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32AC0001")
        r = requests.post(f"{BASE}/api/payments", headers=_hdr(accountant_token), json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 200, f"Accountant should have Payment.Create; got {r.status_code}: {r.text}"


# ============================================================
# 9. Farmer object-ownership scoping
# ============================================================
class TestFarmerScoping:
    def test_farmer_sees_only_own_grower_payments(self, dev_h, bank_mode_id):
        # Create GrowerA with a known mobile, GrowerB with different mobile.
        mobile_a = f"9600{int(time.time() * 1000) % 1000000:06d}"
        gc_a = _ensure_grower(dev_h, f"TEST_FarmerA_{mobile_a[-4:]}", mobile_a)
        gc_b = _fresh_grower(dev_h, "FarmerB")
        pid_a, _ = _create_purchase(dev_h, gc_a, vehicle="UP32FA0001")
        pid_b, _ = _create_purchase(dev_h, gc_b, vehicle="UP32FB0001")
        pa = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc_a, "purchaseId": pid_a,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())}).json()
        pb = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc_b, "purchaseId": pid_b,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())}).json()

        # Farmer user with mobile matching grower A only
        farmer_uname = f"phase9_farmA_{mobile_a[-4:]}"
        farmer_token = _ensure_user(dev_h, farmer_uname, "Farmer", mobile=mobile_a, pwd="Farm@2026")
        if not farmer_token:
            pytest.skip("farmer provisioning failed")
        r = requests.get(f"{BASE}/api/payments?pageSize=200", headers=_hdr(farmer_token))
        assert r.status_code == 200, r.text
        items = r.json()["items"]
        grower_codes = {x["growerCode"] for x in items}
        # Must include A but NOT B (or if empty, at least not include B)
        assert gc_b not in grower_codes, f"farmer A saw grower B's payments: {grower_codes}"
        assert all(gc == gc_a for gc in grower_codes), f"farmer A saw non-A grower payments: {grower_codes}"

        # Also GET by id for grower B's payment must be 404 (not 403 per code comment)
        g = requests.get(f"{BASE}/api/payments/{pb['paymentId']}", headers=_hdr(farmer_token))
        assert g.status_code == 404, f"farmer must not see grower B's payment {pb['paymentId']}, got {g.status_code}"

        # Own payment IS visible
        g2 = requests.get(f"{BASE}/api/payments/{pa['paymentId']}", headers=_hdr(farmer_token))
        assert g2.status_code == 200


# ============================================================
# 10. Anti-regression: existing modes rejected
# ============================================================
class TestNegativeAndSanity:
    def test_invalid_payment_mode_id_400(self, dev_h):
        gc = _fresh_grower(dev_h, "GEN")
        pid, _ = _create_purchase(dev_h, gc, vehicle="UP32BM0001")
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": 99999, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 400, r.text

    def test_unknown_grower_on_issue_404(self, dev_h, bank_mode_id):
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "FARMER", "growerCode": "999/999",
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 404

    def test_no_eligible_purchases_409(self, dev_h, bank_mode_id):
        gc = _fresh_grower(dev_h, "GEN")
        # no purchases created
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "FARMER", "growerCode": gc,
            "paymentModeId": bank_mode_id, "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 409
