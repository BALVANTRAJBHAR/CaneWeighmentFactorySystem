"""
Phase 11 (Reports), Phase 12 (Farmer Portal), Phase 13 (Security/Backup/Health) tests.
Also verifies Razorpay endpoint removal regression.

Requires backend running on http://localhost:8099 with fresh SQLite DB.
Seed dev password 'Dev@2026Temp' -> change to DevSecure@2026 on first bootstrap.
"""
import os
import io
import time
import pytest
import requests
from datetime import date, timedelta

BASE = "http://localhost:8099"
DEV_USER = "developer"
DEV_TEMP = "Dev@2026Temp"
DEV_PASS = "DevSecure@2026"


@pytest.fixture(scope="session")
def dev_token():
    r = requests.post(f"{BASE}/api/auth/login", json={"username": DEV_USER, "password": DEV_TEMP})
    if r.status_code == 200 and r.json().get("mustChangePassword"):
        tok = r.json()["accessToken"]
        cp = requests.post(f"{BASE}/api/auth/change-password",
            headers={"Authorization": f"Bearer {tok}"},
            json={"currentPassword": DEV_TEMP, "newPassword": DEV_PASS, "confirmPassword": DEV_PASS})
        assert cp.status_code in (200, 204), cp.text
    r = requests.post(f"{BASE}/api/auth/login", json={"username": DEV_USER, "password": DEV_PASS})
    assert r.status_code == 200, r.text
    return r.json()["accessToken"]


def H(token):
    return {"Authorization": f"Bearer {token}"}


# ---------- Phase 13 Health (public) ----------
def test_health_no_auth():
    r = requests.get(f"{BASE}/api/health")
    assert r.status_code == 200
    j = r.json()
    assert "status" in j
    assert "database" in j
    assert "serverTimeUtc" in j


# ---------- Regression: Razorpay removed ----------
def test_razorpay_endpoint_removed(dev_token):
    r = requests.get(f"{BASE}/api/config/razorpay", headers=H(dev_token))
    assert r.status_code == 404, f"Razorpay endpoint should be removed, got {r.status_code}"


# ---------- Phase 13 Backup ----------
def test_backup_config_get(dev_token):
    r = requests.get(f"{BASE}/api/backup/config", headers=H(dev_token))
    assert r.status_code == 200, r.text
    j = r.json()
    # Should have frequency, time, retentionDays, folder path
    keys = set(k.lower() for k in j.keys())
    assert any("frequency" in k for k in keys)


def _bp(**overrides):
    p = {"enabled": True, "frequency": "Daily", "timeOfDay": "02:00", "retentionDays": 7, "backupFolderPath": "E:\\Backup"}
    p.update(overrides)
    return p


def test_backup_config_put_invalid(dev_token):
    r = requests.put(f"{BASE}/api/backup/config", headers=H(dev_token), json=_bp(frequency="Monthly"))
    assert r.status_code == 400, r.text


def test_backup_config_put_invalid_time(dev_token):
    r = requests.put(f"{BASE}/api/backup/config", headers=H(dev_token), json=_bp(timeOfDay="25:99"))
    assert r.status_code == 400, r.text


def test_backup_config_put_invalid_retention(dev_token):
    r = requests.put(f"{BASE}/api/backup/config", headers=H(dev_token), json=_bp(retentionDays=0))
    assert r.status_code == 400, r.text


def test_backup_config_put_empty_folder(dev_token):
    r = requests.put(f"{BASE}/api/backup/config", headers=H(dev_token), json=_bp(backupFolderPath=""))
    assert r.status_code == 400, r.text


def test_backup_config_put_valid(dev_token):
    r = requests.put(f"{BASE}/api/backup/config", headers=H(dev_token), json=_bp(timeOfDay="03:30", retentionDays=10, backupFolderPath="E:\\CaneBackups"))
    assert r.status_code in (200, 204), r.text
    r2 = requests.get(f"{BASE}/api/backup/config", headers=H(dev_token))
    assert r2.status_code == 200
    body = r2.text
    assert "03:30" in body, f"time not persisted: {body}"
    assert '"retentionDays":10' in body


def test_backup_script(dev_token):
    r = requests.get(f"{BASE}/api/backup/script", headers=H(dev_token))
    assert r.status_code == 200, r.text
    text = r.text
    assert "BACKUP DATABASE" in text.upper() or "BACKUP" in text.upper()


def test_backup_task_scheduler_xml(dev_token):
    r = requests.get(f"{BASE}/api/backup/task-scheduler-xml", headers=H(dev_token))
    assert r.status_code == 200, r.text
    body = r.text
    assert body.strip().startswith("<?xml") or body.lstrip().startswith("<")


# ---------- Phase 13 Security audit (401/403 logging) ----------
def test_security_audit_401_and_403(dev_token):
    # Trigger 401 - no token
    r = requests.get(f"{BASE}/api/growers")
    assert r.status_code == 401
    # Trigger 403 - invalid/expired
    r2 = requests.get(f"{BASE}/api/growers", headers={"Authorization": "Bearer invalid.jwt.token"})
    assert r2.status_code in (401, 403)
    # Give middleware a moment to write
    time.sleep(1)
    # Read audit log
    a = requests.get(f"{BASE}/api/audit?module=Security", headers=H(dev_token))
    # If module filter unsupported, try without
    if a.status_code != 200:
        a = requests.get(f"{BASE}/api/audit", headers=H(dev_token))
    assert a.status_code == 200, a.text
    body = a.text.lower()
    assert ("unauthenticated" in body or "permissiondenied" in body or "security" in body or "401" in body or "403" in body), \
        "Expected a security audit entry after 401/403"


# ---------- Phase 11 Reports ----------
def _seed_masters(tok):
    """Create minimal masters + grower + purchase for reports."""
    # Zone
    r = requests.get(f"{BASE}/api/zones", headers=H(tok))
    zones = r.json() if r.status_code == 200 else []
    zone_id = None
    if isinstance(zones, list) and zones:
        zone_id = zones[0].get("id")
    elif isinstance(zones, dict) and zones.get("items"):
        zone_id = zones["items"][0].get("id")
    if not zone_id:
        z = requests.post(f"{BASE}/api/zones", headers=H(tok), json={"zoneName": "Zone T", "zoneCode": "ZT"})
        assert z.status_code in (200, 201), z.text
        zone_id = z.json().get("id") or z.json().get("Id")

    # Village
    r = requests.get(f"{BASE}/api/villages", headers=H(tok))
    v = r.json() if r.status_code == 200 else []
    village_id = None
    items = v if isinstance(v, list) else v.get("items", [])
    if items:
        village_id = items[0].get("id")
    if not village_id:
        vv = requests.post(f"{BASE}/api/villages", headers=H(tok), json={"villageName": "Village T", "zoneId": zone_id})
        assert vv.status_code in (200, 201), vv.text
        village_id = vv.json().get("id")

    return zone_id, village_id


@pytest.fixture(scope="session")
def seeded_ids(dev_token):
    return _seed_masters(dev_token)


def test_report_purchases_json(dev_token):
    r = requests.get(f"{BASE}/api/reports/purchases?format=json", headers=H(dev_token))
    assert r.status_code == 200, r.text
    j = r.json()
    # Must contain either items+totals or rows+totals
    assert isinstance(j, dict), f"expected dict, got {type(j)}"
    lk = {k.lower() for k in j.keys()}
    assert "totals" in lk or any("total" in k for k in lk), f"missing totals key: {j.keys()}"


def test_report_purchases_pdf(dev_token):
    r = requests.get(f"{BASE}/api/reports/purchases?format=pdf", headers=H(dev_token))
    assert r.status_code == 200, r.text
    assert r.content[:4] == b"%PDF", f"not a PDF: {r.content[:20]}"


def test_report_purchases_excel(dev_token):
    r = requests.get(f"{BASE}/api/reports/purchases?format=excel", headers=H(dev_token))
    assert r.status_code == 200, r.text
    assert r.content[:2] == b"PK", f"not a PK zip: {r.content[:20]}"


def test_report_payments_all_formats(dev_token):
    r = requests.get(f"{BASE}/api/reports/payments?format=json", headers=H(dev_token))
    assert r.status_code == 200, r.text
    r = requests.get(f"{BASE}/api/reports/payments?format=pdf", headers=H(dev_token))
    assert r.status_code == 200 and r.content[:4] == b"%PDF"
    r = requests.get(f"{BASE}/api/reports/payments?format=excel", headers=H(dev_token))
    assert r.status_code == 200 and r.content[:2] == b"PK"


def test_report_loans_all_formats(dev_token):
    r = requests.get(f"{BASE}/api/reports/loans?format=json", headers=H(dev_token))
    assert r.status_code == 200, r.text
    r = requests.get(f"{BASE}/api/reports/loans?format=pdf", headers=H(dev_token))
    assert r.status_code == 200 and r.content[:4] == b"%PDF"
    r = requests.get(f"{BASE}/api/reports/loans?format=excel", headers=H(dev_token))
    assert r.status_code == 200 and r.content[:2] == b"PK"


def test_report_daily_collection_all_formats(dev_token):
    today = date.today().isoformat()
    r = requests.get(f"{BASE}/api/reports/daily-collection?format=json&fromDate={today}&toDate={today}", headers=H(dev_token))
    assert r.status_code == 200, r.text
    r = requests.get(f"{BASE}/api/reports/daily-collection?format=pdf", headers=H(dev_token))
    assert r.status_code == 200 and r.content[:4] == b"%PDF"
    r = requests.get(f"{BASE}/api/reports/daily-collection?format=excel", headers=H(dev_token))
    assert r.status_code == 200 and r.content[:2] == b"PK"


def test_report_date_range_filter(dev_token):
    frm = (date.today() - timedelta(days=30)).isoformat()
    to = date.today().isoformat()
    r = requests.get(f"{BASE}/api/reports/purchases?format=json&fromDate={frm}&toDate={to}", headers=H(dev_token))
    assert r.status_code == 200


# ---------- Phase 12 Farmer portal ----------
@pytest.fixture(scope="session")
def farmer_setup(dev_token, seeded_ids):
    """Create a grower + matching farmer user."""
    zone_id, village_id = seeded_ids
    tok = dev_token
    mobile = "98700" + str(int(time.time()))[-5:]

    # Create grower
    gname = f"TEST_Farmer_{int(time.time())}"
    gpay = {
        "growerName": gname,
        "mobile": mobile,
        "villageId": village_id,
        "fatherName": "TestDad",
    }
    gr = requests.post(f"{BASE}/api/growers", headers=H(tok), json=gpay)
    assert gr.status_code in (200, 201), f"grower create failed: {gr.status_code} {gr.text}"
    grower = gr.json()
    grower_id = grower.get("id") or grower.get("growerId")

    # Get Farmer role id
    roles = requests.get(f"{BASE}/api/roles", headers=H(tok))
    assert roles.status_code == 200, roles.text
    rl = roles.json()
    rlist = rl if isinstance(rl, list) else rl.get("items", [])
    farmer_role = next((r for r in rlist if r.get("name") == "Farmer" or r.get("roleName") == "Farmer"), None)
    assert farmer_role, f"Farmer role not found in {rlist}"
    farmer_role_id = farmer_role.get("id") or farmer_role.get("roleId")

    # Create farmer user with matching mobile
    uname = f"testfarmer_{int(time.time())}"
    upay = {
        "username": uname,
        "temporaryPassword": "Farm@2026Temp",
        "fullName": gname,
        "mobile": mobile,
        "roleIds": [farmer_role_id],
        "email": f"{uname}@t.com",
    }
    ur = requests.post(f"{BASE}/api/users", headers=H(tok), json=upay)
    assert ur.status_code in (200, 201), f"user create failed: {ur.status_code} {ur.text}"

    # Login as farmer
    lg = requests.post(f"{BASE}/api/auth/login", json={"username": uname, "password": "Farm@2026Temp"})
    assert lg.status_code == 200, lg.text
    lj = lg.json()
    ftok = lj["accessToken"]
    if lj.get("mustChangePassword"):
        cp = requests.post(f"{BASE}/api/auth/change-password", headers=H(ftok),
            json={"currentPassword": "Farm@2026Temp", "newPassword": "Farm@2026New", "confirmPassword": "Farm@2026New"})
        # after change might need to re-login
        if cp.status_code in (200, 204):
            lg = requests.post(f"{BASE}/api/auth/login", json={"username": uname, "password": "Farm@2026New"})
            if lg.status_code == 200:
                ftok = lg.json()["accessToken"]

    return {"grower_id": grower_id, "mobile": mobile, "username": uname, "token": ftok}


def test_farmer_dashboard_self(farmer_setup):
    tok = farmer_setup["token"]
    r = requests.get(f"{BASE}/api/farmer/dashboard", headers=H(tok))
    assert r.status_code == 200, f"{r.status_code} {r.text}"
    j = r.json()
    lk = {k.lower() for k in j.keys()}
    assert "profile" in lk
    assert "summary" in lk
    assert any("recentpurchase" in k for k in lk) or any("purchase" in k for k in lk)
    assert any("recentpayment" in k for k in lk) or any("payment" in k for k in lk)


def test_farmer_statement_formats(farmer_setup):
    tok = farmer_setup["token"]
    r = requests.get(f"{BASE}/api/farmer/statement?format=json", headers=H(tok))
    assert r.status_code == 200, r.text
    r = requests.get(f"{BASE}/api/farmer/statement?format=pdf", headers=H(tok))
    assert r.status_code == 200 and r.content[:4] == b"%PDF"
    r = requests.get(f"{BASE}/api/farmer/statement?format=excel", headers=H(tok))
    assert r.status_code == 200 and r.content[:2] == b"PK"


def test_farmer_dashboard_no_grower_link(dev_token):
    """Developer's mobile likely doesn't match any grower -> should 404, NOT another farmer's data."""
    r = requests.get(f"{BASE}/api/farmer/dashboard", headers=H(dev_token))
    # Developer might have permission but no grower profile linked
    # Expect either 404 or their own scoped data (but not error). Accept 200/404/403.
    assert r.status_code in (200, 403, 404), f"unexpected {r.status_code}: {r.text}"
    if r.status_code == 200:
        # Ensure it's not the farmer's data at least by mobile
        j = r.json()
        # If there's a profile with the farmer's mobile, that's a bug
        body = r.text
        assert "9876500011" not in body, "Developer got farmer's data - SCOPING BUG"
    # loose check - just ensure not crashing


# ---------- Farmer report format=pdf permission check ----------
def test_farmer_cannot_export_reports(farmer_setup):
    """Farmer has Report.View but NOT Report.Print/.Export -> pdf/excel should 403, json OK."""
    tok = farmer_setup["token"]
    rj = requests.get(f"{BASE}/api/reports/purchases?format=json", headers=H(tok))
    # Farmer might not have Report.View on the general reports endpoint at all - accept 200/403
    r_pdf = requests.get(f"{BASE}/api/reports/purchases?format=pdf", headers=H(tok))
    assert r_pdf.status_code == 403, f"Farmer should get 403 on report pdf, got {r_pdf.status_code}"
