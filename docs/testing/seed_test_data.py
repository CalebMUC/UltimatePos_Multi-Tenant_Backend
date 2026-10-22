"""
Seeds a small, realistic month of trading into a dev database THROUGH THE API, so every figure the dashboard and reports show has
gone through the same code paths as real data (stock ledger, average costing, customer ledger, expenses).

It builds on what is already in the database: the Alvet business, its supplier (Kamau Enterprises), its customer (Famakeya Center
Limited), the Dairy and Pet finished goods and their price tiers, and the seeded expense categories. It adds only what is missing:
three raw materials, their purchases, a formula and a production run, opening stock, sales, a customer payment and expenses.

    python seed_test_data.py [base-url] [email] [password]
    defaults: http://localhost:5062  admin@ultimatepos.local  Admin@12345

Everything is dated NOW (the API stamps records itself), so query the reports with from=to=today.
It refuses to run if the database already has sales, so it cannot be run twice by accident.
"""
import json, sys, urllib.request, urllib.error
from datetime import datetime, timedelta, timezone

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5062").rstrip("/")
EMAIL = sys.argv[2] if len(sys.argv) > 2 else "admin@ultimatepos.local"
PASSWORD = sys.argv[3] if len(sys.argv) > 3 else "Admin@12345"

# Enums go over the wire as numbers: the API has no string-enum converter.
RAW_MATERIAL, FINISHED_GOOD = 0, 2
STANDARD = 0
CASH, CREDIT = 0, 1
LABOUR, UTILITIES, TRANSPORT = 0, 2, 3


def call(method, path, token=None, body=None, full=False):
    req = urllib.request.Request(BASE + path, method=method, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Content-Type": "application/json", **({"Authorization": f"Bearer {token}"} if token else {})})
    try:
        with urllib.request.urlopen(req) as r:
            envelope = json.loads(r.read())
            return envelope if full else envelope["data"]
    except urllib.error.HTTPError as e:
        sys.exit(f"\n{method} {path} -> {e.code}\n{e.read().decode(errors='replace')}\nrequest: {json.dumps(body)}")


def items(data):
    return data


def step(text):
    print(f"\n== {text}")


token = call("POST", "/api/v1/auth/Login", body={"email": EMAIL, "password": PASSWORD})["token"]
api = lambda m, p, b=None: call(m, p, token, b)

# ---------------------------------------------------------------- what already exists
business = api("GET", "/api/v1/businesses")
business = items(business)[0]
bid = business["businessId"]
units = {u["symbol"]: u["unitOfMeasureId"] for u in items(api("GET", "/api/v1/units-of-measure/get-units"))}
categories = {c["code"]: c["categoryId"] for c in items(api("GET", "/api/v1/categories/get-categories"))}
supplier = next(s for s in items(api("GET", f"/api/v1/suppliers?businessId={bid}")) if s["supplierName"] == "Kamau Enterprises")
customer = next(c for c in items(api("GET", f"/api/v1/customers?businessId={bid}")) if c["customerName"] == "Famakeya Center Limited")
expense_categories = {c["code"]: c["expenseCategoryId"] for c in items(api("GET", "/api/v1/expense-categories"))}

products = {}
page = 1
while True:
    chunk = call("GET", f"/api/v1/products/get-products?page={page}&pageSize=100", token, full=True)
    products.update({p["sku"]: p for p in chunk["data"]})
    if page >= chunk["meta"]["totalPages"]:
        break
    page += 1

if items(api("GET", "/api/v1/sales?page=1&pageSize=1"))[:1]:
    sys.exit("This database already has sales. This script only seeds an empty business; use a fresh copy of the database.")

KIND = {0: "Manufacturer", 1: "Trader"}[business["kind"]]
print(f"Business: {business['businessName']} ({KIND})  Supplier: {supplier['supplierName']}  Customer: {customer['customerName']}")
if KIND != "Manufacturer":
    sys.exit("Production and raw-material purchases need a Manufacturer business.")

basemix = products["DAIRY-FG-000002"]      # Super Dairy Cattle Basemix, kg: Wholesale 150 / Retail 160
dogmeal = products["PET-FG-000002"]        # Supreme Dog Meal, kg:          Wholesale 600 / Retail 750
nutripak = products["DAIRY-FG-000007"]     # Yeast Dairy Nutripak, g, reorder level 20
kg, g = units["kg"], units["g"]

# ---------------------------------------------------------------- raw materials
step("Raw materials (the catalogue only has finished goods)")
raw = {}
for name, reorder in [("Limestone Powder", 200), ("Dicalcium Phosphate (Feed Grade)", 100), ("Premix Salt", 50)]:
    raw[name] = api("POST", "/api/v1/products/create-product", {
        "name": name, "description": "Test data", "categoryId": categories["ADDITIVE"], "itemType": RAW_MATERIAL,
        "taxClassification": STANDARD, "baseUnitOfMeasureId": kg, "reorderLevel": reorder})
    print(f"  {raw[name]['sku']:<18} {name}")
lime, dcp, salt = raw["Limestone Powder"], raw["Dicalcium Phosphate (Feed Grade)"], raw["Premix Salt"]

# ---------------------------------------------------------------- purchases
step("Purchase orders to Kamau Enterprises")
po1 = api("POST", "/api/v1/purchase-orders", {"supplierId": supplier["supplierId"], "expectedDeliveryDate": None, "notes": "Initial raw materials", "lines": [
    {"productId": lime["productId"], "unitOfMeasureId": kg, "quantityOrdered": 500, "unitCost": 20},
    {"productId": dcp["productId"], "unitOfMeasureId": kg, "quantityOrdered": 300, "unitCost": 60},
    {"productId": salt["productId"], "unitOfMeasureId": kg, "quantityOrdered": 100, "unitCost": 15}]})
api("POST", f"/api/v1/purchase-orders/{po1['purchaseOrderId']}/receive",
    {"notes": "Delivered in full", "lines": [{"purchaseOrderLineId": l["purchaseOrderLineId"], "quantityReceived": l["quantityOrdered"]} for l in po1["lines"]]})
print(f"  {po1['orderNumber']}: 500 kg limestone @20, 300 kg DCP @60, 100 kg salt @15 - received in full")

due = (datetime.now(timezone.utc) - timedelta(days=3)).strftime("%Y-%m-%dT%H:%M:%SZ")
po2 = api("POST", "/api/v1/purchase-orders", {"supplierId": supplier["supplierId"], "expectedDeliveryDate": due, "notes": "Top-up, price went up", "lines": [
    {"productId": lime["productId"], "unitOfMeasureId": kg, "quantityOrdered": 200, "unitCost": 22}]})
api("POST", f"/api/v1/purchase-orders/{po2['purchaseOrderId']}/receive",
    {"notes": "Short delivery", "lines": [{"purchaseOrderLineId": po2["lines"][0]["purchaseOrderLineId"], "quantityReceived": 80}]})
print(f"  {po2['orderNumber']}: 200 kg limestone @22, due 3 days ago, only 80 kg arrived - an overdue open order")

# ---------------------------------------------------------------- production
step("Formula and production run for Super Dairy Cattle Basemix")
formula = api("POST", "/api/v1/formulas", {"productId": basemix["productId"], "outputQuantity": 100, "outputUnitOfMeasureId": kg, "notes": "Test formula", "lines": [
    {"ingredientProductId": lime["productId"], "unitOfMeasureId": kg, "quantity": 60},
    {"ingredientProductId": dcp["productId"], "unitOfMeasureId": kg, "quantity": 30},
    {"ingredientProductId": salt["productId"], "unitOfMeasureId": kg, "quantity": 10}]})
run = api("POST", "/api/v1/production-runs", {"formulaId": formula["formulaId"], "quantityToProduce": 200, "unitOfMeasureId": kg,
    "actualQuantityProduced": 200, "notes": "Test run", "additionalCosts": [
        {"costType": LABOUR, "description": "Two mixers, one day", "amount": 400},
        {"costType": UTILITIES, "description": "Electricity", "amount": 150}]})
print(f"  {run['runNumber']}: 200 kg made, material {run['materialCost']}, extra {run['additionalCost']}, total {run['totalCost']}, {run['costPerUnit']}/kg")

# ---------------------------------------------------------------- opening stock
step("Opening stock for products that are bought in finished (stock adjustments)")
adjust = lambda p, u, qty, reason, cost=None: api("POST", "/api/v1/stock/adjustments",
    {"productId": p["productId"], "unitOfMeasureId": u, "quantityChange": qty, "reason": reason, **({"unitCost": cost} if cost else {})})
adjust(dogmeal, kg, 100, "Opening balance", 420)
adjust(dogmeal, kg, -5, "Spillage in store")
print("  Supreme Dog Meal: +100 kg @420, then -5 kg spillage")
adjust(nutripak, g, 15, "Opening balance", 1.2)
print("  Yeast Dairy Nutripak: +15 g @1.20 (reorder level is 20, so it starts LOW)")
adjust(products["DAIRY-FG-000003"], g, 10, "Opening balance", 2)
adjust(products["DAIRY-FG-000003"], g, -10, "Written off, expired")
print("  Multimix Dairy Premix: +10 g then -10 g written off (a stock record at zero = OUT OF STOCK)")

# ---------------------------------------------------------------- sales
step("Sales")
sale = lambda customer_id, method, lines, tendered=None: api("POST", "/api/v1/sales", {
    "customerId": customer_id, "paymentMethod": method, "notes": "Test data", "amountTendered": tendered, "mpesaPhoneNumber": None, "mpesaTransactionId": None,
    "lines": [{"productId": p["productId"], "unitOfMeasureId": kg, "quantity": q} for p, q in lines]})
s1 = sale(None, CASH, [(basemix, 50), (dogmeal, 10)], 16000)
print(f"  {s1['saleNumber']}: walk-in cash, 50 kg basemix @160 + 10 kg dog meal @750 = {s1['totalAmount']}")
s2 = sale(customer["customerId"], CREDIT, [(basemix, 100)])
print(f"  {s2['saleNumber']}: Famakeya on credit, 100 kg basemix @150 (wholesale) = {s2['totalAmount']}")
s3 = sale(customer["customerId"], CREDIT, [(dogmeal, 20)])
print(f"  {s3['saleNumber']}: Famakeya on credit, 20 kg dog meal @600 (wholesale) = {s3['totalAmount']}")
s4 = sale(None, CASH, [(dogmeal, 2)], 2000)
api("POST", f"/api/v1/sales/{s4['saleId']}/void")
print(f"  {s4['saleNumber']}: walk-in cash, 2 kg dog meal = {s4['totalAmount']} - then VOIDED")

step("Customer payment")
api("POST", f"/api/v1/customers/{customer['customerId']}/payments", {"amount": 10000, "notes": "Part payment, cheque 0045"})
print("  Famakeya pays 10,000 - settles the oldest invoice first")

# ---------------------------------------------------------------- expenses
step("Expenses")
today = (datetime.now(timezone.utc) + timedelta(hours=3)).date().isoformat()
for code, text, amount in [("RENT", "October shop rent", 25000), ("UTIL", "Electricity and water", 4200), ("TRN", "Delivery to Kiambu", 1800)]:
    api("POST", "/api/v1/expenses", {"expenseDate": today, "expenseCategoryId": expense_categories[code], "description": text, "amount": amount,
                                      "supplierInvoiceNumber": None, "etimsControlNumber": None, "receiptUrl": None, "notes": "Test data"})
    print(f"  {code:<5} {amount:>8}  {text}")

print(f"\nDone. Everything is dated {today} (Nairobi). Query the reports with from={today}&to={today}.")
