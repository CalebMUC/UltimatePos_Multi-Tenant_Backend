"""Creates a demo business through the API and loads it: units, categories, products, packs, suppliers, customers, bundles, the
opening stock (one purchase order per supplier, received), and optionally roles and staff. Each demo script (setup_hardware_demo.py,
setup_electrical_demo.py, setup_minimart_demo.py, setup_chemist_demo.py, setup_liquor_demo.py) describes its business and calls run().
See DEMO_HARDWARE_AND_ELECTRICAL.md §3, DEMO_MINIMART_AND_CHEMIST.md §3 and DEMO_LIQUOR_STORE.md §3.

What the bulk upload cannot take yet (gap M2) comes from the build sheet's extra columns, where a catalogue has them: barcode, batch
tracking, prescription-only, age-restricted and the medicine's generic name, strength and dosage form are set on each product after
the upload, the container a pack is sold with (a crate's deposit) is set on the pack, and batch-tracked stock is received in named
batches with expiry dates.

Safe to run again: every step skips what already exists. Needs `requests` and `openpyxl`. Refuses any API that is not on localhost
unless --allow-remote is given: demo data does not belong in production.
"""
import argparse
import datetime as dt
import sys
import zlib
from pathlib import Path
from urllib.parse import urlparse

import requests
from openpyxl import load_workbook

HERE = Path(__file__).resolve().parent
WHOLESALE, RETAIL = 0, 1
SETTLEMENT_BANK = 2
COMPLIMENTARY, LICENCE_ACTIVE, PO_CANCELLED = 2, 0, 4

# Role permissions, as in MINIMART_ONBOARDING.md §10. A cashier cannot change a price, adjust stock, void or dispense.
COUNTER = ["Sales.Create", "Sales.View", "Customers.View", "Customers.Register", "Customers.RecordPayment", "Products.View",
           "Categories.View", "Units.View", "Stock.View", "Mpesa.View", "CashSessions.View", "CashSessions.RecordMovement"]
MANAGER = COUNTER + [
    "Sales.Void", "Sales.Return", "Sales.ApplyDiscount", "Sales.OverrideCreditLimit", "Customers.Update", "Customers.ViewLedger",
    "Customers.ViewInsights", "Customers.ManageDiscount", "Products.Create", "Products.Update", "Products.ManagePricing",
    "Products.ManageUnitConversions", "Products.ManageBundles", "Suppliers.Register", "Suppliers.View", "Suppliers.Update",
    "Suppliers.ViewLedger", "Suppliers.RecordPayment", "PurchaseOrders.Create", "PurchaseOrders.View", "PurchaseOrders.Receive",
    "PurchaseOrders.Cancel", "Stock.Adjust", "CashSessions.Open", "CashSessions.Close", "Costing.View", "ExpenseCategories.View",
    "Expenses.Create", "Expenses.View", "Dashboard.View", "SalesReports.View", "CustomerReports.View", "InventoryReports.View",
    "PurchaseReports.View"]
STOREKEEPER = ["Suppliers.View", "PurchaseOrders.View", "PurchaseOrders.Receive", "Stock.View", "Products.View", "Categories.View",
               "Units.View"]
DELIVERIES = ["DeliveryOrders.View", "DeliveryOrders.Create", "DeliveryOrders.Dispatch", "DeliveryOrders.Deliver",
              "DeliveryOrders.Cancel", "DeliveryOrders.Return"]
DISPENSING = ["Prescriptions.Dispense", "Prescriptions.View"]
# How a customer's age was checked (AgeCheckMethod), for alcohol and tobacco.
NATIONAL_ID, PASSPORT, DRIVING_LICENCE, OTHER_ID, KNOWN_CUSTOMER = 0, 1, 2, 3, 4


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


# Build sheet columns by their header, without the "(build)" suffix; costs are headed "Unit Cost" or "Unit Cost ex VAT".
COLUMNS = {"Name": "name", "Unit Symbol": "unit", "Wholesale Price": "wholesale", "Retail Price": "retail", "Unit Cost": "cost",
           "Unit Cost ex VAT": "cost", "Opening Qty": "qty", "Supplier": "supplier", "Pack Unit": "pack_unit", "Pack Size": "pack_size",
           "Pack Retail": "pack_retail", "Pack Wholesale": "pack_wholesale", "Barcode": "barcode", "Batch Tracked": "batch",
           "Shelf Life Days": "life", "First Batch Expires In Days": "near", "Prescription Only": "rx", "Generic Name": "generic",
           "Strength": "strength", "Dosage Form": "form", "Age Restricted": "age", "Pack Container": "container"}


def read_build_sheet(file):
    ws = load_workbook(HERE / file).active
    rows = ws.iter_rows(values_only=True)
    keys = [COLUMNS.get((h or "").replace("(build)", "").strip()) for h in next(rows)]
    sheet = []
    for r in rows:
        row = {k: v for k, v in zip(keys, r) if k}
        row["batch"], row["rx"], row["age"] = row.get("batch") == "Y", row.get("rx") == "Y", row.get("age") == "Y"
        sheet.append(row)
    return sheet


def batch_number(name, year, salt=""):
    """A lot number in the style printed on packs, the same on every run: the product's initials, the year, four digits."""
    code = "".join(w[0] for w in name.split() if w[0].isalpha()).upper()[:4]
    return f"{code}{year % 100:02d}{zlib.crc32((name + salt).encode()) % 9000 + 1000}"


def batches_for(row, today):
    """The opening stock of a batch-tracked product as (batch number, expiry, quantity): an older lot first where there is a lot of
    stock, so first-expiry-first-out has something to choose, and a lot that expires soon where the build sheet asks for one (near)."""
    qty, life = row["qty"], row.get("life") or 730
    newer = (batch_number(row["name"], today.year), today + dt.timedelta(days=life))
    if row.get("near"):
        first, expires = max(1, round(qty * 0.2)), row["near"]
    elif qty >= 200 and life >= 180:
        first, expires = round(qty * 0.3), life // 2
    else:
        return [(*newer, qty)]
    older = (batch_number(row["name"], today.year - 1, "older"), today + dt.timedelta(days=expires))
    return [(*older, first), (*newer, qty - first)]


def run(demo):
    """demo: business, owner, units [(name, symbol)], files (categories, products), first_department (a department code in the
    categories file), suppliers {name: (terms, "full" | "half" | None)}, customers [tuples], bundles [(name, code, [(product, qty)])],
    and optionally age_rules: the body of PUT /businesses/current/age-restricted-sales (minimum age, selling hours)."""
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
    if demo.get("monthly_discount"):
        threshold, percent = demo["monthly_discount"]
        api.call("PUT", "/businesses/current/monthly-discount", {"threshold": threshold, "percent": percent, "scope": 0})
        step(f"monthly-spend reward: {percent}% once a customer has spent KES {threshold:,} in the month")
    if demo.get("age_rules"):
        api.call("PUT", "/businesses/current/age-restricted-sales", demo["age_rules"])
        rules = demo["age_rules"]
        step(f"alcohol and tobacco: {rules['minimumAge']}+, sold {rules.get('weekdayFrom') or 'any time'}-{rules.get('weekdayTo') or ''}")

    units = {u["symbol"]: u["unitOfMeasureId"] for u in api.call("GET", "/units-of-measure/get-units")}
    for name, symbol in demo["units"]:
        if symbol not in units:
            units[symbol] = api.call("POST", "/units-of-measure/create-unit", {"name": name, "symbol": symbol})["unitOfMeasureId"]
            step(f"unit {symbol} created")
    expense_codes = {c["code"] for c in api.call("GET", "/expense-categories")}
    for code, name, description in demo.get("expense_categories", []):
        if code not in expense_codes:
            api.call("POST", "/expense-categories", {"code": code, "name": name, "description": description})
            step(f"expense category {code} created")

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
        # The crate's deposit (a product of its own) is added at the till when the customer brings no crate of empties.
        container = by_name[row["container"]]["productId"] if row.get("container") else None
        conversions = api.call("GET", f"/products/{pid}/unit-conversions")
        existing = next((c for c in conversions if c["packUnitOfMeasureId"] == pack_unit), None)
        if not existing:
            api.call("POST", f"/products/{pid}/unit-conversions",
                     {"packUnitOfMeasureId": pack_unit, "conversionFactor": row["pack_size"], "containerProductId": container})
            added += 1
        elif container and existing.get("containerProductId") != container:
            api.call("PUT", f"/products/{pid}/unit-conversions/{existing['productUnitConversionId']}/container", {"containerProductId": container})
        tiers = api.call("GET", f"/products/{pid}/price-tiers")
        for price_type, price in ((RETAIL, row["pack_retail"]), (WHOLESALE, row["pack_wholesale"])):
            if not any(t["unitOfMeasureId"] == pack_unit and t["priceType"] == price_type for t in tiers):
                api.call("POST", f"/products/{pid}/price-tiers",
                         {"unitOfMeasureId": pack_unit, "priceType": price_type, "price": price, "effectiveFrom": now})
    step(f"packs: {added} added, {sum(1 for r in sheet if r['pack_unit'])} in the build sheet")

    # 3b. What the upload cannot take: barcode, batch tracking, prescription-only, age-restricted, the medicine's details
    changed = 0
    for row in sheet:
        wanted = dict(barcode=row.get("barcode"), isBatchTracked=row["batch"], requiresPrescription=row["rx"],
                      genericName=row.get("generic"), strength=row.get("strength"), dosageForm=row.get("form"), isAgeRestricted=row["age"])
        if not any(wanted.values()):
            continue
        p = api.call("GET", f"/products/{by_name[row['name']]['productId']}")
        if all((p.get(k) or None) == (v or None) for k, v in wanted.items()):
            continue
        api.call("PUT", f"/products/{p['productId']}", {
            "name": p["name"], "description": p.get("description"), "categoryId": p["categoryId"], "itemType": p["itemType"],
            "taxClassification": p["taxClassification"], "baseUnitOfMeasureId": p["baseUnitOfMeasureId"], "reorderLevel": p["reorderLevel"],
            "manufacturer": p.get("manufacturer"), "brandId": None, **wanted})
        changed += 1
    if any(r["batch"] or r["rx"] or r["age"] or r.get("generic") or r.get("barcode") for r in sheet):
        step(f"details set on {changed} products: {sum(r['batch'] for r in sheet)} batch-tracked, "
             f"{sum(r['rx'] for r in sheet)} prescription-only, {sum(r['age'] for r in sheet)} age-restricted")

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
    bundles = {b["name"] for b in api.call("GET", "/product-bundles")} if demo["bundles"] else set()
    for name, code, parts in demo["bundles"]:
        if name in bundles:
            continue
        retail = r5(sum(rows[p]["retail"] * q for p, q in parts) * 0.97)
        wholesale = r5(sum(rows[p]["wholesale"] * q for p, q in parts) * 0.97)
        api.call("POST", "/product-bundles", {
            "name": name, "code": code, "description": None, "retailPrice": retail, "wholesalePrice": wholesale,
            "components": [{"productId": by_name[p]["productId"], "unitOfMeasureId": by_name[p]["baseUnitOfMeasureId"], "quantity": q}
                           for p, q in parts]})
        step(f"bundle {name}: KES {retail:,} (wholesale {wholesale:,})")

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
        received = []
        for l in po["lines"]:
            row = rows[l["productName"]]
            if not row["batch"]:
                received.append({"purchaseOrderLineId": l["purchaseOrderLineId"], "quantityReceived": l["quantityOrdered"]})
                continue
            for number, expiry, qty in batches_for(row, dt.date.today()):
                received.append({"purchaseOrderLineId": l["purchaseOrderLineId"], "quantityReceived": qty, "batchNumber": number,
                                 "expiryDate": expiry.isoformat()})
        api.call("POST", f"/purchase-orders/{po['purchaseOrderId']}/receive", {
            "lines": received, "notes": "Opening stock", "supplierInvoiceNumber": invoice})
        value = sum(l["quantityOrdered"] * l["unitCost"] for l in lines)
        step(f"{po['orderNumber']} from {name}: {len(lines)} lines, KES {value:,.0f}, received on {invoice}")
        if pay:
            amount = round(value if pay == "full" else value / 2, 2)
            api.call("POST", f"/suppliers/{supplier_id}/payments", {
                "amount": amount, "paymentMethod": SETTLEMENT_BANK, "paymentReference": f"TRF-{invoice}", "notes": f"Payment for {invoice}"})
            step(f"paid {name} KES {amount:,.0f}")

    # 7. Roles and staff, with one password to type in front of the client (each must change it at first sign-in)
    if demo.get("roles"):
        permissions = {p["name"]: p["permissionId"] for p in api.call("GET", "/permissions")}
        unknown = {n for _, names in demo["roles"].values() for n in names} - permissions.keys()
        if unknown:
            raise SystemExit(f"Unknown permissions: {sorted(unknown)}")
        roles = {r["roleName"]: r for r in api.call("GET", "/roles")}
        for name, (description, names) in demo["roles"].items():
            if name not in roles:
                roles[name] = api.call("POST", "/roles", {"roleName": name, "description": description})
                api.call("PUT", f"/roles/{roles[name]['roleId']}/permissions", {"permissionIds": [permissions[n] for n in names]})
                step(f"role {name}: {len(names)} permissions")
        users = {u["email"].lower() for u in api.call("GET", "/users")}
        for email, full_name, phone, role in demo["users"]:
            if email.lower() not in users:
                api.call("POST", "/auth/Register", {"email": email, "password": demo["staff_password"], "fullName": full_name,
                                                    "phoneNumber": phone, "roleName": role})
                step(f"user {email} ({role})")

    print(f"\nDone. Sign in as {owner['email']} / {args.owner_password or owner['password']} (business {business_id}).")
    if demo.get("users"):
        print(f"Staff sign in with {demo['staff_password']} and change it at first sign-in.")


def upload(api, path, file):
    for dry_run in (True, False):
        with open(file, "rb") as f:
            result = api.call("POST", path, params={"dryRun": str(dry_run).lower()},
                              files={"file": (file.name, f, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")})
        bad = [r for r in result.get("rows", []) if not r.get("isValid")]
        if bad:
            raise SystemExit(f"{file.name}: {len(bad)} invalid rows, e.g. {bad[:5]}")


def list_products(api):
    products, page = [], 1
    while True:
        batch = api.call("GET", "/products/get-products", params={"page": page, "pageSize": 200, "isActive": "true"}) or []
        products += batch
        if len(batch) < 200:
            return products
        page += 1
