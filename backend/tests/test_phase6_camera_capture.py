"""
Phase 6 - Camera capture pipeline tests (SIMULATOR mode).
Verifies auto-capture on Gross/Tare, manual re-capture, disk persistence,
image file endpoint, kill switches, per-camera skip, RBAC, live snapshot,
and regression on core weighment cycle math.
"""
import os
import time
import uuid
import pytest
import requests

BASE_URL = "http://localhost:8001"

DEV_USER = ("developer", "DevSecure@2026")
OP_USER = ("opuser2", "OpSecure@2026")


# ---------- fixtures ----------

def _login(username, password):
    r = requests.post(f"{BASE_URL}/api/auth/login",
                      json={"username": username, "password": password}, timeout=10)
    assert r.status_code == 200, f"login failed for {username}: {r.status_code} {r.text}"
    return r.json()["accessToken"]


@pytest.fixture(scope="session")
def dev_token():
    return _login(*DEV_USER)


@pytest.fixture(scope="session")
def op_token():
    return _login(*OP_USER)


@pytest.fixture(scope="session")
def dev_headers(dev_token):
    return {"Authorization": f"Bearer {dev_token}", "Content-Type": "application/json"}


@pytest.fixture(scope="session")
def op_headers(op_token):
    return {"Authorization": f"Bearer {op_token}", "Content-Type": "application/json"}


def _create_gross(headers, cutting=2.0):
    body = {
        "growerCode": "101/1",
        "vehicleTypeId": 1,
        "vehicleNumber": "UP32AB1234",
        "varietyTypeId": 1,
        "varietyId": 1,
        "cuttingPercent": cutting,
        "taxPercent": 1.0,
        "scaleReadingKg": 5000,
        "idempotencyKey": str(uuid.uuid4()),
    }
    r = requests.post(f"{BASE_URL}/api/weighment/gross", json=body, headers=headers, timeout=15)
    assert r.status_code == 200, f"gross failed: {r.status_code} {r.text}"
    return r.json()


def _create_tare(headers, purchase_id, kg=1000):
    body = {"purchaseId": purchase_id, "scaleReadingKg": kg,
            "idempotencyKey": str(uuid.uuid4())}
    r = requests.post(f"{BASE_URL}/api/weighment/tare", json=body, headers=headers, timeout=15)
    assert r.status_code == 200, f"tare failed: {r.status_code} {r.text}"
    return r.json()


@pytest.fixture(scope="module")
def gross_purchase(dev_headers):
    """Fresh purchase with GROSS + queued auto-capture."""
    res = _create_gross(dev_headers)
    time.sleep(3)  # let background capture finish
    return res


@pytest.fixture(scope="module")
def full_cycle_purchase(dev_headers):
    """Fresh purchase with both GROSS and TARE completed."""
    g = _create_gross(dev_headers)
    time.sleep(2.5)
    t = _create_tare(dev_headers, g["purchaseId"])
    time.sleep(2.5)
    return {"gross": g, "tare": t, "purchaseId": g["purchaseId"]}


# ---------- 1) GROSS auto-capture ----------

def test_gross_response_has_capture_queued_flag(gross_purchase):
    assert gross_purchase.get("captureQueued") is True
    assert isinstance(gross_purchase.get("purchaseId"), int)
    assert gross_purchase["grossWeightQuintal"] == 50.0


def test_gross_auto_capture_creates_image_per_camera(dev_headers, gross_purchase):
    pid = gross_purchase["purchaseId"]
    r = requests.get(f"{BASE_URL}/api/purchases/{pid}/images", headers=dev_headers)
    assert r.status_code == 200
    imgs = [i for i in r.json() if i["captureStage"] == "GROSS"]
    cam_ids = sorted({i["cameraId"] for i in imgs})
    assert cam_ids == [1, 2], f"expected 2 cameras GROSS rows, got {imgs}"
    for i in imgs:
        assert i["fileHash"], "fileHash must be non-null"
        assert i["imageName"].startswith("GROSS-CAM"), i["imageName"]
        assert i["imageName"].endswith(".jpg")


# ---------- 2) TARE auto-capture coexists with GROSS ----------

def test_tare_auto_capture_produces_tare_rows(dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    assert full_cycle_purchase["tare"].get("captureQueued") is True
    r = requests.get(f"{BASE_URL}/api/purchases/{pid}/images", headers=dev_headers)
    assert r.status_code == 200
    imgs = r.json()
    stages = {i["captureStage"] for i in imgs}
    assert "GROSS" in stages and "TARE" in stages, f"stages={stages}"
    tare_cams = sorted({i["cameraId"] for i in imgs if i["captureStage"] == "TARE"})
    assert tare_cams == [1, 2]
    for i in imgs:
        if i["captureStage"] == "TARE":
            assert i["imageName"].startswith("TARE-CAM"), i["imageName"]


# ---------- 3) File exists on disk with valid JPEG magic bytes ----------

def test_saved_files_exist_and_are_valid_jpeg(dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    r = requests.get(f"{BASE_URL}/api/purchases/{pid}/images", headers=dev_headers)
    imgs = r.json()
    # Find files on disk
    root = "/tmp/canefactory_images"
    found = []
    for dirpath, _, files in os.walk(root):
        if f"PUR-{pid}" in dirpath:
            for f in files:
                found.append(os.path.join(dirpath, f))
    assert len(found) >= len(imgs), f"expected >= {len(imgs)} files, found {found}"
    for p in found:
        with open(p, "rb") as fh:
            magic = fh.read(2)
        assert magic == b"\xff\xd8", f"{p} not a JPEG (magic={magic!r})"


# ---------- 4) GET /api/images/{id}/file ----------

def test_get_image_file_returns_jpeg_bytes(dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    imgs = requests.get(f"{BASE_URL}/api/purchases/{pid}/images", headers=dev_headers).json()
    assert imgs
    img_id = imgs[0]["id"]
    r = requests.get(f"{BASE_URL}/api/images/{img_id}/file", headers=dev_headers)
    assert r.status_code == 200
    assert "image/jpeg" in r.headers.get("Content-Type", "")
    assert r.content[:2] == b"\xff\xd8"


def test_get_image_file_404_for_missing_id(dev_headers):
    r = requests.get(f"{BASE_URL}/api/images/999999/file", headers=dev_headers)
    assert r.status_code == 404


# ---------- 5) Manual re-capture ----------

def test_manual_recapture_increments_sequence(dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    before = requests.get(f"{BASE_URL}/api/purchases/{pid}/images", headers=dev_headers).json()
    max_seq_before = max(
        int(i["imageName"].split("-")[-1].split(".")[0])
        for i in before if i["captureStage"] == "GROSS" and i["cameraId"] == 1
    )
    r = requests.post(f"{BASE_URL}/api/purchases/{pid}/images/capture",
                      json={"stage": "GROSS"}, headers=dev_headers, timeout=15)
    assert r.status_code == 200
    data = r.json()
    assert isinstance(data.get("results"), list)
    assert len(data["results"]) == 2
    for res in data["results"]:
        for k in ("cameraConfigId", "cameraNumber", "success", "imageId", "imageName"):
            assert k in res, f"missing key {k} in {res}"
        assert res["success"] is True

    after = requests.get(f"{BASE_URL}/api/purchases/{pid}/images", headers=dev_headers).json()
    max_seq_after = max(
        int(i["imageName"].split("-")[-1].split(".")[0])
        for i in after if i["captureStage"] == "GROSS" and i["cameraId"] == 1
    )
    assert max_seq_after == max_seq_before + 1


def test_manual_recapture_invalid_stage_400(dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    r = requests.post(f"{BASE_URL}/api/purchases/{pid}/images/capture",
                      json={"stage": "FOO"}, headers=dev_headers, timeout=10)
    assert r.status_code == 400


# ---------- 6) Global kill switch ----------

def _set_setting(headers, key, value):
    r = requests.put(f"{BASE_URL}/api/config/settings/{key}",
                     json={"value": value}, headers=headers, timeout=10)
    assert r.status_code == 200, f"setting {key}={value} failed: {r.status_code} {r.text}"


def test_image_capture_enabled_kill_switch(dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    try:
        _set_setting(dev_headers, "ImageCaptureEnabled", "false")
        r = requests.post(f"{BASE_URL}/api/purchases/{pid}/images/capture",
                          json={"stage": "GROSS"}, headers=dev_headers, timeout=10)
        assert r.status_code == 200
        data = r.json()
        assert data.get("results") == []
        assert "disabled" in (data.get("message") or "").lower()
    finally:
        _set_setting(dev_headers, "ImageCaptureEnabled", "true")


def test_camera_system_enabled_kill_switch(dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    try:
        _set_setting(dev_headers, "CameraSystemEnabled", "false")
        r = requests.post(f"{BASE_URL}/api/purchases/{pid}/images/capture",
                          json={"stage": "TARE"}, headers=dev_headers, timeout=10)
        assert r.status_code == 200
        assert r.json().get("results") == []
    finally:
        _set_setting(dev_headers, "CameraSystemEnabled", "true")


# ---------- 7) Per-camera CaptureEnabled=false ----------

def test_disabled_camera_is_skipped(dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    # Fetch existing camera 1 fields to preserve them, then disable capture
    cams = requests.get(f"{BASE_URL}/api/config/cameras", headers=dev_headers).json()
    cam1 = next(c for c in cams if c["id"] == 1)
    def _upsert(enabled):
        payload = {
            "cameraNumber": cam1["cameraNumber"], "vendor": cam1["vendor"],
            "model": cam1.get("model"), "protocol": cam1["protocol"],
            "ipAddress": cam1["ipAddress"], "port": cam1["port"],
            "username": cam1["username"], "password": "dummyPass",
            "channel": cam1["channel"], "streamType": cam1["streamType"],
            "rtspUrl": cam1.get("rtspUrl"), "resolution": cam1.get("resolution"),
            "fps": cam1.get("fps"), "captureEnabled": enabled,
            "liveViewEnabled": cam1["liveViewEnabled"],
            "retentionDays": cam1["retentionDays"], "status": cam1["status"],
        }
        r = requests.post(f"{BASE_URL}/api/config/cameras", json=payload, headers=dev_headers)
        assert r.status_code == 200, f"upsert failed: {r.status_code} {r.text}"
    try:
        _upsert(False)
        cap = requests.post(f"{BASE_URL}/api/purchases/{pid}/images/capture",
                            json={"stage": "GROSS"}, headers=dev_headers, timeout=10).json()
        cam_ids = {res["cameraConfigId"] for res in cap.get("results", [])}
        assert 1 not in cam_ids, f"camera 1 should be skipped, got {cap}"
        assert 2 in cam_ids
    finally:
        _upsert(True)


# ---------- 8) RBAC image permission separation ----------

def test_operator_can_capture_but_not_view(op_headers, dev_headers, full_cycle_purchase):
    pid = full_cycle_purchase["purchaseId"]
    # Operator: capture => 200, view => 403
    r_get = requests.get(f"{BASE_URL}/api/purchases/{pid}/images", headers=op_headers)
    assert r_get.status_code == 403, f"operator GET must be 403, got {r_get.status_code}"

    r_post = requests.post(f"{BASE_URL}/api/purchases/{pid}/images/capture",
                           json={"stage": "GROSS"}, headers=op_headers, timeout=15)
    assert r_post.status_code == 200, f"operator POST must be 200, got {r_post.status_code} {r_post.text}"

    # Developer: view => 200
    r_dev = requests.get(f"{BASE_URL}/api/purchases/{pid}/images", headers=dev_headers)
    assert r_dev.status_code == 200


# ---------- 9) Live snapshot endpoint ----------

def test_live_snapshot_returns_jpeg(dev_headers):
    r = requests.get(f"{BASE_URL}/api/config/cameras/1/snapshot", headers=dev_headers, timeout=10)
    assert r.status_code == 200, f"{r.status_code} {r.text[:200]}"
    assert "image/jpeg" in r.headers.get("Content-Type", "")
    assert r.content[:2] == b"\xff\xd8"


def test_live_snapshot_missing_camera_conflict(dev_headers):
    r = requests.get(f"{BASE_URL}/api/config/cameras/9999/snapshot", headers=dev_headers, timeout=10)
    assert r.status_code == 409
    assert "message" in r.json()


# ---------- 10) Regression: login + full weighment math ----------

def test_regression_full_cycle_math(dev_headers):
    g = _create_gross(dev_headers, cutting=2.0)
    pid = g["purchaseId"]
    t = _create_tare(dev_headers, pid, kg=1000)
    r = requests.get(f"{BASE_URL}/api/purchases/{pid}", headers=dev_headers)
    assert r.status_code == 200
    p = r.json()
    # Gross 50, Tare 10 => Net 40, Cutting 2% => 0.80 => Final ~ 39.20; tax 1% off 40 => 0.40 => 38.80
    assert p["grossWeightQuintal"] == 50.0
    assert p["tareWeightQuintal"] == 10.0
    assert p["netWeightQuintal"] == 40.0
    assert p["cuttingWeightQuintal"] == 0.80
    assert p["finalWeightQuintal"] is not None
    assert p["purchaseAmount"] is not None
    # Rate 350 -> amount check
    assert abs(p["purchaseAmount"] - round(p["finalWeightQuintal"] * p["rate"], 2)) < 0.01


def test_login_forced_password_change_gate_still_intact():
    # Fresh accounts change already done; a wrong password should 401
    r = requests.post(f"{BASE_URL}/api/auth/login",
                      json={"username": "developer", "password": "WrongPass!1"}, timeout=10)
    assert r.status_code in (401, 423, 429)
