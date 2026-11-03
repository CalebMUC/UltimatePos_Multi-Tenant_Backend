"""Creates a demo business through the API and loads it: units, categories, products, packs, suppliers, customers, bundles and the
opening stock (one purchase order per supplier, received). Each demo script (setup_hardware_demo.py, setup_electrical_demo.py)
describes its business and calls run(). See DEMO_HARDWARE_AND_ELECTRICAL.md §3.

Safe to run again: every step skips what already exists. Needs `requests` and `openpyxl`. Refuses any API that is not on localhost
unless --allow-remote is given: demo data does not belong in production.
"""
import argparse
import datetime as dt
import sys
from pathlib import Path
from urllib.parse import urlparse

import requests
from openpyxl import load_workbook

HERE = Path(__file__).resolve().parent
WHOLESALE, RETAIL = 0, 1
SETTLEMENT_BANK = 2
COMPLIMENTARY, LICENCE_ACTIVE, PO_CANCELLED = 2, 0, 4


class Api:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.token = None

    def call(self, method, path, json=None, params=None, files=None):
        headers = {"Authorization": f"Bearer {self.token}"} if self.token else {}
        r = requests.request(method, f"{self.base}{path}", json=json, params=params, files=files, headers=headers, timeout=120)
        body = r.json() if r.content and r.headers.get("content-type", "").startswith("application/json") else {}
        if not r.ok or body.get("success") is False:
            raise SystemExit(f"{method} {path} -> {r.status_code}: {body or r.text[:500]}")
        return body.get("data")

    def login(self, email, password, business_id=None):
        data = self.call("POST", "/auth/Login", {"email": email, "password": password, "businessId": business_id})
        self.token = data["token"]
        return data


def step(msg):
    print(f"-- {msg}")


def r5(x):
    step_ = 5 if x < 100 else 10 if x < 2000 else 50
    return int(round(x / step_) * step_)


def read_build_sheet(file):
    ws = load_workbook(HERE / file).active
    return [dict(name=r[0], unit=r[4], wholesale=r[6], retail=r[7], cost=r[10], qty=r[11], supplier=r[12], pack_unit=r[13],
                 pack_size=r[14], pack_retail=r[15], pack_wholesale=r[16])
            for r in ws.iter_rows(min_row=2, values_only=True)]


def run(demo):
    """demo: business, owner, units [(name, symbol)], files (categories, products), first_department (a department code in the
    categories file), suppliers {name: (terms, "full" | "half" | None)}, customers [tuples], bundles [(name, code, [(product, qty)])]."""
    ap = argparse.ArgumentParser()
    ap.add_argument("--api", default="http://localhost:5062/api/v1")
    ap.add_argument("--super-email", default="superuser@ultimatepos.local")
    ap.add_argument("--super-password", default="SuperUser@12345")
    ap.add_argument("--owner-password", help="The owner's password, if it was changed after the first run")
    ap.add_argument("--allow-remote", action="store_true")
    args = ap.parse_args()
    if urlparse(args.api).hostname not in ("localhost", "127.0.0.1") and not args.allow_remote:
        sys.exit("Not a local API. Pass --allow-remote only for a demo or staging environment, never production.")

    business, owner = demo["business"], demo["owner"]
    api = Api(args.api)

    # 1. Business and licence, as the super user
    api.login(args.super_email, args.super_password)
    existing = [b for b in api.call("GET", "/platform/businesses") if b["business"]["kraPin"] == business["kraPin"]]
    if existing:
        business_id = existing[0]["business"]["businessId"]
        step(f"business exists: {business_id}")
    else:
        business_id = api.call("POST", "/businesses", {**business, "owner": owner})["business"]["businessId"]
        step(f"business registered: {business_id}")

    licenses = api.call("GET", f"/platform/businesses/{business_id}/licenses")
    if not any(l["kind"] == COMPLIMENTARY and l["state"] == LICENCE_ACTIVE for l in licenses):
        # No start date: it follows on from the trial, so the two never overlap.
        api.call("POST", f"/platform/businesses/{business_id}/licenses",
                 {"kind": COMPLIMENTARY, "planName": "Demo", "months": 12, "amount": 0, "notes": f"{business['tradingName']} demo"})
        step("complimentary 12-month licence issued")
    else:
        step("complimentary licence exists")

    # 2. Everything else as the owner
    session = api.login(owner["email"], args.owner_password or owner["password"], business_id)
    if session.get("mustChangePassword"):
        step("note: the owner must change the password at first sign-in in the app")

    units = {u["symbol"]: u["unitOfMeasureId"] for u in api.call("GET", "/units-of-measure/get-units")}
    for name, symbol in demo["units"]:
        if symbol not in units:
            units[symbol] = api.call("POST", "/units-of-measure/create-unit", {"name": name, "symbol": symbol})["unitOfMeasureId"]
            step(f"unit {symbol} created")

    categories_file, products_file = demo["files"]
    # A tree: departments at the top, categories under each.
    if not any(c.get("code") == demo["first_department"] for c in api.call("GET", "/categories/get-categories")):
        upload(api, "/categories/bulk-upload", HERE / categories_file)
        step("categories uploaded")
    else:
        step("categories exist")

    products = list_products(api)
    if not products:
        upload(api, "/products/bulk-upload", HERE / products_file)
        products = list_products(api)
        step(f"{len(products)} products uploaded")
    else:
        step(f"{len(products)} products exist")
    by_name = {p["name"]: p for p in products}

    # 3. Packs and their prices
    sheet = read_build_sheet(products_file)
    rows = {r["name"]: r for r in sheet}
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    added = 0
    for row in (r for r in sheet if r["pack_unit"]):
        pid, pack_unit = by_name[row["name"]]["productId"], units[row["pack_unit"]]
        conversions = api.call("GET", f"/products/{pid}/unit-conversions")
        if not any(c["packUnitOfMeasureId"] == pack_unit for c in conversions):
            api.call("POST", f"/products/{pid}/unit-conversions", {"packUnitOfMeasureId": pack_unit, "conversionFactor": row["pack_size"]})
            added += 1
        tiers = api.call("GET", f"/products/{pid}/price-tiers")
        for price_type, price in ((RETAIL, row["pack_retail"]), (WHOLESALE, row["pack_wholesale"])):
            if not any(t["unitOfMeasureId"] == pack_unit and t["priceType"] == price_type for t in tiers):
                api.call("POST", f"/products/{pid}/price-tiers",
                         {"unitOfMeasureId": pack_unit, "priceType": price_type, "price": price, "effectiveFrom": now})
    step(f"packs: {added} added, {sum(1 for r in sheet if r['pack_unit'])} in the build sheet")

    # 4. Suppliers and customers
    suppliers = {s["supplierName"]: s["supplierId"] for s in api.call("GET", "/suppliers")}
    for i, (name, (terms, _)) in enumerate(demo["suppliers"].items(), start=1):
        if name not in suppliers:
            suppliers[name] = api.call("POST", "/suppliers", {
                "businessId": business_id, "supplierName": name, "kraPin": None, "contactPerson": None,
                "phoneNumber": f"{demo['phone_prefix']}2{i:02d}", "email": None, "physicalAddress": None, "paymentTermDays": terms})["supplierId"]
            step(f"supplier {name}")

    customers = {c["customerName"]: c["customerId"] for c in api.call("GET", "/customers")}
    for name, pin, contact, phone, address, price_type, limit, discount, note in demo["customers"]:
        if name in customers:
            continue
        cid = api.call("POST", "/customers", {
            "businessId": business_id, "customerName": name, "kraPin": pin, "contactPerson": contact, "phoneNumber": phone,
            "email": None, "physicalAddress": address, "priceType": price_type, "creditLimit": limit})["customerId"]
        if discount:
            api.call("PUT", f"/customers/{cid}/discount", {"discountPercent": discount, "note": note})
        step(f"customer {name}")

    # 5. Bundles, priced about 3% below their parts
    bundles = {b["name"] for b in api.call("GET", "/product-bundles")}
    for name, code, parts in demo["bundles"]:
        if name in bundles:
            continue
        retail = r5(sum(rows[p]["retail"] * q for p, q in parts) * 0.97)
        wholesale = r5(sum(rows[p]["wholesale"] * q for p, q in parts) * 0.97)
        api.call("POST", "/product-bundles", {
            "name": name, "code": code, "description": None, "retailPrice": retail, "wholesalePrice": wholesale,
            "components": [{"productId": by_name[p]["productId"], "unitOfMeasureId": by_name[p]["baseUnitOfMeasureId"], "quantity": q}
                           for p, q in parts]})
        step(f"bundle {name}: KES {retail:,} (contractor {wholesale:,})")

    # 6. Opening stock: one purchase order per supplier, received in full against an invoice number
    ordered_from = {o["supplierId"] for o in api.call("GET", "/purchase-orders", params={"pageSize": 100}) if o["status"] != PO_CANCELLED}
    for n, (name, (_, pay)) in enumerate(demo["suppliers"].items(), start=1):
        supplier_id = suppliers[name]
        if supplier_id in ordered_from:
            step(f"opening order from {name} exists")
            continue
        lines = [{"productId": by_name[r["name"]]["productId"], "unitOfMeasureId": units[r["unit"]],
                  "quantityOrdered": r["qty"], "unitCost": r["cost"]} for r in sheet if r["supplier"] == name]
        po = api.call("POST", "/purchase-orders", {"supplierId": supplier_id, "expectedDeliveryDate": None,
                                                   "notes": "Opening stock", "lines": lines})
        invoice = f"INV-{demo['invoice_base'] + n * 17}"
        api.call("POST", f"/purchase-orders/{po['purchaseOrderId']}/receive", {
            "lines": [{"purchaseOrderLineId": l["purchaseOrderLineId"], "quantityReceived": l["quantityOrdered"]} for l in po["lines"]],
            "notes": "Opening stock", "supplierInvoiceNumber": invoice})
        value = sum(l["quantityOrdered"] * l["unitCost"] for l in lines)
        step(f"{po['orderNumber']} from {name}: {len(lines)} lines, KES {value:,.0f}, received on {invoice}")
        if pay:
            amount = round(value if pay == "full" else value / 2, 2)
            api.call("POST", f"/suppliers/{supplier_id}/payments", {
                "amount": amount, "paymentMethod": SETTLEMENT_BANK, "paymentReference": f"TRF-{invoice}", "notes": f"Payment for {invoice}"})
            step(f"paid {name} KES {amount:,.0f}")

    print(f"\nDone. Sign in as {owner['email']} / {args.owner_password or owner['password']} (business {business_id}).")


def upload(api, path, file):
    for dry_run in (True, False):
        with open(file, "rb") as f:
            result = api.call("POST", path, params={"dryRun": str(dry_run).lower()},
                              files={"file": (file.name, f, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")})
        bad = [r for r in result.get("rows", []) if not r.get("isValid")]
        if bad:
            raise SystemExit(f"{file.name}: {len(bad)} invalid rows, e.g. {bad[:5]}")


def list_products(api):
    return api.call("GET", "/products/get-products", params={"pageSize": 200, "isActive": "true"}) or []
