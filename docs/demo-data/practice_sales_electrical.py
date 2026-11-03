"""A day of practice trading for the electrical demo (DEMO_HARDWARE_AND_ELECTRICAL.md §3.11 A), through the API as the owner:

- a cash session with a float, a pay-out for fuel, closed at the end with a small shortage;
- about 45 sales: walk-ins (cash with change, or an M-Pesa code), the electrician with his discount, full rolls and boxes,
  contractors and the property manager on credit with a promised date and an LPO number, the solar kit and the wiring kit;
- a faulty item returned, a sale voided, a contractor paying part of his account by cheque;
- a delivery to a contractor's site, left pending for the demo;
- two months and this month of expenses (rent, salaries, power, internet, fuel, M-Pesa charges), dated when they were paid.

The API dates sales today; only expenses take their own date. Run it once per practice day: it stops if the session of the day is
already closed. Same seed, same day: the sales are random but repeatable.

    python docs/demo-data/practice_sales_electrical.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
import argparse
import datetime as dt
import random
import string
import sys
from urllib.parse import urlparse

from demo_setup import Api, step

CASH, CREDIT, MPESA_CODE = 0, 1, 4
REFUND_CASH = 0
SETTLE_CHEQUE = 3
PAY_IN, PAY_OUT = 0, 1
EXP_CASH, EXP_MPESA, EXP_BANK = 0, 1, 2

# What walk-ins buy, with how many (base unit; cable in metres)
COUNTER = [
    ("Tronic 1-Gang 1-Way Switch", (1, 4)), ("Tronic 2-Gang 1-Way Switch", (1, 3)), ("Tronic 13A Twin Switched Socket", (1, 3)),
    ("Tronic 13A Single Switched Socket", (1, 2)), ("MK 13A Twin Switched Socket", (1, 2)), ("MK 1-Gang 1-Way Switch", (1, 2)),
    ("Tronic LED Bulb 9W B22", (1, 6)), ("Tronic LED Bulb 12W B22", (1, 4)), ("Philips LED Bulb 9W B22 Daylight", (1, 3)),
    ("LED Tube 4ft 18W with Fitting", (1, 2)), ("Ceiling Rose", (1, 3)), ("Batten Lamp Holder", (1, 3)),
    ("PVC Insulation Tape", (1, 3)), ("Cable Ties 200mm (pack of 100)", (1, 1)), ("Junction Box 20mm 3-Way", (2, 6)),
    ("PVC Conduit 20mm x 2.9m", (2, 10)), ("Conduit Bend 20mm", (2, 8)), ("Chint MCB 1P 16A", (1, 3)), ("Hager MCB 1P 16A", (1, 2)),
    ("Tronic 4-Way Extension 3m", (1, 1)), ("3-Way Multi Plug Adaptor", (1, 2)), ("Fridge Guard Voltage Protector", (1, 1)),
    ("TV Guard Voltage Protector", (1, 1)), ("Tester Screwdriver", (1, 1)), ("Motion Sensor Security Light", (1, 1)),
    ("LED Floodlight 50W", (1, 1)), ("Electric Kettle 1.7L Stainless", (1, 1)), ("Dry Iron Box", (1, 1)),
]
CUT_CABLE = [("EAC Single Core 1.5mm Red", (5, 30)), ("EAC Single Core 2.5mm Red", (5, 30)), ("EAC Single Core 2.5mm Black", (5, 30)),
             ("Kenwest Single Core 2.5mm Red", (5, 25)), ("Flex Cable 3-Core 1.5mm", (3, 15)), ("Kenwest Twin & Earth 2.5mm", (5, 20))]

OMONDI = "Omondi the electrician (fundi wa stima)"
KARIUKI = "Kariuki Electrical Contractors"
BRIGHTLINE = "Brightline Solar Installers"
GREENVIEW = "Greenview Apartments Management"
NJERI = "Njeri - new house, Membley"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--api", default="http://localhost:5062/api/v1")
    ap.add_argument("--owner-email", default="demo.electrical@ultimatepos.local")
    ap.add_argument("--owner-password", default="MwangazaDemo@2026")
    ap.add_argument("--allow-remote", action="store_true")
    ap.add_argument("--finish-only", action="store_true", help="Only the expenses and the close: the sales of the day are in")
    args = ap.parse_args()
    if urlparse(args.api).hostname not in ("localhost", "127.0.0.1") and not args.allow_remote:
        sys.exit("Not a local API. Pass --allow-remote only for a demo or staging environment, never production.")

    today = dt.date.today()
    rng = random.Random(f"mwangaza-{today.isoformat()}")
    api = Api(args.api)
    api.login(args.owner_email, args.owner_password)

    products = {p["name"]: p for p in api.call("GET", "/products/get-products", params={"pageSize": 200, "isActive": "true"})}
    units = {u["symbol"]: u["unitOfMeasureId"] for u in api.call("GET", "/units-of-measure/get-units")}
    customers = {c["customerName"]: c["customerId"] for c in api.call("GET", "/customers")}
    bundles = {b["code"]: b["productBundleId"] for b in api.call("GET", "/product-bundles")}

    def line(name, qty, unit=None):
        p = products[name]
        return {"productId": p["productId"], "unitOfMeasureId": units[unit] if unit else p["baseUnitOfMeasureId"], "quantity": qty}

    used_codes = set()

    def mpesa_code():
        while True:
            code = "T" + rng.choice("ABCDEFGHJK") + "".join(rng.choices(string.ascii_uppercase + string.digits, k=8))
            if code not in used_codes:
                used_codes.add(code)
                return code

    def sell(lines, customer=None, method=None, due_days=None, notes=None, bundle=None, override=False):
        body = {"customerId": customers.get(customer), "lines": lines, "notes": notes,
                "bundles": [{"productBundleId": bundles[bundle], "quantity": 1}] if bundle else []}
        if method is None:
            method = MPESA_CODE if rng.random() < 0.4 else CASH
        if method == CASH:
            total = api.call("POST", "/sales/preview", {**body, "paymentMethod": CASH})["totalAmount"]
            note = next(n for n in (50, 100, 200, 500, 1000, 2000, 5000) if n >= total / 3) if total < 15000 else 1000
            body["amountTendered"] = -(-total // note) * note        # rounded up to the notes a customer would hand over
        elif method == MPESA_CODE:
            body["mpesaReference"] = mpesa_code()
        elif method == CREDIT:
            body["paymentDueDate"] = (today + dt.timedelta(days=due_days or 30)).isoformat()
            body["overrideCreditLimit"] = override
        return api.call("POST", "/sales", {**body, "paymentMethod": method})

    # 1. Open the day
    current = api.call("GET", "/cash-sessions/current")
    if current and current["status"] == 0:
        session = current
        step(f"session {session['sessionNumber']} already open")
    else:
        todays = [s for s in api.call("GET", "/cash-sessions", params={"pageSize": 5}) or []
                  if s["openedAt"][:10] == today.isoformat()]
        if todays:
            sys.exit(f"Today's session {todays[0]['sessionNumber']} is already closed: practice sales were run today.")
        session = api.call("POST", "/cash-sessions/open", {"openingFloat": 5000, "notes": "Float from the safe"})
        step(f"session {session['sessionNumber']} opened with KES 5,000")

    sales = []
    if not args.finish_only:
        sales = trade(api, rng, today, session, products, units, customers, line, sell)

    # 8. Expenses, dated when they were paid
    categories = {c["code"]: c["expenseCategoryId"] for c in api.call("GET", "/expense-categories")}
    existing = api.call("GET", "/expenses", params={"pageSize": 200}) or []
    if not existing:
        first = today.replace(day=1)
        previous = lambda month: (month - dt.timedelta(days=1)).replace(day=1)
        months = [previous(previous(first)), previous(first), first]
        count = 0
        for m in months:
            entries = [(m, "RENT", "Shop rent, Kamakis Road", 45000, EXP_BANK, "Kamakis Plaza Ltd"),
                       (m.replace(day=min(5, today.day) if m == first else 5), "UTIL", "Electricity tokens", 6500, EXP_MPESA, "Kenya Power"),
                       (m.replace(day=min(5, today.day) if m == first else 5), "UTIL", "Internet", 3000, EXP_MPESA, "Safaricom Home")]
            if m != first:
                entries += [(m.replace(day=28), "SAL", "Salaries: 2 shop attendants and the technician", 66000, EXP_BANK, None),
                            (m.replace(day=28), "FIN", "M-Pesa and bank charges", 2300, EXP_MPESA, None),
                            (m.replace(day=15), "SEC", "Night guard", 8000, EXP_CASH, "Usalama Guards"),
                            *[(m.replace(day=d), "TRN", "Fuel for deliveries", 3000, EXP_CASH, None) for d in (7, 14, 21)]]
            for date, code, description, amount, method, payee in entries:
                if date > today:
                    continue
                # Bank and M-Pesa payments need their reference.
                reference = None if method == EXP_CASH else (f"FT{date:%y%m%d}{count:03d}" if method == EXP_BANK else mpesa_code())
                api.call("POST", "/expenses", {
                    "expenseDate": date.isoformat(), "expenseCategoryId": categories[code], "description": description, "amount": amount,
                    "paymentMethod": method, "paymentReference": reference, "supplierId": None, "payeeName": payee,
                    "supplierInvoiceNumber": None, "etimsControlNumber": None, "receiptUrl": None, "notes": None})
                count += 1
        step(f"{count} expenses over three months")

    # 9. Close the day 200 short, so the variance shows
    session = api.call("GET", f"/cash-sessions/{session['cashSessionId']}")
    expected = session["breakdown"]["expectedCash"]
    api.call("POST", f"/cash-sessions/{session['cashSessionId']}/close", {"countedCash": expected - 200, "notes": "200 short"})
    step(f"session closed: expected KES {expected:,.0f}, counted {expected - 200:,.0f}")

    if sales:
        total = sum(s["totalAmount"] for s in sales)
        by_method = {}
        for s in sales:
            by_method[s["paymentMethod"]] = by_method.get(s["paymentMethod"], 0) + s["totalAmount"]
        names = {CASH: "cash", CREDIT: "credit", MPESA_CODE: "M-Pesa"}
        print(f"\n{len(sales)} sales kept, plus one returned and one voided (KES {total:,.0f}): "
              + ", ".join(f"{names[k]} {v:,.0f}" for k, v in by_method.items()))


def trade(api, rng, today, session, products, units, customers, line, sell):
    """Steps 2-7: the sales of the day, the return and void, the payment, the pay-out and the delivery."""
    sales = []

    # 2. Morning: electricians before site, walk-ins
    for _ in range(3):
        picks = rng.sample(CUT_CABLE, 2) + rng.sample(COUNTER[:16], rng.randint(2, 4))
        sales.append(sell([line(n, rng.randint(*q)) for n, q in picks], OMONDI))
    step("Omondi: 3 sales with his 5%")

    for _ in range(14):
        picks = rng.sample(COUNTER, rng.randint(1, 4))
        if rng.random() < 0.35:
            picks.append(rng.choice(CUT_CABLE))
        sales.append(sell([line(n, rng.randint(*q)) for n, q in picks]))
    step("14 walk-in sales")

    # 3. Contractors and institutions on account
    sales.append(sell([line("EAC Single Core 2.5mm Red", 3, "roll"), line("EAC Single Core 2.5mm Black", 3, "roll"),
                       line("EAC Single Core 2.5mm Green/Yellow Earth", 2, "roll"), line("Hager Consumer Unit 8-Way", 2),
                       line("Hager MCB 1P 16A", 10), line("Hager MCB 1P 20A", 6), line("Hager RCCB 2P 40A 30mA", 6),
                       line("PVC Conduit 20mm x 2.9m", 120), line("MK 13A Twin Switched Socket", 24), line("MK 1-Gang 1-Way Switch", 18)],
                      KARIUKI, CREDIT, 30, "LPO KEC-0912 - Kimbo apartments, block B"))
    step("Kariuki: wiring for a block of flats on account")

    sales.append(sell([line("Lithium Battery 48V 100Ah LiFePO4", 2), line("Longi 550W Mono Solar Panel", 4),
                       line("Solar PV Cable 4mm", 1, "roll"), line("MC4 Connector Pair", 6)],
                      BRIGHTLINE, CREDIT, 14, "Job: Juja farmhouse 5 kW", bundle="SOLAR-1KW", override=True))
    step("Brightline: solar kit and a 48 V system on account (credit limit overridden by the owner)")

    sales.append(sell([line("Tronic LED Bulb 9W B22", 3, "box"), line("Chint MCB 1P 16A", 8), line("LED Floodlight 100W", 4),
                       line("Recessed LED Panel 18W Round", 10)],
                      GREENVIEW, CREDIT, 30, "LPO GV-0418 - common areas"))
    step("Greenview Apartments: bulbs, breakers and floodlights on LPO")

    # 4. Full packs and the wiring kit
    sales.append(sell([line("Kenwest Single Core 2.5mm Red", 1, "roll"), line("Kenwest Single Core 2.5mm Black", 1, "roll")], OMONDI))
    sales.append(sell([line("Cat6 Network Cable", 1, "box"), line("Lever Connector 3-Way", 1, "box")]))
    sales.append(sell([], NJERI, MPESA_CODE, bundle="WIRE-1RM"))
    sales.append(sell([line("Instant Shower Heater 5.5kW", 1), line("EAC Single Core 4mm Red", 15), line("EAC Single Core 4mm Black", 15),
                       line("MK 20A DP Switch with Neon", 1)], NJERI, MPESA_CODE))
    step("full rolls, a Cat6 box and the single-room wiring kit")

    # 5. Afternoon walk-ins
    for _ in range(18):
        picks = rng.sample(COUNTER, rng.randint(1, 3))
        if rng.random() < 0.3:
            picks.append(rng.choice(CUT_CABLE))
        sales.append(sell([line(n, rng.randint(*q)) for n, q in picks]))
    step("18 more walk-in sales")

    # 6. A faulty item comes back, a mistake is voided, a contractor pays, fuel for the delivery
    faulty = sell([line("Fridge Guard Voltage Protector", 1), line("PVC Insulation Tape", 2)], method=CASH)
    guard = next(l for l in faulty["lines"] if l["productName"] == "Fridge Guard Voltage Protector")
    api.call("POST", f"/sales/{faulty['saleId']}/returns", {
        "reason": "Faulty - does not switch on; back to supplier under warranty", "refundMethod": REFUND_CASH, "refundReference": None,
        "lines": [{"saleLineId": guard["saleLineId"], "quantity": 1, "restock": False}]})
    step(f"{faulty['saleNumber']}: faulty fridge guard returned, cash refunded")

    wrong = sell([line("Hager MCB 1P 32A", 2)], method=CASH)
    api.call("POST", f"/sales/{wrong['saleId']}/void")
    step(f"{wrong['saleNumber']}: voided (wrong breaker rung up)")

    api.call("POST", f"/customers/{customers[KARIUKI]}/payments", {
        "amount": 60000, "notes": "Part payment, Kimbo apartments", "paymentMethod": SETTLE_CHEQUE, "paymentReference": "CHQ 004512"})
    step("Kariuki paid KES 60,000 by cheque")

    api.call("POST", f"/cash-sessions/{session['cashSessionId']}/movements",
             {"movementType": PAY_OUT, "amount": 1500, "reason": "Fuel for the pickup - Kimbo delivery"})
    step("pay-out: KES 1,500 fuel")

    # 7. A delivery to site, left for the demo to dispatch
    pending = [o for o in api.call("GET", "/delivery-orders", params={"pageSize": 50}) or [] if o.get("status") == 0]
    if not pending:
        api.call("POST", "/delivery-orders", {
            "customerId": customers[KARIUKI], "deliveryAddress": "Kimbo apartments, block B, off Kamiti Road, Ruiru",
            "contactPhone": "0700000201", "notes": "Site foreman: Peter", "expectedPaymentMethod": CREDIT,
            "lines": [line("Armoured Cable 6mm 3-Core", 40), line("Manual Changeover Switch 100A 4P", 1), line("Copper Earth Rod 1.2m", 2),
                      line("Bare Copper Earth Wire 16mm", 15)]})
        step("delivery order for Kariuki's site, pending dispatch")
    return sales


if __name__ == "__main__":
    main()
