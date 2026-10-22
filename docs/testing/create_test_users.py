"""
Creates three restricted users (and one role each) for testing who can see what. Safe to run more than once.

    python create_test_users.py [base-url]        default http://localhost:5062

    nopermissions@test.local   no permissions at all
    dashboard@test.local       Dashboard.View only        -> sees nothing: Dashboard.View opens the door, each section needs its own permission
    salesstaff@test.local      Dashboard.View, Sales.View, Stock.View, SalesReports.View, InventoryReports.View
                               -> sees Sales and Stock on the dashboard, but NO costs (no Costing.View)
All passwords: Test@12345   (the admin account is admin@ultimatepos.local / Admin@12345)
"""
import json, sys, urllib.request, urllib.error

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5062").rstrip("/")

USERS = {
    "nopermissions@test.local": ("Test - No permissions", []),
    "dashboard@test.local": ("Test - Dashboard only", ["Dashboard.View"]),
    "salesstaff@test.local": ("Test - Sales staff", ["Dashboard.View", "Sales.View", "Stock.View", "SalesReports.View", "InventoryReports.View"]),
}


def call(method, path, token=None, body=None):
    req = urllib.request.Request(BASE + path, method=method, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Content-Type": "application/json", **({"Authorization": f"Bearer {token}"} if token else {})})
    try:
        with urllib.request.urlopen(req) as r:
            return json.loads(r.read())["data"]
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {path} -> {e.code}\n{e.read().decode(errors='replace')}")


token = call("POST", "/api/v1/auth/Login", body={"email": "admin@ultimatepos.local", "password": "Admin@12345"})["token"]
permission_ids = {p["name"]: p["permissionId"] for p in call("GET", "/api/v1/permissions", token)}
roles = {r["roleName"]: r for r in call("GET", "/api/v1/roles", token)}

for email, (role_name, permissions) in USERS.items():
    role = roles.get(role_name) or call("POST", "/api/v1/roles", token, {"roleName": role_name, "description": "Test data"})
    call("PUT", f"/api/v1/roles/{role['roleId']}/permissions", token, {"permissionIds": [permission_ids[p] for p in permissions]})
    try:
        call("POST", "/api/v1/auth/Register", token, {"email": email, "password": "Test@12345", "fullName": role_name, "phoneNumber": "0700000000", "roleName": role_name})
        print(f"created  {email:28} {role_name}")
    except SystemExit as e:
        if "USER_EXISTS" in str(e):
            print(f"exists   {email:28} {role_name} (role permissions refreshed)")
        else:
            raise
