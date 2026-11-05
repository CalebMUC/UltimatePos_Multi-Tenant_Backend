"""What the minimart, chemist and liquor-store practice days share (practice_sales_minimart.py, practice_sales_chemist.py,
practice_sales_liquor.py): signing in, the cash session, ringing up a sale the way a customer pays (with the customer's age checked
where the shop sells alcohol), receiving a delivery, and back-dated expenses. DEMO_MINIMART_AND_CHEMIST.md §3.6, DEMO_LIQUOR_STORE.md §3.6.

The API dates sales today; only expenses take their own date. A day is run once: it stops if the session of the day is already
closed. Same seed, same day: the sales are random but repeatable.
"""
import argparse
import datetime as dt
import random
import string
import sys
from urllib.parse import urlparse

from demo_setup import Api, batch_number, list_products, step

CASH, CREDIT, MPESA_CODE = 0, 1, 4
REFUND_CASH, REFUND_MPESA = 0, 1
SETTLE_CASH, SETTLE_MPESA, SETTLE_BANK, SETTLE_CHEQUE = 0, 1, 2, 3
PAY_IN, PAY_OUT = 0, 1
EXP_CASH, EXP_MPESA, EXP_BANK = 0, 1, 2


class Day:
    def __init__(self, email, password, seed):
        ap = argparse.ArgumentParser()
        ap.add_argument("--api", default="http://localhost:5062/api/v1")
        ap.add_argument("--owner-email", default=email)
        ap.add_argument("--owner-password", default=password)
        ap.add_argument("--allow-remote", action="store_true")
        ap.add_argument("--finish-only", action="store_true", help="Only the expenses and the close: the sales of the day are in")
        self.args = ap.parse_args()
        if urlparse(self.args.api).hostname not in ("localhost", "127.0.0.1") and not self.args.allow_remote:
            sys.exit("Not a local API. Pass --allow-remote only for a demo or staging environment, never production.")

        self.today = dt.date.today()
        self.rng = random.Random(f"{seed}-{self.today.isoformat()}")
        self.api = Api(self.args.api)
        self.api.login(self.args.owner_email, self.args.owner_password)
        self.products = {p["name"]: p for p in list_products(self.api)}
        self.units = {u["symbol"]: u["unitOfMeasureId"] for u in self.api.call("GET", "/units-of-measure/get-units")}
        self.customers = {c["customerName"]: c["customerId"] for c in self.api.call("GET", "/customers")}
        self.suppliers = {s["supplierName"]: s["supplierId"] for s in self.api.call("GET", "/suppliers")}
        self.bundles = {b["code"]: b["productBundleId"] for b in self.api.call("GET", "/product-bundles") or []}
        self.sales = []
        self.session = None
        self._codes = set()
        # [(AgeCheckMethod, weight)]: how cashiers check age at this shop. Set = every sale says how; the API keeps it only on sales of
        # alcohol or tobacco.
        self.age_mix = None

    def line(self, name, qty, unit=None):
        p = self.products[name]
        return {"productId": p["productId"], "unitOfMeasureId": self.units[unit] if unit else p["baseUnitOfMeasureId"], "quantity": qty}

    def mpesa_code(self):
        while True:
            code = "T" + self.rng.choice("ABCDEFGHJK") + "".join(self.rng.choices(string.ascii_uppercase + string.digits, k=8))
            if code not in self._codes:
                self._codes.add(code)
                return code

    def age_check(self):
        methods, weights = zip(*self.age_mix)
        return self.rng.choices(methods, weights)[0]

    def sell(self, lines, customer=None, method=None, due_days=None, notes=None, bundle=None, prescription=None, mpesa_share=0.45,
             age_check=None):
        body = {"customerId": self.customers.get(customer), "lines": lines, "notes": notes, "prescription": prescription,
                "bundles": [{"productBundleId": self.bundles[bundle], "quantity": 1}] if bundle else []}
        if age_check is None and self.age_mix:
            age_check = self.age_check()
        if age_check is not None:
            body["ageCheck"] = age_check
        if method is None:
            method = MPESA_CODE if self.rng.random() < mpesa_share else CASH
        if method == CASH:
            total = self.api.call("POST", "/sales/preview", {**body, "paymentMethod": CASH})["totalAmount"]
            note = next(n for n in (50, 100, 200, 500, 1000) if n >= total / 3) if total < 3000 else 1000
            body["amountTendered"] = -(-total // note) * note        # rounded up to the notes and coins handed over
        elif method == MPESA_CODE:
            body["mpesaReference"] = self.mpesa_code()
        elif method == CREDIT:
            body["paymentDueDate"] = (self.today + dt.timedelta(days=due_days or 30)).isoformat()
        sale = self.api.call("POST", "/sales", {**body, "paymentMethod": method})
        self.sales.append(sale)
        return sale

    def open(self, float_, notes):
        current = self.api.call("GET", "/cash-sessions/current")
        if current and current["status"] == 0:
            self.session = current
            step(f"session {current['sessionNumber']} already open")
            return
        todays = [s for s in self.api.call("GET", "/cash-sessions", params={"pageSize": 5}) or []
                  if s["openedAt"][:10] == self.today.isoformat()]
        if todays:
            sys.exit(f"Today's session {todays[0]['sessionNumber']} is already closed: practice sales were run today.")
        self.session = self.api.call("POST", "/cash-sessions/open", {"openingFloat": float_, "notes": notes})
        step(f"session {self.session['sessionNumber']} opened with KES {float_:,}")

    def pay_out(self, amount, reason):
        self.api.call("POST", f"/cash-sessions/{self.session['cashSessionId']}/movements",
                      {"movementType": PAY_OUT, "amount": amount, "reason": reason})

    def receive(self, supplier, lines, invoice, shelf_life=None):
        """A delivery received at the door: an order raised and received at once (gap G8: no receiving without an order).
        lines: [(product, quantity in its base unit, unit cost)]; batch-tracked products get today's lot, expiring after shelf_life
        days (a product's own entry overrides it)."""
        po = self.api.call("POST", "/purchase-orders", {
            "supplierId": self.suppliers[supplier], "expectedDeliveryDate": None, "notes": "Delivered at the door",
            "lines": [{"productId": self.products[n]["productId"], "unitOfMeasureId": self.products[n]["baseUnitOfMeasureId"],
                       "quantityOrdered": q, "unitCost": c} for n, q, c in lines]})
        received = []
        for l in po["lines"]:
            entry = {"purchaseOrderLineId": l["purchaseOrderLineId"], "quantityReceived": l["quantityOrdered"]}
            if l["isBatchTracked"]:
                days = (shelf_life or {}).get(l["productName"], 14)
                entry |= {"batchNumber": batch_number(l["productName"], self.today.year, self.today.isoformat()),
                          "expiryDate": (self.today + dt.timedelta(days=days)).isoformat()}
            received.append(entry)
        self.api.call("POST", f"/purchase-orders/{po['purchaseOrderId']}/receive",
                      {"lines": received, "notes": "Delivered at the door", "supplierInvoiceNumber": invoice})
        return po, sum(q * c for _, q, c in lines)

    def expenses(self, entries_for):
        """entries_for(month start, is this month) -> [(date, code, description, amount, method, payee)]. An expense already
        recorded on its date is skipped, so a later day only adds what has fallen due since. Run it after close(): a cash expense
        recorded while the drawer is open is taken as paid out of it, which is right for today's but not for last month's."""
        done = {(e["expenseDate"], e["description"]) for e in self.api.call("GET", "/expenses", params={"pageSize": 200}) or []
                if e["status"] != "Voided"}
        categories = {c["code"]: c["expenseCategoryId"] for c in self.api.call("GET", "/expense-categories")}
        first = self.today.replace(day=1)
        previous = lambda month: (month - dt.timedelta(days=1)).replace(day=1)
        count = 0
        for m in (previous(previous(first)), previous(first), first):
            for date, code, description, amount, method, payee in entries_for(m, m == first):
                if date > self.today or (date.isoformat(), description) in done:
                    continue
                reference = None if method == EXP_CASH else (f"FT{date:%y%m%d}{count:03d}" if method == EXP_BANK else self.mpesa_code())
                self.api.call("POST", "/expenses", {
                    "expenseDate": date.isoformat(), "expenseCategoryId": categories[code], "description": description, "amount": amount,
                    "paymentMethod": method, "paymentReference": reference, "supplierId": None, "payeeName": payee,
                    "supplierInvoiceNumber": None, "etimsControlNumber": None, "receiptUrl": None, "notes": None})
                count += 1
        step(f"{count} expenses over three months")

    def close(self, difference, notes):
        """Counts the drawer `difference` away from what the system expects (negative: short), so the variance shows."""
        session = self.api.call("GET", f"/cash-sessions/{self.session['cashSessionId']}")
        expected = session["breakdown"]["expectedCash"]
        self.api.call("POST", f"/cash-sessions/{self.session['cashSessionId']}/close", {"countedCash": expected + difference, "notes": notes})
        step(f"session closed: expected KES {expected:,.0f}, counted {expected + difference:,.0f}")

    def summary(self, voided=()):
        sales = [s for s in self.sales if s["saleId"] not in voided]
        if not sales:
            return
        by_method = {}
        for s in sales:
            by_method[s["paymentMethod"]] = by_method.get(s["paymentMethod"], 0) + s["totalAmount"]
        names = {CASH: "cash", CREDIT: "credit", MPESA_CODE: "M-Pesa"}
        print(f"\n{len(sales)} sales kept (KES {sum(s['totalAmount'] for s in sales):,.0f}): "
              + ", ".join(f"{names[k]} {v:,.0f}" for k, v in by_method.items()))


def pick(rng, menu, low, high):
    """low..high different items from menu [(product, (min qty, max qty))], with a quantity each."""
    return [(n, rng.randint(*q) if isinstance(q[0], int) else rng.choice(q)) for n, q in rng.sample(menu, rng.randint(low, high))]
