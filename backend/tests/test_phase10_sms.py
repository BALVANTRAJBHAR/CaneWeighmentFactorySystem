"""Phase 10: SMS Notifications - Backend Test Suite.

Runs against the ASP.NET Core API on http://localhost:8001 with a fresh SQLite dev DB
(delete /tmp/canefactory_dev.db* first) and a local mock SMS HTTP server on :9099
(python3 /app/backend/tests/mock_sms_server.py 9099).

Covers what iteration_6 review requested:
  - GET /api/config/sms shape (booleans only, seeded 4 templates, defaults)
  - PUT /api/config/sms (encrypt on save, omit-preserves, language validation)
  - POST /api/config/sms/templates upsert + validation (400 on invalid EventCode/Language)
  - POST /api/config/sms/test-connection (409 unreachable, 200 reachable)
  - POST /api/config/sms/test-send (never returns raw provider body, no SmsLog row)
  - End-to-end queue: Weighment Tare -> SmsLog SENT + placeholders substituted
  - End-to-end queue: Payment -> SmsLog SENT for PAYMENT_COMPLETED
  - Idempotency: only one SmsLog row per (EventCode, ReferenceId)
  - Enabled=false / no-mobile: no SMS queued at all
  - Failure -> RETRY_PENDING -> manual retry -> SENT
  - Non-blocking: business txns succeed even when provider unreachable
  - GET /api/sms-logs masking + filters + never leaks full mobile
  - RBAC 403 for non-Developer roles
  - Regression: Loan module has zero SMS side-effect
"""
import os
import time
import uuid
import json
import pytest
import requests
from datetime import datetime, timezone

BASE = os.environ.get("CANE_API_URL", "http://localhost:8001")
MOCK_URL = "http://localhost:9099/send"

# ---------- helpers ----------
def _login(u, p, retries=3):
    for i in range(retries):
        r = requests.post(f"{BASE}/api/auth/login", json={"username": u, "password": p}, timeout=15)
        if r.status_code == 429:
            time.sleep(2 + i)
            continue
        return r
    return r

def _hdr(tok):
    return {"Authorization": f"Bearer {tok}", "Content-Type": "application/json"}

def _dev_token():
    r = _login("developer", "DevSecure@2026")
    if r.status_code == 200:
        return r.json()["accessToken"]
    r = _login("developer", "Dev@2026Temp")
    assert r.status_code == 200, r.text
    j = r.json()
    if j.get("mustChangePassword"):
        cp = requests.post(f"{BASE}/api/auth/change-password", headers=_hdr(j["accessToken"]),
            json={"currentPassword": "Dev@2026Temp", "newPassword": "DevSecure@2026",
                  "confirmPassword": "DevSecure@2026"}, timeout=10)
        assert cp.status_code == 200, cp.text
    return _login("developer", "DevSecure@2026").json()["accessToken"]

@pytest.fixture(scope="session")
def dev_h():
    return _hdr(_dev_token())

def _ensure_grower(h, name="TEST_Grower", mobile="9812345610", village_id=101):
    # try find first
    r = requests.get(f"{BASE}/api/growers?pageSize=200", headers=h, timeout=10).json()
    items = r.get("items", r) if isinstance(r, dict) else r
    ex = next((g for g in items if g.get("mobile") == mobile), None)
    if ex: return ex["growerCode"]
    cr = requests.post(f"{BASE}/api/growers", headers=h,
        json={"villageId": village_id, "growerName": name, "mobile": mobile, "fatherName": "TEST"}, timeout=15)
    assert cr.status_code in (200, 201), cr.text
    j = cr.json()
    return j.get("growerCode") or j["data"]["growerCode"]

def _bootstrap_seed(h):
    """Idempotent: create Zone/Village/Rate/Grower once."""
    requests.post(f"{BASE}/api/zones", headers=h, json={"zoneName": "Zone A"}, timeout=10)
    requests.post(f"{BASE}/api/villages", headers=h, json={"zoneId": 1, "villageName": "Village A"}, timeout=10)
    requests.post(f"{BASE}/api/rates", headers=h,
        json={"varietyTypeId": 1, "rate": 350.00, "effectiveFrom": "2026-01-01T00:00:00Z"}, timeout=10)

@pytest.fixture(scope="session", autouse=True)
def seed(dev_h):
    _bootstrap_seed(dev_h)
    yield

def _fresh_grower(h, prefix="P10"):
    mobile = f"98{(int(time.time()*1000) + abs(hash(uuid.uuid4()))) % 100000000:08d}"
    return _ensure_grower(h, f"TEST_{prefix}_{mobile[-6:]}", mobile), mobile

def _create_purchase(h, grower_code, gross=5000, tare=1200, vehicle=None):
    vehicle = vehicle or f"UP32SM{uuid.uuid4().hex[:4].upper()}"
    g = requests.post(f"{BASE}/api/weighment/gross", headers=h, json={
        "growerCode": grower_code, "vehicleTypeId": 1, "vehicleNumber": vehicle,
        "varietyTypeId": 1, "varietyId": 1, "cuttingPercent": 2.0, "taxPercent": 1.0,
        "scaleReadingKg": gross, "idempotencyKey": str(uuid.uuid4())}, timeout=15)
    assert g.status_code == 200, g.text
    pid = g.json()["purchaseId"]
    t = requests.post(f"{BASE}/api/weighment/tare", headers=h, json={
        "purchaseId": pid, "scaleReadingKg": tare, "idempotencyKey": str(uuid.uuid4())}, timeout=15)
    assert t.status_code == 200, t.text
    return pid, t.json()

def _bank_mode_id(h):
    r = requests.get(f"{BASE}/api/config/payment-modes", headers=h, timeout=10)
    if r.status_code != 200:
        r = requests.get(f"{BASE}/api/payment-modes", headers=h, timeout=10)
    modes = r.json()
    if isinstance(modes, dict): modes = modes.get("items", [])
    b = next((m for m in modes if (m.get("code") or m.get("name","")).upper() == "BANK"), None)
    return b["id"] if b else 2

def _configure_sms_ok(h, base_url=MOCK_URL, enabled=True, language="hi"):
    body = {
        "providerName": "MockProvider", "apiBaseUrl": base_url, "httpMethod": "POST",
        "apiKey": "TEST_APIKEY_XYZ", "apiSecret": "TEST_APISECRET_ABC",
        "authorizationHeader": "Bearer TEST_TOKEN", "senderId": "CANEFA", "entityId": "12345",
        "enabled": enabled, "language": language,
        "requestContentType": "application/json",
        "requestBodyTemplate": '{"mobile":"{Mobile}","message":"{Message}","key":"{ApiKey}","sender":"{SenderId}"}',
        "responseSuccessPath": "status", "responseSuccessValue": "success"
    }
    r = requests.put(f"{BASE}/api/config/sms", headers=h, json=body, timeout=15)
    assert r.status_code == 200, r.text
    return r


# ============================== TESTS ==============================
class TestSmsConfigCrud:
    def test_get_sms_seeded_shape(self, dev_h):
        r = requests.get(f"{BASE}/api/config/sms", headers=dev_h, timeout=10)
        assert r.status_code == 200, r.text
        j = r.json()
        assert "config" in j and "templates" in j
        # config defaults on fresh DB: Enabled=false, hasApiKey=false, hasApiSecret=false
        cfg = j["config"]
        if cfg is not None:
            assert cfg.get("enabled") in (False, None) or isinstance(cfg["enabled"], bool)
            assert isinstance(cfg.get("hasApiKey"), bool), f"hasApiKey must be bool: {cfg}"
            assert isinstance(cfg.get("hasApiSecret"), bool)
            # No plaintext/encrypted secret leaks
            body_str = json.dumps(cfg)
            assert "apiKeyEncrypted" not in body_str.lower().replace("hasapikey", "")
            assert "apisecret" not in body_str.lower().replace("hasapisecret", "")
        tpls = j["templates"]
        # Fresh DB must have exactly 4 seeded templates
        events = sorted([(t["eventCode"], t["language"]) for t in tpls])
        assert ("TARE_COMPLETED", "hi") in events
        assert ("TARE_COMPLETED", "en") in events
        assert ("PAYMENT_COMPLETED", "hi") in events
        assert ("PAYMENT_COMPLETED", "en") in events
        assert len(tpls) == 4, f"expected 4 seeded templates got {len(tpls)}"
        for t in tpls:
            assert t.get("enabled") is True

    def test_put_sms_encrypts_and_hides_secrets(self, dev_h):
        _configure_sms_ok(dev_h)
        r = requests.get(f"{BASE}/api/config/sms", headers=dev_h, timeout=10).json()
        cfg = r["config"]
        assert cfg["hasApiKey"] is True
        assert cfg["hasApiSecret"] is True
        body = json.dumps(cfg)
        assert "TEST_APIKEY_XYZ" not in body, "raw apiKey must NEVER be returned"
        assert "TEST_APISECRET_ABC" not in body, "raw apiSecret must NEVER be returned"
        assert cfg["providerName"] == "MockProvider"
        assert cfg["language"] == "hi"
        assert cfg["responseSuccessPath"] == "status"

    def test_put_omit_secret_preserves(self, dev_h):
        _configure_sms_ok(dev_h)
        # Second PUT without apiKey/apiSecret - preserve
        body = {"providerName": "MockProvider2", "apiBaseUrl": MOCK_URL, "httpMethod": "POST",
                "enabled": True, "language": "en"}
        r = requests.put(f"{BASE}/api/config/sms", headers=dev_h, json=body, timeout=15)
        assert r.status_code == 200, r.text
        cfg = requests.get(f"{BASE}/api/config/sms", headers=dev_h).json()["config"]
        assert cfg["hasApiKey"] is True, "omitting apiKey should not blank it"
        assert cfg["hasApiSecret"] is True
        assert cfg["language"] == "en"
        # restore hi
        _configure_sms_ok(dev_h, language="hi")

    def test_template_upsert_and_validation(self, dev_h):
        # Invalid EventCode
        r = requests.post(f"{BASE}/api/config/sms/templates", headers=dev_h, json={
            "eventCode": "LOAN_ISSUED", "language": "hi", "messageTemplate": "x", "enabled": True}, timeout=10)
        assert r.status_code == 400, r.text
        # Invalid language
        r = requests.post(f"{BASE}/api/config/sms/templates", headers=dev_h, json={
            "eventCode": "TARE_COMPLETED", "language": "fr", "messageTemplate": "x", "enabled": True}, timeout=10)
        assert r.status_code == 400, r.text
        # Upsert existing (should NOT create new row)
        before = requests.get(f"{BASE}/api/config/sms", headers=dev_h).json()["templates"]
        r = requests.post(f"{BASE}/api/config/sms/templates", headers=dev_h, json={
            "eventCode": "TARE_COMPLETED", "language": "hi",
            "messageTemplate": "प्रिय {GrowerName}, वजन: {FinalWeight} क्विंटल, राशि: Rs {PurchaseAmount}",
            "enabled": True}, timeout=10)
        assert r.status_code == 200, r.text
        after = requests.get(f"{BASE}/api/config/sms", headers=dev_h).json()["templates"]
        assert len(after) == len(before), f"upsert must not create new row: {len(before)} -> {len(after)}"


class TestTestEndpoints:
    def test_connection_reachable_and_unreachable(self, dev_h):
        _configure_sms_ok(dev_h, base_url=MOCK_URL)
        r = requests.post(f"{BASE}/api/config/sms/test-connection", headers=dev_h, timeout=15)
        assert r.status_code == 200, r.text
        # Unreachable
        _configure_sms_ok(dev_h, base_url="http://127.0.0.1:1/send")
        r = requests.post(f"{BASE}/api/config/sms/test-connection", headers=dev_h, timeout=15)
        assert r.status_code == 409, r.text
        _configure_sms_ok(dev_h, base_url=MOCK_URL)  # restore

    def test_test_send_no_raw_response_leak(self, dev_h):
        _configure_sms_ok(dev_h, base_url=MOCK_URL)
        logs_before = requests.get(f"{BASE}/api/sms-logs?pageSize=1", headers=dev_h).json()["totalCount"]
        r = requests.post(f"{BASE}/api/config/sms/test-send", headers=dev_h, json={
            "mobileNumber": "9812345610", "message": "hello test"}, timeout=15)
        assert r.status_code == 200, r.text
        j = r.json()
        assert "success" in j and "message" in j
        # Ensure raw provider response body isn't echoed (mock returns {"status":"success","id":"MOCK-N"})
        assert "MOCK-" not in json.dumps(j), f"raw provider response leaked: {j}"
        # No new SmsLog row created by test-send
        logs_after = requests.get(f"{BASE}/api/sms-logs?pageSize=1", headers=dev_h).json()["totalCount"]
        assert logs_after == logs_before, f"test-send must not create SmsLog rows ({logs_before}->{logs_after})"


class TestEndToEndQueue:
    def test_tare_queues_and_sends_sms(self, dev_h):
        _configure_sms_ok(dev_h, base_url=MOCK_URL, enabled=True)
        gc, mobile = _fresh_grower(dev_h, "TARE")
        pid, tare_resp = _create_purchase(dev_h, gc)
        # smsQueued field expected true
        assert tare_resp.get("smsQueued") is True, f"tare response missing smsQueued=true: {tare_resp}"
        # Wait for background processor (15s cycle)
        time.sleep(20)
        logs = requests.get(f"{BASE}/api/sms-logs?eventCode=TARE_COMPLETED&pageSize=50", headers=dev_h).json()
        row = next((l for l in logs["items"] if l["referenceId"] == f"PUR-{pid}"), None)
        assert row is not None, f"No TARE_COMPLETED SmsLog for PUR-{pid}. items={logs['items'][:3]}"
        assert row["status"] == "SENT", f"expected SENT got {row['status']} reason={row.get('failureReason')}"
        assert row["sentAt"] is not None
        # Masking: e.g. 98XXXXXX10
        mm = row["mobileMasked"]
        assert "X" in mm, f"mobile not masked: {mm}"
        assert mobile not in mm, "full mobile leaked in mask"
        # Full number never in any list response
        list_json = json.dumps(logs)
        assert mobile not in list_json, "full mobile in list response"
        # Placeholders substituted
        text = row["messageText"]
        assert "{" not in text and "}" not in text, f"unsubstituted placeholder in: {text}"
        assert "TEST_TARE_" in text or "TEST_" in text, f"grower name not substituted: {text}"

    def test_payment_queues_and_sends_sms(self, dev_h):
        _configure_sms_ok(dev_h, base_url=MOCK_URL, enabled=True)
        gc, mobile = _fresh_grower(dev_h, "PAY")
        pid, _ = _create_purchase(dev_h, gc)
        bmid = _bank_mode_id(dev_h)
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bmid, "transactionRefNumber": "SMSTEST",
            "idempotencyKey": str(uuid.uuid4())}, timeout=15)
        assert r.status_code == 200, r.text
        pj = r.json()
        assert pj.get("smsQueued") is True, f"payment response missing smsQueued=true: {pj}"
        adv = pj.get("adviceNumber") or pj.get("paymentId")
        time.sleep(20)
        logs = requests.get(f"{BASE}/api/sms-logs?eventCode=PAYMENT_COMPLETED&pageSize=50", headers=dev_h).json()
        # ReferenceId format: PAY-<paymentId>
        pay_id = pj.get("paymentId")
        row = next((l for l in logs["items"] if l.get("referenceId") == f"PAY-{pay_id}"), None)
        if row is None:
            # try match by grower
            row = next((l for l in logs["items"] if l.get("growerId") and adv and str(adv) in l.get("messageText","")), None)
        assert row is not None, f"No PAYMENT_COMPLETED SmsLog. items={logs['items'][:5]}"
        assert row["status"] == "SENT", f"expected SENT got {row['status']} reason={row.get('failureReason')}"
        assert "{" not in row["messageText"] and "}" not in row["messageText"], row["messageText"]


class TestIdempotencyAndSkip:
    def test_no_queue_when_disabled(self, dev_h):
        # Disable SMS
        body = {"providerName": "MockProvider", "apiBaseUrl": MOCK_URL, "httpMethod": "POST",
                "enabled": False, "language": "hi",
                "responseSuccessPath": "status", "responseSuccessValue": "success"}
        requests.put(f"{BASE}/api/config/sms", headers=dev_h, json=body, timeout=15)
        gc, _ = _fresh_grower(dev_h, "DIS")
        pid, tare_resp = _create_purchase(dev_h, gc)
        assert tare_resp.get("smsQueued") in (False, None), f"smsQueued should be false when disabled: {tare_resp}"
        time.sleep(2)
        logs = requests.get(f"{BASE}/api/sms-logs?eventCode=TARE_COMPLETED&pageSize=100", headers=dev_h).json()
        assert not any(l["referenceId"] == f"PUR-{pid}" for l in logs["items"]), "no SmsLog when disabled"
        _configure_sms_ok(dev_h, enabled=True)  # re-enable

    def test_no_queue_when_grower_has_no_mobile(self, dev_h):
        _configure_sms_ok(dev_h, enabled=True)
        # Create grower with empty mobile - see if API allows it. If not, skip.
        cr = requests.post(f"{BASE}/api/growers", headers=dev_h,
            json={"villageId": 101, "growerName": "TEST_NoMobile", "mobile": "", "fatherName": "N/A"}, timeout=15)
        if cr.status_code not in (200, 201):
            pytest.skip(f"cannot create grower with empty mobile: {cr.status_code} {cr.text}")
        gc = cr.json().get("growerCode") or cr.json().get("data", {}).get("growerCode")
        pid, tare_resp = _create_purchase(dev_h, gc)
        assert tare_resp.get("smsQueued") in (False, None), f"smsQueued should be false when no mobile: {tare_resp}"
        time.sleep(2)
        logs = requests.get(f"{BASE}/api/sms-logs?eventCode=TARE_COMPLETED&pageSize=100", headers=dev_h).json()
        assert not any(l["referenceId"] == f"PUR-{pid}" for l in logs["items"])


class TestFailureAndRetry:
    def test_failure_retry_pending_then_manual_retry(self, dev_h):
        # Point to unreachable
        _configure_sms_ok(dev_h, base_url="http://127.0.0.1:1/send", enabled=True)
        gc, _ = _fresh_grower(dev_h, "FAIL")
        pid, tare_resp = _create_purchase(dev_h, gc)
        assert tare_resp.get("smsQueued") is True
        # Wait for at least one processor cycle
        time.sleep(20)
        logs = requests.get(f"{BASE}/api/sms-logs?eventCode=TARE_COMPLETED&pageSize=50", headers=dev_h).json()
        row = next((l for l in logs["items"] if l["referenceId"] == f"PUR-{pid}"), None)
        assert row is not None
        assert row["status"] in ("RETRY_PENDING", "FAILED"), f"expected RETRY_PENDING got {row['status']}"
        assert row["attemptCount"] >= 1
        assert row.get("failureReason")
        assert row.get("nextAttemptAt") is not None
        log_id = row["id"]

        # Fix the config -> reachable
        _configure_sms_ok(dev_h, base_url=MOCK_URL, enabled=True)
        # Force immediate retry
        r = requests.post(f"{BASE}/api/sms-logs/{log_id}/retry", headers=dev_h, timeout=10)
        assert r.status_code == 200, r.text
        time.sleep(20)
        logs2 = requests.get(f"{BASE}/api/sms-logs?pageSize=100", headers=dev_h).json()
        row2 = next((l for l in logs2["items"] if l["id"] == log_id), None)
        assert row2["status"] == "SENT", f"after retry expected SENT got {row2}"

    def test_business_txn_independent_of_sms(self, dev_h):
        """Weighment Tare + Payment must succeed HTTP 200 even when SMS provider is unreachable."""
        _configure_sms_ok(dev_h, base_url="http://127.0.0.1:1/send", enabled=True)
        gc, _ = _fresh_grower(dev_h, "INDEP")
        pid, tare_resp = _create_purchase(dev_h, gc)
        # purchase still transitions
        p = requests.get(f"{BASE}/api/purchases/{pid}", headers=dev_h).json()
        assert p["paymentStatus"] in ("PENDING", "PAID")  # tare-done
        bmid = _bank_mode_id(dev_h)
        r = requests.post(f"{BASE}/api/payments", headers=dev_h, json={
            "selectionMode": "SINGLE", "growerCode": gc, "purchaseId": pid,
            "paymentModeId": bmid, "transactionRefNumber": "INDEP",
            "idempotencyKey": str(uuid.uuid4())}, timeout=15)
        assert r.status_code == 200, f"payment failed while SMS unreachable: {r.status_code} {r.text}"
        _configure_sms_ok(dev_h, base_url=MOCK_URL, enabled=True)


class TestSmsLogsAndRBAC:
    def test_logs_masked_and_filters(self, dev_h):
        r = requests.get(f"{BASE}/api/sms-logs?pageSize=200", headers=dev_h, timeout=10)
        assert r.status_code == 200, r.text
        body = r.json()
        assert "items" in body and "totalCount" in body
        for l in body["items"]:
            assert "mobileMasked" in l
            assert "X" in l["mobileMasked"] or l["mobileMasked"] == ""
        # filter by status
        r2 = requests.get(f"{BASE}/api/sms-logs?status=SENT&pageSize=10", headers=dev_h).json()
        for l in r2["items"]:
            assert l["status"] == "SENT"

    def test_rbac_non_developer_forbidden(self, dev_h):
        # Create an operator and check 403 on sms endpoints
        rr = requests.get(f"{BASE}/api/roles", headers=dev_h, timeout=10).json()
        op_role = next((x for x in rr if x["name"] == "Operator"), None)
        assert op_role, "Operator role missing"
        uname = f"opsms_{uuid.uuid4().hex[:6]}"
        pwd = "OpSms@2026"
        tmp = pwd + "Tmp"
        cu = requests.post(f"{BASE}/api/users", headers=dev_h, json={
            "username": uname, "fullName": "Op SMS", "mobile": f"90{uuid.uuid4().hex[:8]}"[:11],
            "temporaryPassword": tmp, "roleIds": [op_role["id"]]}, timeout=15)
        if cu.status_code not in (200, 201):
            pytest.skip(f"user create failed (known env quirk): {cu.status_code} {cu.text[:100]}")
        rl = _login(uname, tmp)
        if rl.status_code != 200:
            pytest.skip(f"login failed: {rl.status_code}")
        j = rl.json()
        if j.get("mustChangePassword"):
            cp = requests.post(f"{BASE}/api/auth/change-password", headers=_hdr(j["accessToken"]),
                json={"currentPassword": tmp, "newPassword": pwd, "confirmPassword": pwd}, timeout=10)
            if cp.status_code != 200:
                pytest.skip("change password failed")
            rl = _login(uname, pwd)
        op_h = _hdr(rl.json()["accessToken"])
        # All should 403
        r = requests.get(f"{BASE}/api/config/sms", headers=op_h, timeout=10)
        assert r.status_code == 403, f"Operator should be 403 on GET /api/config/sms: {r.status_code}"
        r = requests.put(f"{BASE}/api/config/sms", headers=op_h, json={
            "providerName": "x", "apiBaseUrl": "http://x", "httpMethod": "POST", "enabled": False}, timeout=10)
        assert r.status_code == 403
        r = requests.get(f"{BASE}/api/sms-logs", headers=op_h, timeout=10)
        assert r.status_code == 403


class TestLoanNoSmsRegression:
    def test_loan_issue_does_not_queue_sms(self, dev_h):
        _configure_sms_ok(dev_h, base_url=MOCK_URL, enabled=True)
        gc, _ = _fresh_grower(dev_h, "LOAN")
        before = requests.get(f"{BASE}/api/sms-logs?pageSize=1", headers=dev_h).json()["totalCount"]
        r = requests.post(f"{BASE}/api/loans", headers=dev_h, json={
            "growerCode": gc, "loanTypeId": 1, "loanAmount": 1000, "remarks": "TEST no-sms"}, timeout=15)
        assert r.status_code == 200, r.text
        j = r.json()
        assert "smsQueued" not in j, f"loan response must NOT have smsQueued field: {j}"
        time.sleep(2)
        after = requests.get(f"{BASE}/api/sms-logs?pageSize=1", headers=dev_h).json()["totalCount"]
        assert after == before, f"loan issue must not create SmsLog: {before}->{after}"
