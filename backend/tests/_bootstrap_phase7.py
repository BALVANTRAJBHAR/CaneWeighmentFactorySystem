"""Bootstrap seed data + opuser2 for Phase 7 tests. Idempotent-ish (uses IF-NOT-EXISTS checks)."""
import requests, uuid, sys
BASE = "http://localhost:8001"

def login(u,p):
    r = requests.post(f"{BASE}/api/auth/login", json={"username":u,"password":p}, timeout=15)
    r.raise_for_status()
    return r.json()

def post(hdr, path, body):
    r = requests.post(f"{BASE}{path}", headers=hdr, json=body, timeout=30)
    print(path, r.status_code, r.text[:200])
    return r

def main():
    try:
        dev = login("developer","Dev@2026Temp")
    except Exception:
        dev = login("developer","DevSecure@2026")
    if dev.get("mustChangePassword"):
        h = {"Authorization": f"Bearer {dev['accessToken']}", "Content-Type": "application/json"}
        r = requests.post(f"{BASE}/api/auth/change-password", headers=h,
            json={"currentPassword":"Dev@2026Temp","newPassword":"DevSecure@2026","confirmPassword":"DevSecure@2026"}, timeout=10)
        print("change-pwd", r.status_code, r.text[:200])
        dev = login("developer","DevSecure@2026")
    tok = dev["accessToken"]
    H = {"Authorization": f"Bearer {tok}", "Content-Type":"application/json"}

    # Zone
    post(H, "/api/zones", {"zoneName":"Zone A"})
    # Village
    post(H, "/api/villages", {"zoneId":1, "villageName":"Village A"})
    # Grower
    post(H, "/api/growers", {"villageId":101, "growerName":"Ramesh Kumar", "mobile":"9999999999", "fatherName":"Suresh"})
    # Rate
    post(H, "/api/rates", {"varietyTypeId":1, "rate":350.00, "effectiveFrom":"2026-01-01T00:00:00Z"})
    # Cameras
    post(H, "/api/config/cameras", {
        "cameraNumber":1, "displayName":"Cam1", "provider":"Hikvision", "protocol":"ISAPI",
        "host":"10.0.0.1", "port":80, "username":"admin", "password":"pw",
        "streamPath":"/ISAPI/Streaming/channels/101/picture", "captureEnabled":True, "isEnabled":True
    })
    post(H, "/api/config/cameras", {
        "cameraNumber":2, "displayName":"Cam2", "provider":"GenericRTSP", "protocol":"RTSP",
        "host":"10.0.0.2", "port":554, "username":"admin", "password":"pw",
        "streamPath":"/live", "captureEnabled":True, "isEnabled":True
    })

    # Create opuser2 (role Operator) if missing
    r = requests.get(f"{BASE}/api/users", headers=H, timeout=10)
    data = r.json() if r.status_code==200 else []
    users = data.get("items") if isinstance(data, dict) else data
    if not isinstance(users, list): users = []
    if not any((u.get("username") if isinstance(u,dict) else None)=="opuser2" for u in users):
        # need role id for Operator
        rr = requests.get(f"{BASE}/api/roles", headers=H, timeout=10)
        roles = rr.json() if rr.status_code==200 else []
        op_role = next((x for x in roles if x.get("name")=="Operator"), None)
        if not op_role:
            print("Operator role not found", roles); sys.exit(1)
        post(H, "/api/users", {
            "username":"opuser2","fullName":"Operator Two","mobile":"9111111111",
            "temporaryPassword":"OpSecure@2026Temp", "roleIds":[op_role["id"]]
        })
        # first login + change-password
        try:
            op = login("opuser2","OpSecure@2026Temp")
            oh = {"Authorization": f"Bearer {op['accessToken']}", "Content-Type":"application/json"}
            r2 = requests.post(f"{BASE}/api/auth/change-password", headers=oh,
                json={"currentPassword":"OpSecure@2026Temp","newPassword":"OpSecure@2026","confirmPassword":"OpSecure@2026"}, timeout=10)
            print("opuser2 change-pwd", r2.status_code, r2.text[:200])
        except Exception as e:
            print("opuser2 setup failed:", e)

    # Create initial purchase 1 if none
    r = requests.get(f"{BASE}/api/purchases?take=1", headers=H, timeout=10)
    print("purchases probe", r.status_code, r.text[:200])
    body = {
        "growerCode":"101/1", "vehicleTypeId":1, "vehicleNumber":"UP32AB1234",
        "varietyTypeId":1, "varietyId":1, "cuttingPercent":2.0, "taxPercent":1.0,
        "scaleReadingKg":5000, "idempotencyKey":str(uuid.uuid4())
    }
    post(H, "/api/weighment/gross", body)

if __name__=="__main__":
    main()
