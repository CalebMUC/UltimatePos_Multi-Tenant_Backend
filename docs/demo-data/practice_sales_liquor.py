"""A day of trading at the liquor-store demo (DEMO_LIQUOR_STORE.md §3.6), through the API as the owner:

- the brewer's distributor lorry received in the morning (crates exchanged for empties, so no deposit changes hands) and paid by bank
  transfer; the ice van paid in cash from the till;
- an afternoon of quarters, sodas and cigarettes; a bar topping up crates at the wholesale price on its account;
- the evening rush: single beers (with the bottle deposit when the customer brings no empty), cans, quarters with mixers and ice, the
  three party packs, a walk-in buying a crate with its deposit; most paid by M-Pesa;
- every sale of alcohol or tobacco with the customer's age check recorded (ID, or a regular whose ID was seen before), and one sale
  refused because the cashier did not record it;
- an events company's order delivered on account, a night delivery to the apartments delivered with the rider's ID check, and one
  left pending for the demo;
- empties brought back and their deposits refunded from the drawer; a broken bottle and a quarter missing at the count written off; a
  sale voided; cash dropped to the safe;
- the day closed KES 200 short, then two months and this month of expenses.

It must run within the shop's selling hours for alcohol (10:00-23:00 in the demo): outside them the till refuses beer, as it should.

    python docs/demo-data/practice_sales_liquor.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
import datetime as dt
import sys

from demo_practice import CASH, CREDIT, EXP_BANK, EXP_CASH, EXP_MPESA, MPESA_CODE, SETTLE_BANK, SETTLE_CASH, SETTLE_MPESA, Day, pick
from demo_setup import DRIVING_LICENCE, KNOWN_CUSTOMER, NATIONAL_ID, OTHER_ID, PASSPORT, read_build_sheet, step

BEERS = [("Tusker Lager 500ml Bottle", (1, 4)), ("Tusker Malt Lager 500ml Bottle", (1, 2)), ("Pilsner Lager 500ml Bottle", (1, 3)),
         ("Balozi Lager 500ml Bottle", (1, 3)), ("White Cap Lager 500ml Bottle", (1, 2)), ("Guinness Foreign Extra Stout 500ml Bottle", (1, 3))]
CANS = [("Tusker Lager 500ml Can", (1, 6)), ("Tusker Lite 500ml Can", (1, 4)), ("Guinness Smooth 500ml Can", (1, 4)),
        ("Tusker Cider 500ml Can", (1, 3)), ("Smirnoff Ice Guarana 300ml Bottle", (1, 4)), ("Heineken 330ml Bottle", (1, 3))]
QUARTERS = [("Kenya Cane 250ml", (1, 2)), ("Chrome Vodka 250ml", (1, 2)), ("Chrome Gin 250ml", (1, 2)), ("Gilbey's Gin 250ml", (1, 1)),
            ("Hunter's Choice Whisky 250ml", (1, 1)), ("Viceroy Brandy 250ml", (1, 1)), ("Richot Brandy 250ml", (1, 1)),
            ("Black & White Whisky 250ml", (1, 1)), ("Smirnoff Red Vodka 250ml", (1, 1))]
BOTTLES = [("Kenya Cane 750ml", (1, 1)), ("Gilbey's Gin 750ml", (1, 1)), ("Johnnie Walker Red Label 750ml", (1, 1)),
           ("Captain Morgan Spiced Gold 750ml", (1, 1)), ("Viceroy Brandy 750ml", (1, 1)), ("Jameson Irish Whiskey 750ml", (1, 1)),
           ("Four Cousins Sweet Red 750ml", (1, 2)), ("4th Street Sweet Red 750ml", (1, 1)), ("Four Cousins Sweet Rose 750ml", (1, 1)),
           ("Caprice Dry Red 750ml", (1, 1)), ("Amarula Cream 750ml", (1, 1)), ("Black & White Whisky 750ml", (1, 1)),
           ("Hunter's Choice Whisky 750ml", (1, 1)), ("Chrome Vodka 750ml", (1, 1))]
MIXERS = [("Coca-Cola 500ml", (1, 2)), ("Coca-Cola 1.25L", (1, 1)), ("Sprite 500ml", (1, 2)), ("Schweppes Tonic Water 500ml", (1, 2)),
          ("Schweppes Soda Water 500ml", (1, 1)), ("Stoney Tangawizi 500ml", (1, 2)), ("Dasani Water 500ml", (1, 2)),
          ("Ice Cubes 2kg Bag", (1, 1)), ("Roasted Groundnuts 100g", (1, 2)), ("Tropical Heat Potato Crisps 50g", (1, 2)),
          ("Red Bull 250ml", (1, 1)), ("Carrier Bag Small (non-woven)", (1, 1))]
SMOKES = [("Sportsman Cigarettes (20)", (1, 1)), ("Embassy Kingsize Cigarettes (20)", (1, 1)), ("Dunhill Switch Cigarettes (20)", (1, 1))]

BOTTLE_DEPOSIT = "Empty Beer Bottle 500ml (deposit)"
CRATE_DEPOSIT = "Crate & 25 Empties (deposit)"
KBL = "Thika Road Beverages Ltd"
ICE_VAN = "Githurai Snacks & Ice Supplies"
KONA = "Kona Bar & Grill (Zimmerman)"
EVENTS = "Wanjiru Events & Catering"
SUNSET = "Sunset Gardens Apartments (deliveries)"
SACCO = "Thika Road Sacco (staff events)"


def main():
    day = Day("demo.liquor@ultimatepos.local", "BaridiDemo@2026", "baridi")
    # Most regulars show an ID; a known face is recorded as such, so the owner sees how often each cashier relies on it.
    day.age_mix = [(NATIONAL_ID, 50), (KNOWN_CUSTOMER, 35), (DRIVING_LICENCE, 10), (PASSPORT, 3), (OTHER_ID, 2)]
    if not day.args.finish_only:
        check_hours(day)
    day.open(5000, "Float from the safe: coins, 50s and 100s for change")
    voided = set()
    if not day.args.finish_only:
        voided = trade(day)

    first = day.today.replace(day=1)
    oldest = ((first - dt.timedelta(days=1)).replace(day=1) - dt.timedelta(days=1)).replace(day=1)

    def month(m, current):
        d = lambda n: m.replace(day=min(n, day.today.day) if current else n)
        entries = [(m, "RENT", "Shop rent, Kamiti Road", 60000, EXP_BANK, "Zimmerman Plaza Ltd"),
                   (d(3), "UTIL", "Electricity tokens: fridges and coolers", 9000, EXP_MPESA, "Kenya Power"),
                   (d(3), "UTIL", "Water", 800, EXP_MPESA, "Nairobi Water")]
        if not current:
            entries += [(m.replace(day=28), "SAL", "Salaries: manager, 2 cashiers, storekeeper", 82000, EXP_BANK, None),
                        (m.replace(day=28), "SEC", "Night guard", 15000, EXP_BANK, "Simba Guards Ltd"),
                        (m.replace(day=28), "FIN", "M-Pesa till and bank charges", 3200, EXP_MPESA, None),
                        (m.replace(day=12), "TRN", "Boda riders: night deliveries", 6000, EXP_MPESA, None),
                        (m.replace(day=10), "PKG", "Carrier bags and bottle sleeves", 1500, EXP_CASH, None)]
        if m == oldest:
            entries += [(m.replace(day=4), "LIC", "Liquor licence (wines and spirits), Nairobi County, annual", 30000, EXP_MPESA,
                         "Nairobi City County"),
                        (m.replace(day=4), "LIC", "Unified business permit", 15000, EXP_MPESA, "Nairobi City County"),
                        (m.replace(day=18), "REF", "Cooler service and gas", 3500, EXP_CASH, "Zimmerman Fridge Repairs")]
        return entries
    day.close(-200, "200 short")
    day.expenses(month)
    day.summary(voided)


def check_hours(day):
    """The till refuses alcohol outside the licence's hours: so would this script, halfway through. Say so before starting."""
    beer = day.line("Tusker Lager 500ml Bottle", 1)
    preview = day.api.call("POST", "/sales/preview", {"customerId": None, "lines": [beer], "paymentMethod": CASH})
    if not preview["withinSellingHours"]:
        sys.exit(f"Alcohol may be sold {preview['sellingHours']} (Settings: age-restricted sales). Run this within those hours.")


def try_sell(day, lines, *args, **kw):
    """A sale that is skipped if what it picked has run out: the shop never sells more than it holds."""
    try:
        return day.sell(lines, *args, **kw)
    except SystemExit as e:
        if "INSUFFICIENT_STOCK" in str(e):
            return None
        raise


def trade(day):
    rng, line = day.rng, day.line
    costs = {r["name"]: r["cost"] for r in read_build_sheet("liquor-products.xlsx")}

    # 1. The lorry: full crates exchanged for the empties on hand (no deposit either way), paid by bank transfer before it leaves
    restock = [("Tusker Lager 500ml Bottle", 100), ("Tusker Lager 500ml Can", 48), ("Guinness Foreign Extra Stout 500ml Bottle", 50),
               ("Balozi Lager 500ml Bottle", 50), ("Pilsner Lager 500ml Bottle", 25), ("White Cap Lager 500ml Bottle", 25),
               ("Kenya Cane 250ml", 48), ("Chrome Vodka 250ml", 48), ("Chrome Gin 250ml", 48), ("Smirnoff Ice Guarana 300ml Bottle", 24),
               ("Johnnie Walker Red Label 750ml", 12), ("Kenya Cane 750ml", 12), ("Gilbey's Gin 750ml", 12)]
    po, value = day.receive(KBL, [(n, q, costs[n]) for n, q in restock], f"KBL-{day.today:%m%d}",
                            {"Smirnoff Ice Guarana 300ml Bottle": 270})
    day.api.call("POST", f"/suppliers/{day.suppliers[KBL]}/payments", {
        "amount": round(value, 2), "paymentMethod": SETTLE_BANK, "paymentReference": f"RTGS-{day.today:%m%d}", "notes": po["orderNumber"]})
    ice = [("Ice Cubes 2kg Bag", 20), ("Ice Cubes 5kg Bag", 8)]
    po_ice, ice_value = day.receive(ICE_VAN, [(n, q, costs[n]) for n, q in ice], f"ICE-{day.today:%m%d}")
    day.api.call("POST", f"/suppliers/{day.suppliers[ICE_VAN]}/payments", {
        "amount": round(ice_value, 2), "paymentMethod": SETTLE_CASH, "paymentReference": None, "notes": f"Cash to the ice van, {po_ice['orderNumber']}"})
    step(f"lorry {po['orderNumber']} KES {value:,.0f} by bank transfer; ice KES {ice_value:,.0f} cash from the till")

    # 2. The age check is not optional: a sale of beer without it is refused
    try:
        day.api.call("POST", "/sales", {"customerId": None, "lines": [line("Tusker Lager 500ml Bottle", 2)], "paymentMethod": MPESA_CODE,
                                        "mpesaReference": day.mpesa_code(), "bundles": []})
        sys.exit("A beer sale went through without an age check: the API should have refused it.")
    except SystemExit as e:
        if "AGE_CHECK_REQUIRED" not in str(e):
            raise
    step("a beer sale without the customer's age check was refused (AGE_CHECK_REQUIRED)")

    # 3. The afternoon: quarters, sodas, cigarettes; the bar tops up on its account (it brings its own crates of empties)
    for _ in range(12):
        try_sell(day, [line(n, q) for n, q in pick(rng, QUARTERS + MIXERS + SMOKES, 1, 3)], mpesa_share=0.6)
    owed = day.api.call("GET", f"/customers/{day.customers[KONA]}/balance")["outstandingBalance"]
    if owed > 0:
        day.api.call("POST", f"/customers/{day.customers[KONA]}/payments", {
            "amount": owed, "notes": "Last top-up", "paymentMethod": SETTLE_MPESA, "paymentReference": day.mpesa_code()})
    try_sell(day, [line("Tusker Lager 500ml Bottle", 2, "crate"), line("Guinness Foreign Extra Stout 500ml Bottle", 1, "crate"),
                   line("Kenya Cane 250ml", 1, "ctn")], KONA, CREDIT, 7, "Bar top-up: their distributor is late. Brought 3 crates of empties",
             age_check=KNOWN_CUSTOMER)
    step("afternoon: 12 sales; Kona Bar paid the last top-up and took 3 crates and a carton of quarters at the wholesale price")

    # 4. The events order, delivered on account: crates with their deposits (the caterer has no empties), a case of wine, whisky, ice
    order = day.api.call("POST", "/delivery-orders", {
        "customerId": day.customers[EVENTS], "deliveryAddress": "Kahawa West, St. Mary's grounds (wedding)", "contactPhone": "0700000602",
        "notes": "Wedding on Saturday. LPO WE-114", "expectedPaymentMethod": CREDIT, "riderName": "Pickup: Dennis",
        "lines": [line("Tusker Lager 500ml Bottle", 3, "crate"), line(CRATE_DEPOSIT, 3), line("Four Cousins Sweet Red 750ml", 1, "case"),
                  line("Johnnie Walker Red Label 750ml", 4), line("Ice Cubes 5kg Bag", 4), line("Coca-Cola 500ml", 2, "ctn")]})
    day.api.call("POST", f"/delivery-orders/{order['deliveryOrderId']}/deliver", {
        "paymentMethod": CREDIT, "paymentDueDate": (day.today + dt.timedelta(days=14)).isoformat(), "ageCheck": NATIONAL_ID,
        "notes": "Received by the caterer, ID seen"})
    step(f"{order['orderNumber']}: the wedding order delivered on account (14 days), crate deposits charged")

    # 5. The evening rush
    for _ in range(26):
        picked = pick(rng, BEERS, 1, 2)
        lines = [line(n, q) for n, q in picked]
        if rng.random() < 0.3:       # no empties brought: the bottle deposit
            lines.append(line(BOTTLE_DEPOSIT, sum(q for _, q in picked)))
        if rng.random() < 0.4:
            lines += [line(n, q) for n, q in pick(rng, MIXERS, 1, 1)]
        try_sell(day, lines, mpesa_share=0.6)
    for _ in range(14):
        try_sell(day, [line(n, q) for n, q in pick(rng, CANS, 1, 2) + pick(rng, MIXERS, 0, 1)], mpesa_share=0.6)
    for _ in range(14):
        try_sell(day, [line(n, q) for n, q in pick(rng, QUARTERS, 1, 1) + pick(rng, MIXERS, 1, 2)], mpesa_share=0.55)
    for _ in range(8):
        try_sell(day, [line(n, q) for n, q in pick(rng, BOTTLES, 1, 1) + pick(rng, MIXERS, 0, 2)], mpesa_share=0.7)
    for code in ("SUNDOWNER", "WHISKY", "MATCHDAY"):
        try_sell(day, [], bundle=code, mpesa_share=0.8)
    try_sell(day, [line("Tusker Lager 500ml Bottle", 1, "crate"), line(CRATE_DEPOSIT, 1)], method=MPESA_CODE,
             notes="House party: no crate of empties, deposit paid")
    try_sell(day, [line("Gilbey's Gin 750ml", 2), line("Schweppes Tonic Water 500ml", 6)], SACCO, CREDIT, 30, "Staff end-month, LPO 0457")
    step("evening rush: about 65 sales, the party packs, a crate with its deposit, the Sacco on account")

    # 6. Night deliveries: one delivered with the rider's ID check, one waiting for a rider in the demo
    night = day.api.call("POST", "/delivery-orders", {
        "customerId": day.customers[SUNSET], "deliveryAddress": "Sunset Gardens, Block A, House 12", "contactPhone": "0700000604",
        "notes": "WhatsApp order. Rider to see ID", "expectedPaymentMethod": MPESA_CODE, "riderName": "Kevin (boda)",
        "lines": [line("Tusker Lager 500ml Can", 6), line("Chrome Gin 250ml", 1), line("Schweppes Tonic Water 500ml", 2), line("Ice Cubes 2kg Bag", 1)]})
    day.api.call("POST", f"/delivery-orders/{night['deliveryOrderId']}/deliver", {
        "paymentMethod": MPESA_CODE, "mpesaReference": day.mpesa_code(), "ageCheck": NATIONAL_ID, "notes": "ID seen at the door"})
    pending = [o for o in day.api.call("GET", "/delivery-orders", params={"pageSize": 50}) or [] if o.get("status") == 0]
    if not pending:
        day.api.call("POST", "/delivery-orders", {
            "customerId": day.customers[SUNSET], "deliveryAddress": "Sunset Gardens, Block C, House 3", "contactPhone": "0700000604",
            "notes": "WhatsApp order. Call on arrival; see ID", "expectedPaymentMethod": MPESA_CODE,
            "lines": [line("Guinness Smooth 500ml Can", 4), line("Johnnie Walker Red Label 750ml", 1), line("Coca-Cola 1.25L", 2)]})
    step(f"{night['orderNumber']} delivered by Kevin with the ID checked at the door; another order waiting for a rider")

    # 7. Empties back for their deposits, paid from the drawer; breakage and a quarter short at the count; a mistake voided
    deposit = {r["name"]: r["retail"] for r in read_build_sheet("liquor-products.xlsx") if r["name"] in (BOTTLE_DEPOSIT, CRATE_DEPOSIT)}
    for name, qty, who in ((BOTTLE_DEPOSIT, 6, "6 empties"), (CRATE_DEPOSIT, 1, "a crate of empties")):
        day.pay_out(deposit[name] * qty, f"Deposit refunded: {who} returned")
        p = day.products[name]
        day.api.call("POST", "/stock/adjustments", {"productId": p["productId"], "unitOfMeasureId": p["baseUnitOfMeasureId"],
                                                    "quantityChange": qty, "reason": f"Returned by a customer, deposit refunded ({who})",
                                                    "unitCost": costs[name]})
    for name, qty, reason in (("Kenya Cane 750ml", -1, "Broken: dropped while filling the shelf"),
                              ("Chrome Vodka 250ml", -1, "Short at the evening count")):
        p = day.products[name]
        day.api.call("POST", "/stock/adjustments", {"productId": p["productId"], "unitOfMeasureId": p["baseUnitOfMeasureId"],
                                                    "quantityChange": qty, "reason": reason})
    wrong = day.sell([line("Hennessy VS Cognac 700ml", 1)], method=CASH)
    day.api.call("POST", f"/sales/{wrong['saleId']}/void")
    day.pay_out(15000, "Cash dropped to the safe at 8 pm")
    step(f"deposits refunded for returned empties; a broken bottle and a missing quarter written off; {wrong['saleNumber']} voided; "
         "15,000 to the safe")
    return {wrong["saleId"]}


if __name__ == "__main__":
    main()
