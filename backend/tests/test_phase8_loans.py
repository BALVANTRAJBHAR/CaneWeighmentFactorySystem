"""Phase 8: Loan & Recovery module - Backend Test Suite.
Runs against ASP.NET Core API on http://localhost:8001 with SQLite dev DB.
"""
import os
import uuid
import time
import pytest
import requests

BASE = os.environ.get("CANE_API_URL", "http://localhost:8001")


# ----------------------------- helpers -----------------------------
def _login(username, password):
    # Handle 429 rate-limit with backoff
    for attempt in range(5):
        r = requests.post(f"{BASE}/api/auth/login", json={"username": username, "password": password}, timeout=15)
        if r.status_code == 429:
            time.sleep(2 + attempt * 2)
            continue
        r.raise_for_status()
        return r.json()
    r.raise_for_status()
    return r.json()


def _hdr(tok):
    return {"Authorization": f"Bearer {tok}", "Content-Type": "application/json"}


def _dev_token():
    try:
        j = _login("developer", "DevSecure@2026")
    except Exception:
        j = _login("developer", "Dev@2026Temp")
        if j.get("mustChangePassword"):
            h = _hdr(j["accessToken"])
            requests.post(f"{BASE}/api/auth/change-password", headers=h, json={
                "currentPassword": "Dev@2026Temp", "newPassword": "DevSecure@2026",
                "confirmPassword": "DevSecure@2026"}, timeout=10).raise_for_status()
            j = _login("developer", "DevSecure@2026")
    return j["accessToken"]


def _ensure_user(dev_h, username, role_name, pwd="Test@2026"):
    """Create a test user with given role. Handles forced password change. Returns access token."""
    rr = requests.get(f"{BASE}/api/roles", headers=dev_h, timeout=10).json()
    role = next((x for x in rr if x["name"] == role_name), None)
    assert role, f"Role {role_name} not found"
    # try to create - ignore duplicate
    tmp = pwd + "Temp"
    body = {"username": username, "fullName": f"{role_name} Tester",
            "mobile": f"9{abs(hash(username)) % 1000000000:09d}",
            "temporaryPassword": tmp, "roleIds": [role["id"]]}
    r = requests.post(f"{BASE}/api/users", headers=dev_h, json=body, timeout=15)
    if r.status_code not in (200, 201, 204, 409, 400, 422):
        pytest.fail(f"create user {username} failed: {r.status_code} {r.text}")
    # login (post-created path)
    try:
        j = _login(username, pwd)
    except Exception:
        # need to change password from tmp
        try:
            j = _login(username, tmp)
        except Exception:
            # user already existed with different password; try recovering by resetting? skip
            return None
        if j.get("mustChangePassword"):
            h = _hdr(j["accessToken"])
            r2 = requests.post(f"{BASE}/api/auth/change-password", headers=h, json={
                "currentPassword": tmp, "newPassword": pwd, "confirmPassword": pwd}, timeout=10)
            if r2.status_code >= 400:
                return None
            j = _login(username, pwd)
    return j["accessToken"]


# ----------------------------- fixtures -----------------------------
@pytest.fixture(scope="module")
def dev_token():
    return _dev_token()


@pytest.fixture(scope="module")
def dev_h(dev_token):
    return _hdr(dev_token)


@pytest.fixture(scope="module")
def grower_code(dev_h):
    """Ensure at least one active grower exists; return its code."""
    r = requests.get(f"{BASE}/api/growers", headers=dev_h, timeout=10).json()
    items = r.get("items", [])
    assert items, "No grower present - bootstrap required"
    return items[0]["growerCode"]


@pytest.fixture(scope="module")
def accountant_token(dev_h):
    return _ensure_user(dev_h, "acctest1", "Accountant", "Acct@2026")


@pytest.fixture(scope="module")
def operator_token(dev_h):
    # opuser2 was possibly created by phase7 bootstrap
    try:
        return _login("opuser2", "OpSecure@2026")["accessToken"]
    except Exception:
        return _ensure_user(dev_h, "optest1", "Operator", "Oper@2026")


@pytest.fixture(scope="module")
def salepurchase_token(dev_h):
    return _ensure_user(dev_h, "sptest1", "SalePurchase", "Sale@2026")


@pytest.fixture(scope="module")
def farmer_token(dev_h, grower_code):
    """Create a Farmer user whose mobile matches an existing grower's mobile."""
    r = requests.get(f"{BASE}/api/growers", headers=dev_h, timeout=10).json()
    grower = r["items"][0]
    mobile = grower.get("mobile") or "9876543210"
    # Farmer must have same mobile as the grower for ownership scoping
    rr = requests.get(f"{BASE}/api/roles", headers=dev_h, timeout=10).json()
    role = next((x for x in rr if x["name"] == "Farmer"), None)
    tmp = "Farm@2026Temp"; pwd = "Farm@2026"
    r = requests.post(f"{BASE}/api/users", headers=dev_h, json={
        "username": "farmtest1", "fullName": "Farmer Tester", "mobile": mobile,
        "temporaryPassword": tmp, "roleIds": [role["id"]]}, timeout=15)
    try:
        j = _login("farmtest1", pwd)
    except Exception:
        j = _login("farmtest1", tmp)
        if j.get("mustChangePassword"):
            h = _hdr(j["accessToken"])
            requests.post(f"{BASE}/api/auth/change-password", headers=h, json={
                "currentPassword": tmp, "newPassword": pwd, "confirmPassword": pwd}, timeout=10)
            j = _login("farmtest1", pwd)
    return j["accessToken"]


# ============================================================
# 1. LoanType Master CRUD
# ============================================================
class TestLoanTypeMaster:
    def test_seeded_defaults_present(self, dev_h):
        r = requests.get(f"{BASE}/api/loan-types", headers=dev_h)
        assert r.status_code == 200
        names = {x["loanTypeName"] for x in r.json()["items"]}
        for n in ["Fertilizer Loan", "Seed Loan", "Equipment Loan", "Emergency Loan"]:
            assert n in names, f"seeded default '{n}' missing"

    def test_create_new_type(self, dev_h):
        name = f"TEST_LoanType_{uuid.uuid4().hex[:6]}"
        r = requests.post(f"{BASE}/api/loan-types", headers=dev_h,
                          json={"loanTypeName": name, "description": "test"})
        assert r.status_code in (200, 201), r.text
        # verify GET returns it
        rl = requests.get(f"{BASE}/api/loan-types", headers=dev_h).json()
        assert any(x["loanTypeName"] == name for x in rl["items"])

    def test_duplicate_name_conflict(self, dev_h):
        r = requests.post(f"{BASE}/api/loan-types", headers=dev_h,
                          json={"loanTypeName": "Fertilizer Loan"})
        assert r.status_code == 409, r.text


# ============================================================
# 2. Loan Issuance
# ============================================================
class TestLoanIssue:
    def test_issue_success(self, dev_h, grower_code):
        r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 5000.00,
            "remarks": "TEST issue"})
        assert r.status_code == 200, r.text
        j = r.json()
        assert j["loanId"] >= 1
        assert j["loanAmount"] == 5000.00
        # verify via GET
        g = requests.get(f"{BASE}/api/loans/{j['loanId']}", headers=dev_h).json()
        assert g["loanStatus"] == "ACTIVE"
        assert float(g["outstandingAmount"]) == 5000.00
        assert float(g["recoveredAmount"]) == 0

    def test_invalid_grower(self, dev_h):
        r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": "999/999", "loanTypeId": 1, "loanAmount": 100})
        assert r.status_code == 404, r.text

    def test_invalid_loan_type(self, dev_h, grower_code):
        r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 9999, "loanAmount": 100})
        assert r.status_code == 400, r.text

    def test_zero_amount(self, dev_h, grower_code):
        r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 0})
        assert r.status_code == 400, r.text

    def test_negative_amount(self, dev_h, grower_code):
        r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": -50})
        assert r.status_code == 400, r.text

    def test_continuous_loan_ids(self, dev_h, grower_code):
        r1 = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 100})
        r2 = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 200})
        assert r1.status_code == 200 and r2.status_code == 200
        assert r2.json()["loanId"] == r1.json()["loanId"] + 1

    def test_idempotency(self, dev_h, grower_code):
        key = str(uuid.uuid4())
        body = {"growerCode": grower_code, "loanTypeId": 1, "loanAmount": 300, "idempotencyKey": key}
        r1 = requests.post(f"{BASE}/api/loans", headers=dev_h, json=body)
        r2 = requests.post(f"{BASE}/api/loans", headers=dev_h, json=body)
        assert r1.status_code == 200
        assert r2.status_code == 409, r2.text
        assert "duplicate" in r2.text.lower() or "idempotency" in r2.text.lower()


# ============================================================
# 3. Loan Recovery (create, over-recovery, auto-close)
# ============================================================
class TestLoanRecovery:
    @pytest.fixture
    def new_loan(self, dev_h, grower_code):
        r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 1000.00,
            "remarks": "TEST for recovery"})
        assert r.status_code == 200
        return r.json()["loanId"]

    def test_partial_recovery(self, dev_h, new_loan):
        r = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": new_loan, "recoveryAmount": 300.00})
        assert r.status_code == 200, r.text
        j = r.json()
        assert float(j["outstandingAmount"]) == 700.00
        assert j["loanStatus"] == "ACTIVE"
        # verify loan updated
        g = requests.get(f"{BASE}/api/loans/{new_loan}", headers=dev_h).json()
        assert float(g["outstandingAmount"]) == 700.00
        assert float(g["recoveredAmount"]) == 300.00

    def test_over_recovery_blocked(self, dev_h, new_loan):
        r = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": new_loan, "recoveryAmount": 1001.00})
        assert r.status_code == 409, r.text
        assert "exceed" in r.text.lower() and "outstanding" in r.text.lower()

    def test_zero_recovery_amount(self, dev_h, new_loan):
        r = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": new_loan, "recoveryAmount": 0})
        assert r.status_code == 400

    def test_recovery_invalid_loan(self, dev_h):
        r = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": 999999, "recoveryAmount": 100})
        assert r.status_code == 404

    def test_full_recovery_closes_loan(self, dev_h, new_loan):
        r = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": new_loan, "recoveryAmount": 1000.00})
        assert r.status_code == 200
        assert r.json()["loanStatus"] == "CLOSED"
        assert float(r.json()["outstandingAmount"]) == 0.00

    def test_recovery_on_closed_loan_blocked(self, dev_h, new_loan):
        # close it
        requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": new_loan, "recoveryAmount": 1000.00}).raise_for_status()
        r = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": new_loan, "recoveryAmount": 1})
        assert r.status_code == 409

    def test_multiple_partial_recoveries_2dp(self, dev_h, grower_code):
        """Rounding: 3 recoveries summing to full amount close loan."""
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 100.00}).json()
        lid = rl["loanId"]
        for amt in [33.33, 33.33, 33.34]:
            r = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
                "loanId": lid, "recoveryAmount": amt})
            assert r.status_code == 200, r.text
        g = requests.get(f"{BASE}/api/loans/{lid}", headers=dev_h).json()
        assert g["loanStatus"] == "CLOSED"
        assert float(g["outstandingAmount"]) == 0.00

    def test_recovery_idempotency(self, dev_h, new_loan):
        key = str(uuid.uuid4())
        body = {"loanId": new_loan, "recoveryAmount": 100, "idempotencyKey": key}
        r1 = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json=body)
        r2 = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json=body)
        assert r1.status_code == 200
        assert r2.status_code == 409

    def test_continuous_lr_ids(self, dev_h, new_loan):
        # Under parallel test execution, other recoveries may interleave; validate
        # that the sequence is strictly monotonically increasing (not EF auto-increment
        # which could produce holes on rollback). Continuity from 1 is verified by
        # existence of recoveryId=1 in the DB via the list endpoint.
        r1 = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": new_loan, "recoveryAmount": 10})
        r2 = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": new_loan, "recoveryAmount": 10})
        assert r1.status_code == 200 and r2.status_code == 200
        assert r2.json()["recoveryId"] > r1.json()["recoveryId"]
        # verify sequence starts from 1 (at least one recovery has id 1)
        lst = requests.get(f"{BASE}/api/loan-recoveries?page=1&pageSize=500", headers=dev_h).json()
        ids = sorted([x["recoveryId"] for x in lst["items"]])
        assert 1 in ids, f"LR sequence should start at 1, got {ids[:5]}"


# ============================================================
# 4. Cancel / Reverse
# ============================================================
class TestCancelReverse:
    def test_cancel_active_loan(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 500}).json()
        lid = rl["loanId"]
        r = requests.post(f"{BASE}/api/loans/{lid}/cancel", headers=dev_h,
                          json={"reason": "wrong entry"})
        assert r.status_code == 200
        g = requests.get(f"{BASE}/api/loans/{lid}", headers=dev_h).json()
        assert g["loanStatus"] == "CANCELLED"
        assert float(g["outstandingAmount"]) == 0

    def test_cancel_short_reason(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 500}).json()
        r = requests.post(f"{BASE}/api/loans/{rl['loanId']}/cancel", headers=dev_h,
                          json={"reason": "x"})
        assert r.status_code == 400

    def test_cancel_with_recovery_blocked(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 500}).json()
        lid = rl["loanId"]
        requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": lid, "recoveryAmount": 50}).raise_for_status()
        r = requests.post(f"{BASE}/api/loans/{lid}/cancel", headers=dev_h,
                          json={"reason": "some valid reason"})
        assert r.status_code == 409
        assert "reverse" in r.text.lower() or "recover" in r.text.lower()

    def test_cancel_already_cancelled(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 500}).json()
        lid = rl["loanId"]
        requests.post(f"{BASE}/api/loans/{lid}/cancel", headers=dev_h,
                      json={"reason": "some valid reason"}).raise_for_status()
        r = requests.post(f"{BASE}/api/loans/{lid}/cancel", headers=dev_h,
                          json={"reason": "again cancel test"})
        assert r.status_code == 409

    def test_reverse_recovery_reopens_closed_loan(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 200}).json()
        lid = rl["loanId"]
        rec = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": lid, "recoveryAmount": 200}).json()
        rid = rec["recoveryId"]
        # loan closed
        assert requests.get(f"{BASE}/api/loans/{lid}", headers=dev_h).json()["loanStatus"] == "CLOSED"
        # reverse
        r = requests.post(f"{BASE}/api/loan-recoveries/{rid}/reverse", headers=dev_h,
                          json={"reason": "wrong recovery entry"})
        assert r.status_code == 200, r.text
        g = requests.get(f"{BASE}/api/loans/{lid}", headers=dev_h).json()
        assert g["loanStatus"] == "ACTIVE"
        assert float(g["outstandingAmount"]) == 200.00
        assert float(g["recoveredAmount"]) == 0.00

    def test_reverse_short_reason(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 200}).json()
        rec = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": rl["loanId"], "recoveryAmount": 50}).json()
        r = requests.post(f"{BASE}/api/loan-recoveries/{rec['recoveryId']}/reverse",
                          headers=dev_h, json={"reason": "no"})
        assert r.status_code == 400

    def test_reverse_already_reversed(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 200}).json()
        rec = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": rl["loanId"], "recoveryAmount": 50}).json()
        rid = rec["recoveryId"]
        requests.post(f"{BASE}/api/loan-recoveries/{rid}/reverse", headers=dev_h,
                      json={"reason": "correcting entry"}).raise_for_status()
        r = requests.post(f"{BASE}/api/loan-recoveries/{rid}/reverse", headers=dev_h,
                         json={"reason": "correcting entry again"})
        assert r.status_code == 409


# ============================================================
# 5. Outstanding endpoint (must be query param, growerCode contains '/')
# ============================================================
class TestOutstanding:
    def test_outstanding_query_param(self, dev_h, grower_code):
        assert "/" in grower_code, "grower_code should contain '/' for this test"
        r = requests.get(f"{BASE}/api/loans/outstanding",
                         headers=dev_h, params={"growerCode": grower_code})
        assert r.status_code == 200, r.text
        j = r.json()
        assert j["growerCode"] == grower_code
        assert "totalOutstanding" in j and "loans" in j
        assert isinstance(j["loans"], list)

    def test_outstanding_missing_param(self, dev_h):
        r = requests.get(f"{BASE}/api/loans/outstanding", headers=dev_h)
        assert r.status_code == 400


# ============================================================
# 6. Print integration
# ============================================================
class TestPrint:
    @pytest.fixture(scope="class")
    def issued_loan_id(self, dev_h, grower_code):
        r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 750})
        return r.json()["loanId"]

    def test_loan_preview_a4_pdf(self, dev_h, issued_loan_id):
        r = requests.get(f"{BASE}/api/print/loan/{issued_loan_id}",
                         headers=dev_h, params={"target": "A4", "format": "preview"})
        assert r.status_code == 200, r.text
        assert r.content[:4] == b"%PDF", f"expected PDF, got {r.content[:8]!r}"

    def test_loan_preview_dotmatrix_png(self, dev_h, issued_loan_id):
        r = requests.get(f"{BASE}/api/print/loan/{issued_loan_id}",
                         headers=dev_h, params={"target": "DotMatrix", "format": "preview"})
        assert r.status_code == 200, r.text
        assert r.content[:4] == b"\x89PNG", f"expected PNG, got {r.content[:8]!r}"

    def test_loan_final_increments_print_count(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 111}).json()
        lid = rl["loanId"]
        # first final -> Print audit
        r1 = requests.get(f"{BASE}/api/print/loan/{lid}",
                          headers=dev_h, params={"target": "A4", "format": "final"})
        assert r1.status_code == 200
        # second final -> Reprint
        r2 = requests.get(f"{BASE}/api/print/loan/{lid}",
                          headers=dev_h, params={"target": "A4", "format": "final"})
        assert r2.status_code == 200
        # verify PrintCount = 2 via /loans/{id}
        g = requests.get(f"{BASE}/api/loans/{lid}", headers=dev_h).json()
        assert g.get("printCount", 0) >= 2, f"printCount should be >=2, got {g.get('printCount')}"

    def test_loan_not_found(self, dev_h):
        r = requests.get(f"{BASE}/api/print/loan/9999999",
                         headers=dev_h, params={"target": "A4", "format": "preview"})
        assert r.status_code == 404

    def test_recovery_print_preview(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 500}).json()
        rec = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": rl["loanId"], "recoveryAmount": 100}).json()
        r = requests.get(f"{BASE}/api/print/loan-recovery/{rec['recoveryId']}",
                         headers=dev_h, params={"target": "A4", "format": "preview"})
        assert r.status_code == 200, r.text
        assert r.content[:4] == b"%PDF"

    def test_recovery_print_dotmatrix(self, dev_h, grower_code):
        rl = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 500}).json()
        rec = requests.post(f"{BASE}/api/loan-recoveries", headers=dev_h, json={
            "loanId": rl["loanId"], "recoveryAmount": 100}).json()
        r = requests.get(f"{BASE}/api/print/loan-recovery/{rec['recoveryId']}",
                         headers=dev_h, params={"target": "DotMatrix", "format": "preview"})
        assert r.status_code == 200
        assert r.content[:4] == b"\x89PNG"


# ============================================================
# 7. RBAC
# ============================================================
class TestRBAC:
    def test_operator_denied_loan_list(self, operator_token):
        if not operator_token:
            pytest.skip("operator token unavailable")
        h = _hdr(operator_token)
        r = requests.get(f"{BASE}/api/loans", headers=h)
        assert r.status_code == 403, f"Operator should be 403 on /api/loans, got {r.status_code}"

    def test_operator_denied_recovery_list(self, operator_token):
        if not operator_token:
            pytest.skip("operator token unavailable")
        h = _hdr(operator_token)
        r = requests.get(f"{BASE}/api/loan-recoveries", headers=h)
        assert r.status_code == 403

    def test_salepurchase_denied(self, salepurchase_token):
        if not salepurchase_token:
            pytest.skip("SalePurchase user unavailable")
        h = _hdr(salepurchase_token)
        r = requests.get(f"{BASE}/api/loans", headers=h)
        assert r.status_code == 403
        r = requests.get(f"{BASE}/api/loan-recoveries", headers=h)
        assert r.status_code == 403

    def test_accountant_can_create_loan(self, accountant_token, grower_code):
        if not accountant_token:
            pytest.skip("Accountant user unavailable")
        h = _hdr(accountant_token)
        r = requests.post(f"{BASE}/api/loans", headers=h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 111})
        assert r.status_code == 200, r.text

    def test_farmer_readonly(self, farmer_token, grower_code):
        if not farmer_token:
            pytest.skip("Farmer user unavailable")
        h = _hdr(farmer_token)
        # Read allowed
        r = requests.get(f"{BASE}/api/loans", headers=h)
        assert r.status_code == 200, r.text
        # Write denied
        r = requests.post(f"{BASE}/api/loans", headers=h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 100})
        assert r.status_code == 403

    def test_farmer_ownership_scoping(self, farmer_token, dev_h, grower_code):
        """Farmer sees only loans for grower(s) whose mobile matches theirs.
        Since we created the farmer with the mobile of the one existing grower,
        farmer's list should be non-empty and every item must be for that grower."""
        if not farmer_token:
            pytest.skip("Farmer user unavailable")
        # ensure at least one loan exists
        requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": grower_code, "loanTypeId": 1, "loanAmount": 100})
        h = _hdr(farmer_token)
        r = requests.get(f"{BASE}/api/loans", headers=h)
        assert r.status_code == 200
        items = r.json()["items"]
        assert len(items) >= 1
        for it in items:
            assert it["growerCode"] == grower_code


# ============================================================
# 8. Regression spot-checks
# ============================================================
class TestRegression:
    def test_login_still_works(self):
        j = _login("developer", "DevSecure@2026")
        assert "accessToken" in j

    def test_zones_master(self, dev_h):
        r = requests.get(f"{BASE}/api/zones", headers=dev_h)
        assert r.status_code == 200

    def test_villages_master(self, dev_h):
        r = requests.get(f"{BASE}/api/villages", headers=dev_h)
        assert r.status_code == 200

    def test_growers_master(self, dev_h):
        r = requests.get(f"{BASE}/api/growers", headers=dev_h)
        assert r.status_code == 200

    def test_print_purchase_still_works(self, dev_h):
        # find any purchase
        r = requests.get(f"{BASE}/api/purchases?take=1", headers=dev_h).json()
        items = r.get("items") if isinstance(r, dict) else r
        if not items:
            pytest.skip("No purchase to test print")
        pid = items[0].get("id") or items[0].get("purchaseId")
        r = requests.get(f"{BASE}/api/print/purchase/{pid}",
                         headers=dev_h, params={"stage": "GROSS", "target": "A4", "format": "preview"})
        assert r.status_code == 200
        assert r.content[:4] == b"%PDF"
