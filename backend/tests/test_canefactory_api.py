"""
CaneFactorySystem ASP.NET Core API — end-to-end regression suite.
Fresh DB at /tmp/canefactory_dev.db, developer temp pwd = Dev@2026Temp.
API on http://localhost:8001. Ordered — do NOT parallelize.
"""
import os
import time
import uuid
import requests
import pytest

BASE = os.environ.get("CANE_API_URL", "http://localhost:8001")
TEMP_PW = "Dev@2026Temp"
NEW_DEV_PW = "DevSecure@2026"
state = {}


def hdr(token):
    return {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}


# ---------------- 1. Forced password change on fresh DB ----------------
class TestFirstLoginForceChange:
    def test_01_temp_login_returns_mustChangePassword(self):
        r = requests.post(f"{BASE}/api/auth/login",
                          json={"username": "developer", "password": TEMP_PW})
        assert r.status_code == 200, r.text
        d = r.json()
        assert d.get("mustChangePassword") is True
        state["dev_token_temp"] = d["accessToken"]

    def test_02_business_api_returns_403_before_change(self):
        r = requests.get(f"{BASE}/api/zones", headers=hdr(state["dev_token_temp"]))
        assert r.status_code == 403, f"got {r.status_code} body={r.text[:200]}"

    def test_03_change_password_success(self):
        r = requests.post(f"{BASE}/api/auth/change-password",
                          headers=hdr(state["dev_token_temp"]),
                          json={"currentPassword": TEMP_PW,
                                "newPassword": NEW_DEV_PW,
                                "confirmPassword": NEW_DEV_PW})
        assert r.status_code in (200, 204), r.text

    def test_04_login_new_password(self):
        r = requests.post(f"{BASE}/api/auth/login",
                          json={"username": "developer", "password": NEW_DEV_PW})
        assert r.status_code == 200, r.text
        d = r.json()
        assert d.get("mustChangePassword") in (False, None)
        state["dev_token"] = d["accessToken"]
        state["dev_refresh"] = d["refreshToken"]

    def test_05_business_api_200_after_change(self):
        r = requests.get(f"{BASE}/api/zones", headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text


# ---------------- 2. Refresh rotation ----------------
class TestRefreshRotation:
    def test_06_refresh_returns_new_pair(self):
        old = state["dev_refresh"]
        r = requests.post(f"{BASE}/api/auth/refresh", json={"refreshToken": old})
        assert r.status_code == 200, r.text
        d = r.json()
        assert d["refreshToken"] != old
        state["dev_token"] = d["accessToken"]
        state["dev_refresh_new"] = d["refreshToken"]
        state["dev_refresh_old"] = old

    def test_07_reuse_old_refresh_401(self):
        r = requests.post(f"{BASE}/api/auth/refresh",
                          json={"refreshToken": state["dev_refresh_old"]})
        assert r.status_code == 401, r.text

    def test_08_after_reuse_new_refresh_also_revoked(self):
        r = requests.post(f"{BASE}/api/auth/refresh",
                          json={"refreshToken": state["dev_refresh_new"]})
        assert r.status_code == 401, r.text

    def test_09_relogin_developer(self):
        r = requests.post(f"{BASE}/api/auth/login",
                          json={"username": "developer", "password": NEW_DEV_PW})
        assert r.status_code == 200, r.text
        state["dev_token"] = r.json()["accessToken"]


# ---------------- 3. Masters ----------------
class TestMasters:
    def test_10_create_zone_id_1(self):
        r = requests.post(f"{BASE}/api/zones", headers=hdr(state["dev_token"]),
                          json={"zoneCode": "TZA", "zoneName": "TEST Zone A"})
        assert r.status_code in (200, 201), r.text
        zid = r.json().get("id") or r.json().get("zoneId")
        assert zid == 1, f"first zone id: {r.text[:200]}"
        state["zone_id"] = zid

    def test_11_create_village_id_101(self):
        r = requests.post(f"{BASE}/api/villages", headers=hdr(state["dev_token"]),
                          json={"villageName": "TEST Village A", "zoneId": state["zone_id"]})
        assert r.status_code in (200, 201), r.text
        vid = r.json().get("id") or r.json().get("villageId")
        assert vid == 101, f"first village id: {r.text[:200]}"
        state["village_id"] = vid

    def test_12_village_duplicate_case_insensitive_conflict(self):
        r = requests.post(f"{BASE}/api/villages", headers=hdr(state["dev_token"]),
                          json={"villageName": "  test village a  ",
                                "zoneId": state["zone_id"]})
        assert r.status_code == 409, f"got {r.status_code}: {r.text[:200]}"

    def test_13_bank_invalid_ifsc_rejected(self):
        r = requests.post(f"{BASE}/api/banks", headers=hdr(state["dev_token"]),
                          json={"bankName": "TEST Bank", "branchName": "Main",
                                "ifsc": "INVALID"})
        assert r.status_code in (400, 409, 422), \
            f"expected 4xx got {r.status_code}: {r.text[:200]}"

    def test_14_bank_valid_ifsc_success(self):
        r = requests.post(f"{BASE}/api/banks", headers=hdr(state["dev_token"]),
                          json={"bankName": "TEST Bank", "branchName": "Main",
                                "ifsc": "SBIN0001234"})
        assert r.status_code in (200, 201), r.text
        state["bank_id"] = r.json().get("id") or r.json().get("bankId")

    def test_15_bank_duplicate_name_branch_conflict(self):
        r = requests.post(f"{BASE}/api/banks", headers=hdr(state["dev_token"]),
                          json={"bankName": "TEST Bank", "branchName": "Main",
                                "ifsc": "HDFC0009999"})
        assert r.status_code == 409, f"got {r.status_code}: {r.text[:200]}"

    def test_16_party_duplicate_name_mobile_conflict(self):
        payload = {"partyName": "TEST Party X", "mobile": "9876500001"}
        r1 = requests.post(f"{BASE}/api/parties",
                           headers=hdr(state["dev_token"]), json=payload)
        assert r1.status_code in (200, 201), r1.text
        r2 = requests.post(f"{BASE}/api/parties",
                           headers=hdr(state["dev_token"]), json=payload)
        assert r2.status_code == 409, f"got {r2.status_code}: {r2.text[:200]}"
        assert "DUPLICATE" in r2.text.upper()


# ---------------- 4. Growers ----------------
class TestGrowers:
    def payload(self, name, mobile, aadhaar, accept_dup=False):
        return {
            "villageId": state["village_id"],
            "growerName": name,
            "fatherName": "Father",
            "mobile": mobile,
            "aadhaarNumber": aadhaar,
            "bankAccountNumber": "1234567890",
            "bankId": state.get("bank_id"),
            "acceptDuplicateWarning": accept_dup,
        }

    def test_17_grower_101_1(self):
        r = requests.post(f"{BASE}/api/growers", headers=hdr(state["dev_token"]),
                          json=self.payload("TEST G1", "9876511111", "111122223333"))
        assert r.status_code in (200, 201), r.text
        j = r.json()
        assert j.get("growerCode") == "101/1", r.text[:300]
        state["grower1_id"] = j.get("id")
        state["grower1_code"] = j.get("growerCode")
        assert "111122223333" not in r.text, "raw Aadhaar leaked"

    def test_18_grower_101_2(self):
        r = requests.post(f"{BASE}/api/growers", headers=hdr(state["dev_token"]),
                          json=self.payload("TEST G2", "9876522222", "222233334444"))
        assert r.status_code in (200, 201), r.text
        assert r.json().get("growerCode") == "101/2", r.text[:200]

    def test_19_duplicate_aadhaar_blocked(self):
        r = requests.post(f"{BASE}/api/growers", headers=hdr(state["dev_token"]),
                          json=self.payload("TEST G3", "9876533333", "111122223333"))
        assert r.status_code == 409, f"got {r.status_code}: {r.text[:200]}"

    def test_20_duplicate_mobile_warns_422(self):
        r = requests.post(f"{BASE}/api/growers", headers=hdr(state["dev_token"]),
                          json=self.payload("TEST G4", "9876511111", "333344445555"))
        assert r.status_code == 422, f"got {r.status_code}: {r.text[:200]}"
        assert r.json().get("requiresConfirmation") is True

    def test_21_duplicate_mobile_accept(self):
        r = requests.post(f"{BASE}/api/growers", headers=hdr(state["dev_token"]),
                          json=self.payload("TEST G4b", "9876511111",
                                            "444455556666", accept_dup=True))
        assert r.status_code in (200, 201), r.text

    def test_22_grower_by_code_masked(self):
        r = requests.get(f"{BASE}/api/growers/by-code",
                         params={"code": "101/1"},
                         headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text
        assert "1234567890" not in r.text, "raw bank account leaked"
        assert "XXXX" in r.text or "accountMasked" in r.text


# ---------------- 5. Rates ----------------
class TestRates:
    def test_23_create_rate(self):
        r = requests.post(f"{BASE}/api/rates", headers=hdr(state["dev_token"]),
                          json={"varietyTypeId": 1, "rate": 320.00,
                                "effectiveFrom": "2026-01-01T00:00:00Z"})
        assert r.status_code in (200, 201), r.text
        state["rate_id"] = r.json().get("id")

    def test_24_overlapping_rate_conflict(self):
        r = requests.post(f"{BASE}/api/rates", headers=hdr(state["dev_token"]),
                          json={"varietyTypeId": 1, "rate": 330.00,
                                "effectiveFrom": "2026-06-01T00:00:00Z"})
        assert r.status_code == 409, f"got {r.status_code}: {r.text[:200]}"

    def test_25_current_rate(self):
        r = requests.get(f"{BASE}/api/rates/current/1",
                         headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text
        assert float(r.json().get("rate", 0)) == 320.00


# ---------------- 6. Parser ----------------
class TestParser:
    def test_26_parser_positive_frame(self):
        r = requests.post(f"{BASE}/api/devices/test-parser",
                          headers=hdr(state["dev_token"]),
                          json={"stringProfileId": 1,
                                "rawHex": "02 20 30 30 31 35 30 30 03 0D 0A"})
        assert r.status_code == 200, r.text
        d = r.json()
        assert d.get("frameValid") is True, d
        assert float(d.get("numericWeight", 0)) == 1500.0

    def test_27_parser_negative_frame(self):
        r = requests.post(f"{BASE}/api/devices/test-parser",
                          headers=hdr(state["dev_token"]),
                          json={"stringProfileId": 1,
                                "rawHex": "02 2D 30 30 30 32 35 30 03 0D 0A"})
        assert r.status_code == 200, r.text
        d = r.json()
        assert d.get("frameValid") is True
        assert float(d.get("numericWeight", 0)) == -250.0

    def test_28_parser_malformed(self):
        r = requests.post(f"{BASE}/api/devices/test-parser",
                          headers=hdr(state["dev_token"]),
                          json={"stringProfileId": 1, "rawHex": "02 20 30 30"})
        assert r.status_code == 200, r.text
        assert r.json().get("frameValid") is False


# ---------------- 7. Weighment gross ----------------
class TestWeighmentGross:
    def _base(self, kg, idem, veh="MH12AB1234"):
        return {"growerCode": state["grower1_code"],
                "vehicleTypeId": 1, "vehicleNumber": veh,
                "varietyTypeId": 1, "varietyId": 1,
                "cuttingPercent": 2.0, "taxPercent": 1.0,
                "scaleReadingKg": kg, "idempotencyKey": idem}

    def test_29_gross_below_min(self):
        r = requests.post(f"{BASE}/api/weighment/gross",
                          headers=hdr(state["dev_token"]),
                          json=self._base(500, str(uuid.uuid4())))
        assert r.status_code == 409, f"got {r.status_code}: {r.text[:200]}"
        assert "BELOW_MINIMUM" in r.text or "below" in r.text.lower()

    def test_30_gross_success(self):
        state["idem"] = str(uuid.uuid4())
        r = requests.post(f"{BASE}/api/weighment/gross",
                          headers=hdr(state["dev_token"]),
                          json=self._base(25050, state["idem"]))
        assert r.status_code in (200, 201), r.text
        assert "250.50" in r.text, f"expected 250.50: {r.text[:400]}"
        pid = r.json().get("purchaseId")
        assert pid == 1, f"expected purchase 1 got {pid}"
        state["purchase_id"] = pid

    def test_31_gross_duplicate_idempotency(self):
        r = requests.post(f"{BASE}/api/weighment/gross",
                          headers=hdr(state["dev_token"]),
                          json=self._base(25050, state["idem"]))
        assert r.status_code == 409, f"got {r.status_code}: {r.text[:200]}"


# ---------------- 8. Weighment tare ----------------
class TestWeighmentTare:
    def test_32_pending_tare_lists(self):
        r = requests.get(f"{BASE}/api/weighment/pending-tare",
                         headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text
        assert str(state["purchase_id"]) in r.text

    def test_33_tare_gte_gross_conflict(self):
        r = requests.post(f"{BASE}/api/weighment/tare",
                          headers=hdr(state["dev_token"]),
                          json={"purchaseId": state["purchase_id"],
                                "scaleReadingKg": 30000,
                                "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 409, f"got {r.status_code}: {r.text[:200]}"

    def test_34_tare_success(self):
        r = requests.post(f"{BASE}/api/weighment/tare",
                          headers=hdr(state["dev_token"]),
                          json={"purchaseId": state["purchase_id"],
                                "scaleReadingKg": 8341,
                                "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code in (200, 201), r.text
        j = r.json()
        # Net = 250.50 - 83.41 = 167.09
        assert float(j.get("netWeightQuintal", 0)) == 167.09, j
        # Cutting = 167.09 * 2 / 100 = 3.3418 -> round 2dp = 3.34
        assert float(j.get("cuttingWeightQuintal", 0)) == 3.34, j
        # Tax = 167.09 * 1 / 100 = 1.6709 -> 1.67
        assert float(j.get("taxWeightQuintal", 0)) == 1.67, j
        # Final = 167.09 - 3.34 - 1.67 = 162.08
        assert float(j.get("finalWeightQuintal", 0)) == 162.08, j
        # Amount = 162.08 * 320 = 51865.60
        assert float(j.get("purchaseAmount", 0)) == 51865.60, j

    def test_35_second_tare_conflict(self):
        r = requests.post(f"{BASE}/api/weighment/tare",
                          headers=hdr(state["dev_token"]),
                          json={"purchaseId": state["purchase_id"],
                                "scaleReadingKg": 8000,
                                "idempotencyKey": str(uuid.uuid4())})
        assert r.status_code == 409, r.text

    def test_36_for_tare_after_done_conflict(self):
        r = requests.get(f"{BASE}/api/weighment/purchase/{state['purchase_id']}/for-tare",
                        headers=hdr(state["dev_token"]))
        assert r.status_code == 409, f"got {r.status_code}: {r.text[:200]}"


# ---------------- 9. Purchase lifecycle ----------------
class TestPurchaseLifecycle:
    def test_37_purchases_list(self):
        r = requests.get(f"{BASE}/api/purchases", headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text

    def test_38_lock_blocks_tare(self):
        # New gross
        idem = str(uuid.uuid4())
        r = requests.post(f"{BASE}/api/weighment/gross", headers=hdr(state["dev_token"]),
                          json={"growerCode": state["grower1_code"],
                                "vehicleTypeId": 1, "vehicleNumber": "MH12CD5555",
                                "varietyTypeId": 1, "varietyId": 1,
                                "cuttingPercent": 2.0, "taxPercent": 1.0,
                                "scaleReadingKg": 20000, "idempotencyKey": idem})
        assert r.status_code in (200, 201), r.text
        pid2 = r.json()["purchaseId"]
        state["purchase_id_2"] = pid2
        rl = requests.post(f"{BASE}/api/purchases/{pid2}/lock",
                           headers=hdr(state["dev_token"]), json={})
        assert rl.status_code in (200, 204), rl.text
        rt = requests.post(f"{BASE}/api/weighment/tare",
                           headers=hdr(state["dev_token"]),
                           json={"purchaseId": pid2, "scaleReadingKg": 6000,
                                 "idempotencyKey": str(uuid.uuid4())})
        assert rt.status_code in (403, 409, 423), \
            f"expected blocked, got {rt.status_code}: {rt.text[:200]}"

    def test_39_unlock_cancel(self):
        pid2 = state["purchase_id_2"]
        ru = requests.post(f"{BASE}/api/purchases/{pid2}/unlock",
                           headers=hdr(state["dev_token"]), json={})
        assert ru.status_code in (200, 204), ru.text
        rc0 = requests.post(f"{BASE}/api/purchases/{pid2}/cancel",
                            headers=hdr(state["dev_token"]), json={})
        assert rc0.status_code == 400, f"got {rc0.status_code}: {rc0.text[:200]}"
        rc = requests.post(f"{BASE}/api/purchases/{pid2}/cancel",
                           headers=hdr(state["dev_token"]),
                           json={"reason": "TEST cancel"})
        assert rc.status_code in (200, 204), rc.text

    def test_40_audit_log_events(self):
        r = requests.get(f"{BASE}/api/audit", headers=hdr(state["dev_token"]),
                         params={"pageSize": 200})
        assert r.status_code == 200, r.text
        body = r.text
        for k in ("Login", "GrossWeighment", "TareWeighment"):
            assert k in body, f"audit missing {k}"


# ---------------- 10. Config ----------------
class TestConfig:
    def test_41_weight_rules_put(self):
        g = requests.get(f"{BASE}/api/config/weight-rules",
                         headers=hdr(state["dev_token"]))
        assert g.status_code == 200, g.text
        cfg = g.json() or {}
        cfg["minimumWeightQuintal"] = 10.00
        cfg.setdefault("enabled", True)
        cfg.setdefault("applyToCanePurchase", True)
        cfg.setdefault("applyToSalePurchase", True)
        cfg.setdefault("applyToGross", True)
        cfg.setdefault("applyToTare", True)
        cfg.setdefault("defaultCuttingPercent", 0)
        cfg.setdefault("defaultTaxPercent", 0)
        r = requests.put(f"{BASE}/api/config/weight-rules",
                         headers=hdr(state["dev_token"]), json=cfg)
        assert r.status_code in (200, 204), r.text

    def test_42_camera_password_not_leaked(self):
        rc = requests.post(f"{BASE}/api/config/cameras",
                           headers=hdr(state["dev_token"]),
                           json={"cameraNumber": 1, "vendor": "Hikvision",
                                 "protocol": "RTSP", "ipAddress": "10.0.0.10",
                                 "port": 554, "username": "admin",
                                 "password": "sup3r-secret-XYZ",
                                 "rtspUrl": "rtsp://10.0.0.10/live",
                                 "channel": 1, "streamType": "Main",
                                 "captureEnabled": True, "liveViewEnabled": True,
                                 "retentionDays": 30, "status": True})
        assert rc.status_code in (200, 201), rc.text
        r = requests.get(f"{BASE}/api/config/cameras", headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text
        assert "sup3r-secret-XYZ" not in r.text, "camera password leaked!"
        assert "hasPassword" in r.text.replace("HasPassword", "hasPassword")

    def test_43_sms_secret_not_leaked(self):
        pr = requests.put(f"{BASE}/api/config/sms",
                          headers=hdr(state["dev_token"]),
                          json={"providerName": "TEST", "apiBaseUrl": "https://x/y",
                                "httpMethod": "POST", "apiKey": "sk_live_ABC123SECRET",
                                "senderId": "CANE", "enabled": True})
        assert pr.status_code in (200, 204), pr.text
        r = requests.get(f"{BASE}/api/config/sms", headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text
        assert "sk_live_ABC123SECRET" not in r.text, "SMS API key leaked!"
        assert "hasApiKey" in r.text.replace("HasApiKey", "hasApiKey")

    def test_44_user_guide(self):
        r = requests.get(f"{BASE}/api/user-guide", headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text

    def test_45_dashboard_health(self):
        r = requests.get(f"{BASE}/api/dashboard/health", headers=hdr(state["dev_token"]))
        assert r.status_code == 200, r.text


# ---------------- 11. RBAC — Operator ----------------
class TestRBACOperator:
    def test_46_create_operator(self):
        # get Operator role id
        rr = requests.get(f"{BASE}/api/roles", headers=hdr(state["dev_token"]))
        assert rr.status_code == 200, rr.text
        op_role = next((x for x in rr.json() if x["name"] == "Operator"), None)
        assert op_role, rr.text[:300]
        state["op_role_id"] = op_role["id"]
        r = requests.post(f"{BASE}/api/users", headers=hdr(state["dev_token"]),
                          json={"username": "testop1", "fullName": "TEST Operator",
                                "mobile": "9876599999",
                                "temporaryPassword": "OpTemp@2026",
                                "roleIds": [op_role["id"]]})
        assert r.status_code in (200, 201), r.text

    def test_47_operator_first_login_change_pw(self):
        r = requests.post(f"{BASE}/api/auth/login",
                          json={"username": "testop1", "password": "OpTemp@2026"})
        assert r.status_code == 200, r.text
        d = r.json()
        assert d.get("mustChangePassword") is True
        tmp = d["accessToken"]
        cp = requests.post(f"{BASE}/api/auth/change-password", headers=hdr(tmp),
                           json={"currentPassword": "OpTemp@2026",
                                 "newPassword": "OpSecure@2026",
                                 "confirmPassword": "OpSecure@2026"})
        assert cp.status_code in (200, 204), cp.text
        r2 = requests.post(f"{BASE}/api/auth/login",
                           json={"username": "testop1", "password": "OpSecure@2026"})
        assert r2.status_code == 200, r2.text
        state["op_token"] = r2.json()["accessToken"]

    def test_48_operator_forbidden_endpoints(self):
        h = hdr(state["op_token"])
        r1 = requests.put(f"{BASE}/api/config/weight-rules", headers=h,
                          json={"minimumWeightQuintal": 10, "enabled": True,
                                "applyToCanePurchase": True, "applyToSalePurchase": True,
                                "applyToGross": True, "applyToTare": True,
                                "defaultCuttingPercent": 0, "defaultTaxPercent": 0})
        assert r1.status_code == 403, f"weight-rules PUT: {r1.status_code} {r1.text[:200]}"
        r2 = requests.get(f"{BASE}/api/config/sms", headers=h)
        assert r2.status_code == 403, f"sms GET: {r2.status_code}"
        r3 = requests.get(f"{BASE}/api/users", headers=h)
        assert r3.status_code == 403, f"users GET: {r3.status_code}"

    def test_49_operator_allowed_endpoints(self):
        h = hdr(state["op_token"])
        r1 = requests.get(f"{BASE}/api/growers/by-code", params={"code": "101/1"},
                          headers=h)
        assert r1.status_code == 200, f"grower lookup: {r1.status_code} {r1.text[:200]}"
        r2 = requests.post(f"{BASE}/api/weighment/gross", headers=h,
                           json={"growerCode": state["grower1_code"],
                                 "vehicleTypeId": 1, "vehicleNumber": "MH12EF7777",
                                 "varietyTypeId": 1, "varietyId": 1,
                                 "cuttingPercent": 2.0, "taxPercent": 1.0,
                                 "scaleReadingKg": 15000,
                                 "idempotencyKey": str(uuid.uuid4())})
        assert r2.status_code in (200, 201), \
            f"operator gross: {r2.status_code} {r2.text[:200]}"

    def test_50_unauthenticated_purchases_401(self):
        r = requests.get(f"{BASE}/api/purchases")
        assert r.status_code == 401, f"got {r.status_code}"


# ---------------- 12. Wrong password + lockout ----------------
class TestAuthLockout:
    def test_51_wait_for_rate_limit_reset(self):
        # /api/auth/* is 10/min per IP; empty the bucket
        time.sleep(65)

    def test_52_wrong_password_generic_401(self):
        r = requests.post(f"{BASE}/api/auth/login",
                          json={"username": "testop1", "password": "WrongPass!"})
        assert r.status_code == 401, r.text
        low = r.text.lower()
        assert "invalid" in low or "incorrect" in low or "unauthorized" in low

    def test_53_lockout_after_5_bad_attempts(self):
        # 4 more wrong (total 5 with prior test), then confirm lockout
        codes = []
        for _ in range(4):
            rr = requests.post(f"{BASE}/api/auth/login",
                               json={"username": "testop1", "password": "WrongPass!"})
            codes.append(rr.status_code)
            time.sleep(0.4)
        r = requests.post(f"{BASE}/api/auth/login",
                         json={"username": "testop1", "password": "OpSecure@2026"})
        assert r.status_code in (401, 403, 423), \
            f"expected lockout got {r.status_code} attempts={codes}: {r.text[:200]}"
        assert "lock" in r.text.lower() or r.status_code == 423, \
            f"expected lock message got body={r.text[:200]}, attempts={codes}"
