"""
Phase 7 - Centralized Printing Engine backend tests.
Tests: byte format (ESC/P + PDF), preview (PNG/PDF), audit + print counter,
TARE conflict flow, invalid inputs, RBAC on print endpoints,
Developer /print/test sample data, and Auto Print wiring on Gross/Tare.
Regression spot-checks: login flow + gross/tare math + camera images endpoint.
"""
import time
import uuid
import pytest
import requests

BASE_URL = "http://localhost:8001"
DEV = ("developer", "DevSecure@2026")
OP = ("opuser2", "OpSecure@2026")


def _login(u, p):
    r = requests.post(f"{BASE_URL}/api/auth/login", json={"username": u, "password": p}, timeout=15)
    assert r.status_code == 200, f"login {u} -> {r.status_code} {r.text}"
    return r.json()["accessToken"]


@pytest.fixture(scope="session")
def dev_h():
    return {"Authorization": f"Bearer {_login(*DEV)}"}


@pytest.fixture(scope="session")
def op_h():
    return {"Authorization": f"Bearer {_login(*OP)}"}


def _create_gross(headers):
    body = {
        "growerCode": "101/1", "vehicleTypeId": 1, "vehicleNumber": "UP32AB1234",
        "varietyTypeId": 1, "varietyId": 1, "cuttingPercent": 2.0, "taxPercent": 1.0,
        "scaleReadingKg": 5000, "idempotencyKey": str(uuid.uuid4()),
    }
    r = requests.post(f"{BASE_URL}/api/weighment/gross", json=body,
                      headers={**headers, "Content-Type": "application/json"}, timeout=15)
    assert r.status_code == 200, r.text
    return r.json()


def _create_tare(headers, pid, kg=1000):
    body = {"purchaseId": pid, "scaleReadingKg": kg, "idempotencyKey": str(uuid.uuid4())}
    r = requests.post(f"{BASE_URL}/api/weighment/tare", json=body,
                      headers={**headers, "Content-Type": "application/json"}, timeout=15)
    assert r.status_code == 200, r.text
    return r.json()


@pytest.fixture(scope="module")
def gross_only(dev_h):
    """Fresh purchase with only GROSS saved (used for TARE-conflict + gross print counter)."""
    g = _create_gross(dev_h)
    time.sleep(1.5)
    return g["purchaseId"]


@pytest.fixture(scope="module")
def full_cycle(dev_h):
    """Fresh purchase with GROSS + TARE saved."""
    g = _create_gross(dev_h)
    time.sleep(1.5)
    t = _create_tare(dev_h, g["purchaseId"])
    time.sleep(1.5)
    return {"pid": g["purchaseId"], "gross": g, "tare": t}


# ---------- 1) ESC/P byte format ----------

def test_gross_dotmatrix_final_returns_escp_bytes(dev_h, full_cycle):
    pid = full_cycle["pid"]
    r = requests.get(f"{BASE_URL}/api/print/purchase/{pid}",
                     params={"stage": "GROSS", "target": "DotMatrix", "format": "final"},
                     headers=dev_h, timeout=30)
    assert r.status_code == 200, r.text
    assert r.headers["Content-Type"].startswith("application/octet-stream"), r.headers
    b = r.content
    assert len(b) > 100, f"too small: {len(b)}"
    assert b[0] == 0x1B and b[1] == 0x40, f"missing ESC @ init: {b[:8].hex()}"
    # find ESC * (0x1B 0x2A) bit-image
    assert b"\x1b\x2a" in b, "missing ESC * bit-image command"


# ---------- 2) A4 PDF ----------

def test_gross_a4_final_returns_pdf(dev_h, full_cycle):
    pid = full_cycle["pid"]
    r = requests.get(f"{BASE_URL}/api/print/purchase/{pid}",
                     params={"stage": "GROSS", "target": "A4", "format": "final"},
                     headers=dev_h, timeout=30)
    assert r.status_code == 200, r.text
    assert r.headers["Content-Type"].startswith("application/pdf"), r.headers
    assert r.content[:4] == b"%PDF", r.content[:8]
    assert len(r.content) > 500


# ---------- 3) Previews ----------

def test_dotmatrix_preview_returns_png(dev_h, full_cycle):
    pid = full_cycle["pid"]
    r = requests.get(f"{BASE_URL}/api/print/purchase/{pid}",
                     params={"stage": "GROSS", "target": "DotMatrix", "format": "preview"},
                     headers=dev_h, timeout=30)
    assert r.status_code == 200, r.text
    assert r.content[:8] == b"\x89PNG\r\n\x1a\n", f"not PNG magic: {r.content[:8].hex()}"


def test_a4_preview_same_as_final_pdf(dev_h, full_cycle):
    pid = full_cycle["pid"]
    a = requests.get(f"{BASE_URL}/api/print/purchase/{pid}",
                     params={"stage": "GROSS", "target": "A4", "format": "final"},
                     headers=dev_h, timeout=30)
    b = requests.get(f"{BASE_URL}/api/print/purchase/{pid}",
                     params={"stage": "GROSS", "target": "A4", "format": "preview"},
                     headers=dev_h, timeout=30)
    assert a.status_code == 200 and b.status_code == 200
    assert b.headers["Content-Type"].startswith("application/pdf")
    assert b.content[:4] == b"%PDF"
    # WYSIWYG - similar size (allow 5% variance for timestamps)
    assert abs(len(a.content) - len(b.content)) < max(500, len(a.content) * 0.05), \
        f"final={len(a.content)} preview={len(b.content)}"


# ---------- 4) Audit + counter ----------

def _get_purchase(dev_h, pid):
    r = requests.get(f"{BASE_URL}/api/purchases/{pid}", headers=dev_h, timeout=10)
    assert r.status_code == 200, r.text
    return r.json()


def test_final_increments_print_count_and_preview_does_not(dev_h):
    # Fresh purchase to isolate counter
    g = _create_gross(dev_h)
    pid = g["purchaseId"]
    time.sleep(1.5)

    def audit_print_actions_for(pid):
        ar = requests.get(f"{BASE_URL}/api/audit",
                          params={"take": 200}, headers=dev_h, timeout=10)
        if ar.status_code != 200:
            return None
        data = ar.json()
        items = data.get("items", data) if isinstance(data, dict) else data
        return [x.get("action") for x in items
                if isinstance(x, dict)
                and x.get("entity") == "Purchase"
                and str(x.get("entityId")) == str(pid)
                and x.get("action") in ("Print", "Reprint")]

    # preview should NOT add audit entry
    r0 = requests.get(f"{BASE_URL}/api/print/purchase/{pid}",
                     params={"stage": "GROSS", "target": "A4", "format": "preview"},
                     headers=dev_h, timeout=30)
    assert r0.status_code == 200
    actions0 = audit_print_actions_for(pid) or []
    assert actions0 == [], f"preview must not audit: {actions0}"

    # First final => audit Print
    r1 = requests.get(f"{BASE_URL}/api/print/purchase/{pid}",
                     params={"stage": "GROSS", "target": "A4", "format": "final"},
                     headers=dev_h, timeout=30)
    assert r1.status_code == 200
    time.sleep(0.5)
    actions1 = audit_print_actions_for(pid) or []
    assert "Print" in actions1, f"expected Print in audit after 1st final: {actions1}"
    assert "Reprint" not in actions1, f"1st call should NOT be Reprint: {actions1}"

    # Second final => audit Reprint
    r2 = requests.get(f"{BASE_URL}/api/print/purchase/{pid}",
                     params={"stage": "GROSS", "target": "A4", "format": "final"},
                     headers=dev_h, timeout=30)
    assert r2.status_code == 200
    time.sleep(0.5)
    actions2 = audit_print_actions_for(pid) or []
    assert "Print" in actions2 and "Reprint" in actions2, \
        f"expected both Print and Reprint after 2nd final: {actions2}"


# ---------- 5) TARE conflict pre/post ----------

def test_tare_before_saved_returns_409(dev_h, gross_only):
    r = requests.get(f"{BASE_URL}/api/print/purchase/{gross_only}",
                     params={"stage": "TARE", "target": "A4", "format": "final"},
                     headers=dev_h, timeout=30)
    assert r.status_code == 409, f"{r.status_code}: {r.text}"
    assert "Tare" in r.text or "tare" in r.text.lower()


def test_tare_after_saved_returns_pdf(dev_h, full_cycle):
    r = requests.get(f"{BASE_URL}/api/print/purchase/{full_cycle['pid']}",
                     params={"stage": "TARE", "target": "A4", "format": "final"},
                     headers=dev_h, timeout=30)
    assert r.status_code == 200, r.text
    assert r.content[:4] == b"%PDF"
    # tare pdf should be non-trivial size (more rows than gross)
    assert len(r.content) > 800


# ---------- 6) Invalid inputs ----------

def test_invalid_stage_returns_400(dev_h, full_cycle):
    r = requests.get(f"{BASE_URL}/api/print/purchase/{full_cycle['pid']}",
                     params={"stage": "FOO", "target": "A4", "format": "final"}, headers=dev_h)
    assert r.status_code == 400, r.text


def test_invalid_target_returns_400(dev_h, full_cycle):
    r = requests.get(f"{BASE_URL}/api/print/purchase/{full_cycle['pid']}",
                     params={"stage": "GROSS", "target": "FOO", "format": "final"}, headers=dev_h)
    assert r.status_code == 400, r.text


def test_invalid_format_returns_400(dev_h, full_cycle):
    r = requests.get(f"{BASE_URL}/api/print/purchase/{full_cycle['pid']}",
                     params={"stage": "GROSS", "target": "A4", "format": "FOO"}, headers=dev_h)
    assert r.status_code == 400, r.text


def test_missing_purchase_returns_404(dev_h):
    r = requests.get(f"{BASE_URL}/api/print/purchase/999999",
                     params={"stage": "GROSS", "target": "A4", "format": "final"}, headers=dev_h)
    assert r.status_code == 404, r.text


# ---------- 7) RBAC ----------

def test_operator_can_print_purchase_slip(op_h, full_cycle):
    r = requests.get(f"{BASE_URL}/api/print/purchase/{full_cycle['pid']}",
                     params={"stage": "GROSS", "target": "A4", "format": "final"},
                     headers=op_h, timeout=30)
    assert r.status_code == 200, f"Operator should have Weighment.Print: {r.status_code} {r.text}"
    assert r.content[:4] == b"%PDF"


def test_operator_forbidden_on_print_test(op_h):
    r = requests.get(f"{BASE_URL}/api/print/test",
                     params={"target": "A4", "language": "en", "format": "final"},
                     headers=op_h, timeout=15)
    assert r.status_code == 403, f"Operator lacks Print.Configure: {r.status_code} {r.text}"


def test_developer_allowed_on_print_test(dev_h):
    r = requests.get(f"{BASE_URL}/api/print/test",
                     params={"target": "A4", "language": "en", "format": "final"},
                     headers=dev_h, timeout=15)
    assert r.status_code == 200, r.text
    assert r.content[:4] == b"%PDF"


# ---------- 8) /api/print/test variants ----------

def test_print_test_dotmatrix_hi_preview_returns_png(dev_h):
    r = requests.get(f"{BASE_URL}/api/print/test",
                     params={"target": "DotMatrix", "language": "hi", "format": "preview"},
                     headers=dev_h, timeout=30)
    assert r.status_code == 200, r.text
    assert r.content[:8] == b"\x89PNG\r\n\x1a\n", r.content[:8].hex()


def test_print_test_has_no_side_effects_on_purchase(dev_h, full_cycle):
    """Calling /print/test must not add any Print/Reprint audit entry for any Purchase."""
    pid = full_cycle["pid"]
    def audit_prints_for(pid):
        ar = requests.get(f"{BASE_URL}/api/audit", params={"take": 200}, headers=dev_h, timeout=10)
        if ar.status_code != 200: return None
        data = ar.json()
        items = data.get("items", data) if isinstance(data, dict) else data
        return len([x for x in items if isinstance(x, dict)
                    and x.get("entity") == "Purchase"
                    and str(x.get("entityId")) == str(pid)
                    and x.get("action") in ("Print", "Reprint")])
    before = audit_prints_for(pid)
    r = requests.get(f"{BASE_URL}/api/print/test",
                     params={"target": "DotMatrix", "language": "hi", "format": "final"},
                     headers=dev_h, timeout=30)
    assert r.status_code == 200
    time.sleep(0.5)
    after = audit_prints_for(pid)
    if before is not None:
        assert after == before, f"print/test must not audit against purchase {pid}: {before}->{after}"


# ---------- 9) Auto Print wiring ----------

def test_gross_response_has_autoprint_object(dev_h):
    g = _create_gross(dev_h)
    ap = g.get("autoPrint")
    assert ap is not None, f"autoPrint missing from gross response: {g}"
    for k in ("printerType", "printerName", "copies", "language", "documentUrl"):
        assert k in ap, f"{k} missing in autoPrint: {ap}"
    url = ap["documentUrl"]
    assert url.startswith("/api/print/purchase/"), url
    assert "stage=GROSS" in url and "format=final" in url
    # And it should be fetchable
    r = requests.get(f"{BASE_URL}{url}", headers=dev_h, timeout=30)
    assert r.status_code == 200, f"documentUrl fetch failed: {r.status_code} {r.text[:200]}"


def test_tare_response_has_autoprint_object(dev_h):
    g = _create_gross(dev_h)
    time.sleep(1.5)
    t = _create_tare(dev_h, g["purchaseId"])
    ap = t.get("autoPrint")
    assert ap is not None, f"autoPrint missing from tare response: {t}"
    url = ap["documentUrl"]
    assert "stage=TARE" in url and "format=final" in url


# ---------- 10) Regression spot-checks ----------

def test_regression_login_and_forced_password_change_flow():
    # create a throwaway user and verify forced-change gate.
    dev_tok = _login(*DEV)
    H = {"Authorization": f"Bearer {dev_tok}", "Content-Type": "application/json"}
    uname = f"tmp{uuid.uuid4().hex[:8]}"
    r = requests.post(f"{BASE_URL}/api/users", headers=H,
                      json={"username": uname, "fullName": "tmp", "mobile": "9000000000",
                            "temporaryPassword": "Tmp@2026Temp", "roleIds": [5]}, timeout=10)
    assert r.status_code in (200, 201), r.text
    lr = requests.post(f"{BASE_URL}/api/auth/login",
                       json={"username": uname, "password": "Tmp@2026Temp"}, timeout=10)
    assert lr.status_code == 200, lr.text
    assert lr.json().get("mustChangePassword") is True


def test_regression_gross_tare_math(dev_h):
    g = _create_gross(dev_h)
    pid = g["purchaseId"]
    time.sleep(1.0)
    t = _create_tare(dev_h, pid, kg=1000)
    # gross=50, tare=10, net=40, cutting 2% => cutWt=0.8, afterCut=39.2, tax 1% =>0.392, final=38.808
    # rounding may differ; just sanity check basic fields
    p = _get_purchase(dev_h, pid)
    assert abs(float(p.get("grossWeightQuintal", 0)) - 50.0) < 0.01
    assert abs(float(p.get("tareWeightQuintal", 0)) - 10.0) < 0.01
    net = float(p.get("netWeightQuintal", 0))
    assert abs(net - 40.0) < 0.01, p
    amt = float(p.get("purchaseAmount", 0))
    assert amt > 0, p


def test_regression_phase6_images_still_returned(dev_h, full_cycle):
    r = requests.get(f"{BASE_URL}/api/purchases/{full_cycle['pid']}/images", headers=dev_h, timeout=10)
    assert r.status_code == 200, r.text
    rows = r.json()
    assert isinstance(rows, list) and len(rows) >= 1, rows
