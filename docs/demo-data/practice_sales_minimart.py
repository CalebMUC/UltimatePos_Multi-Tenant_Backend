"""A day of trading at the minimart demo (DEMO_MINIMART_AND_CHEMIST.md §3.6), through the API as the owner:

- the bread van paid in cash from the till, the dairy van paid by M-Pesa, yoghurt and mala received with their expiry dates;
- about 75 sales across the day: the breakfast rush (bread, milk, eggs, mandazi), midday shoppers, the evening supper rush (unga,
  sukuma, tomatoes by the half kilo, loose sugar, charcoal, paraffin), the breakfast and supper bundles, cash with change or an
  M-Pesa code;
- the deni book: two families and the school staffroom on credit to month end, the kiosk buying bales at the wholesale price,
  the regular with her 2%, a family paying part of what it owes by M-Pesa;
- sour milk brought back and refunded, a sale voided, spoilt tomatoes and a squashed loaf written off, cash banked at midday;
- a WhatsApp order for the apartments, left pending for a boda rider in the demo;
- the day closed KES 100 short, then two months and this month of expenses.

    python docs/demo-data/practice_sales_minimart.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
import datetime as dt

from demo_practice import CREDIT, MPESA_CODE, REFUND_CASH, SETTLE_CASH, SETTLE_MPESA, CASH, EXP_BANK, EXP_CASH, EXP_MPESA, Day, pick
from demo_setup import read_build_sheet, step

BREAKFAST = [("Supaloaf White Bread 400g", (1, 1)), ("Festive White Bread 400g", (1, 1)), ("Supaloaf Brown Bread 400g", (1, 1)),
             ("Brookside Whole Milk 500ml", (1, 3)), ("Fresha Milk 500ml", (1, 2)), ("KCC Gold Crown Milk 500ml", (1, 2)),
             ("Eggs (single)", (2, 6)), ("Mandazi (single)", (2, 5)), ("Blue Band Original 250g", (1, 1)),
             ("Ketepa Pride Tea Leaves 250g", (1, 1)), ("Sugar (loose)", (0.25, 0.5, 1))]
SUPPER = [("Soko Maize Meal 2kg", (1, 1)), ("Jogoo Maize Meal 2kg", (1, 1)), ("Pembe Maize Meal 2kg", (1, 1)),
          ("Sukuma Wiki (bunch)", (1, 3)), ("Tomatoes", (0.25, 0.5, 1)), ("Red Onions", (0.25, 0.5)), ("Cabbage (head)", (1, 1)),
          ("Dhania (bunch)", (1, 2)), ("Potatoes", (1, 2)), ("Rosecoco Beans (loose)", (0.5, 1)), ("Royco Beef Cube (single)", (1, 4)),
          ("Kimbo Cooking Fat 500g", (1, 1)), ("Rina Vegetable Oil 1L", (1, 1)), ("Sugar (loose)", (0.5, 1)),
          ("Charcoal (tin)", (1, 2)), ("Paraffin", (0.5, 1)), ("Kasuku Matches (single box)", (1, 2)), ("Eggs (single)", (2, 4)),
          ("Carrier Bag Small (non-woven)", (1, 1))]
SHELF = [("Geisha Soap 90g", (1, 3)), ("Colgate Maximum Cavity 100ml", (1, 1)), ("Omo Multi-Active 500g", (1, 1)),
         ("Menengai Bar Soap 800g", (1, 1)), ("Rosy Toilet Tissue (single roll)", (1, 4)), ("Always Maxi Thick Pads (8)", (1, 1)),
         ("Vaseline Petroleum Jelly 100ml", (1, 1)), ("Jik Bleach 750ml", (1, 1)), ("Softcare Diapers Medium (10)", (1, 1)),
         ("Indomie Chicken Noodles 70g", (1, 4)), ("Coca-Cola 500ml", (1, 2)), ("Fanta Orange 500ml", (1, 2)),
         ("Dasani Water 500ml", (1, 2)), ("Krackles 30g", (1, 3)), ("Tropical Heat Potato Crisps 50g", (1, 1)),
         ("Big G Chewing Gum (single)", (1, 4)), ("Nuvita Digestive Biscuits 100g", (1, 2)), ("Cadbury Dairy Milk 35g", (1, 1)),
         ("Kabras Sugar 1kg", (1, 1)), ("Fresh Fri Cooking Oil 1L", (1, 1)), ("Eveready AA Battery (pair)", (1, 1)),
         ("Kasuku Exercise Book 96 Pages", (1, 3)), ("Bic Pen Blue (single)", (1, 2)), ("Bananas (single)", (2, 6)),
         ("Avocado (single)", (1, 3)), ("Predator Energy 400ml", (1, 1)), ("Farmer's Choice Smokie (single)", (1, 3)),
         ("Brookside Strawberry Yoghurt 500ml", (1, 1)), ("Daima Vanilla Yoghurt 250ml", (1, 2)), ("Mosquito Coils (box of 10)", (1, 1))]

WANJIKU = "Mama Wanjiku - House 14, Mwiki Gardens"
OTIENO = "Otieno - Block C, Kasarani Estate"
KIOSK = "Baraka Kiosk (Chief's Road)"
SCHOOL = "Mwiki Primary School Staffroom"
JANE = "Jane Muthoni - regular"
SUNRISE = "Sunrise Court Apartments (deliveries)"
BREAD_VAN = "Mkate Fresh Bread Distributors"
DAIRY_VAN = "Maziwa Bora Dairy & Cold Chain"


def main():
    day = Day("demo.minimart@ultimatepos.local", "JiraniDemo@2026", "jirani")
    day.open(3000, "Float from the safe: coins and 50s for change")
    voided = set()
    if not day.args.finish_only:
        voided = trade(day)

    # Expenses, dated when they were paid; the county permit and the fridge service in the first of the three months
    first = day.today.replace(day=1)
    oldest = ((first - dt.timedelta(days=1)).replace(day=1) - dt.timedelta(days=1)).replace(day=1)

    def month(m, current):
        d = lambda n: m.replace(day=min(n, day.today.day) if current else n)
        entries = [(m, "RENT", "Shop rent, Mwiki Road", 25000, EXP_BANK, "Mwiki Road Properties"),
                   (d(3), "UTIL", "Electricity tokens: fridges and freezer", 4500, EXP_MPESA, "Kenya Power"),
                   (d(3), "UTIL", "Water", 600, EXP_MPESA, "Nairobi Water")]
        if not current:
            entries += [(m.replace(day=28), "SAL", "Salaries: 2 shop attendants", 28000, EXP_BANK, None),
                        (m.replace(day=28), "FIN", "M-Pesa till charges", 900, EXP_MPESA, None),
                        (m.replace(day=15), "SEC", "Estate night guard contribution", 3000, EXP_CASH, "Mwiki Gardens Welfare"),
                        (m.replace(day=10), "PKG", "Wrapping paper and small bags for loose sugar", 500, EXP_CASH, None)]
        if m == oldest:
            entries += [(m.replace(day=5), "LIC", "Nairobi County single business permit", 15000, EXP_MPESA, "Nairobi City County"),
                        (m.replace(day=20), "REF", "Fridge gas refill and service", 2500, EXP_CASH, "Kasarani Fridge Repairs")]
        return entries
    day.close(-100, "100 short")
    day.expenses(month)
    day.summary(voided)


def trade(day):
    rng, line, sell = day.rng, day.line, day.sell
    costs = {r["name"]: r["cost"] for r in read_build_sheet("minimart-products.xlsx")}

    # 1. The vans, before opening: bread paid in cash from the till, the dairy by M-Pesa
    bread = [("Supaloaf White Bread 400g", 20), ("Festive White Bread 400g", 20), ("Supaloaf Brown Bread 400g", 10),
             ("Festive White Bread 800g", 6), ("Mandazi (single)", 40)]
    po, value = day.receive(BREAD_VAN, [(n, q, costs[n]) for n, q in bread], f"DN-{day.today:%m%d}-B")
    day.api.call("POST", f"/suppliers/{day.suppliers[BREAD_VAN]}/payments",
                 {"amount": value, "paymentMethod": SETTLE_CASH, "paymentReference": None, "notes": f"Cash to the driver, {po['orderNumber']}"})
    step(f"bread van: {po['orderNumber']}, KES {value:,.0f} paid cash from the till")

    dairy = [("Brookside Whole Milk 500ml", 40), ("Fresha Milk 500ml", 24), ("KCC Gold Crown Milk 500ml", 24),
             ("Brookside Strawberry Yoghurt 500ml", 6), ("Daima Vanilla Yoghurt 250ml", 12), ("Brookside Mala 500ml", 12),
             ("Farmer's Choice Smokie (single)", 30)]
    po, value = day.receive(DAIRY_VAN, [(n, q, costs[n]) for n, q in dairy], f"DN-{day.today:%m%d}-D",
                            {"Brookside Mala 500ml": 14, "Farmer's Choice Smokie (single)": 30, "Brookside Strawberry Yoghurt 500ml": 21,
                             "Daima Vanilla Yoghurt 250ml": 21})
    day.api.call("POST", f"/suppliers/{day.suppliers[DAIRY_VAN]}/payments",
                 {"amount": value, "paymentMethod": SETTLE_MPESA, "paymentReference": day.mpesa_code(), "notes": po["orderNumber"]})
    step(f"dairy van: {po['orderNumber']}, KES {value:,.0f} paid by M-Pesa; yoghurt, mala and smokies in dated batches")

    # 2. The breakfast rush
    for _ in range(18):
        sell([line(n, q) for n, q in pick(rng, BREAKFAST, 1, 3)])
    for _ in range(3):
        sell([], bundle="BREAKFAST")
    sell([line("Eggs (single)", 1, "tray")], method=MPESA_CODE)
    step("breakfast rush: 22 sales, a tray of eggs")

    # 3. Midday: the shelves, the deni book, the kiosk
    for _ in range(16):
        sell([line(n, q) for n, q in pick(rng, SHELF, 1, 3)], mpesa_share=0.55)
    month_end = (day.today.replace(day=28) + dt.timedelta(days=4)).replace(day=1) - dt.timedelta(days=1)
    sell([line("Jogoo Maize Meal 2kg", 1), line("Rina Vegetable Oil 1L", 1), line("Sugar (loose)", 1), line("Geisha Soap 90g", 2)],
         WANJIKU, CREDIT, max((month_end - day.today).days, 3), "Pays at month end")
    sell([line("Ketepa Pride Tea Leaves 250g", 2), line("Kabras Sugar 1kg", 2), line("Brookside Long Life Milk 500ml", 6),
          line("Manji Digestive Biscuits 200g", 2)], SCHOOL, CREDIT, 30, "Staffroom tea, term account")
    owed = day.api.call("GET", f"/customers/{day.customers[KIOSK]}/balance")["outstandingBalance"]
    if owed > 0:
        day.api.call("POST", f"/customers/{day.customers[KIOSK]}/payments", {
            "amount": owed, "notes": "Last restock", "paymentMethod": SETTLE_MPESA, "paymentReference": day.mpesa_code()})
    sell([line("Soko Maize Meal 2kg", 2, "bale"), line("Kabras Sugar 1kg", 1, "bale"), line("Indomie Chicken Noodles 70g", 1, "ctn"),
          line("Coca-Cola 500ml", 1, "ctn")], KIOSK, CREDIT, 7, "Kiosk restock, pays Friday")
    sell([line(n, q) for n, q in pick(rng, SHELF, 2, 4)], JANE)
    owed = day.api.call("GET", f"/customers/{day.customers[OTIENO]}/balance")["outstandingBalance"]
    if owed > 0:
        day.api.call("POST", f"/customers/{day.customers[OTIENO]}/payments", {
            "amount": min(owed, 1500), "notes": "Toward his deni", "paymentMethod": SETTLE_MPESA, "paymentReference": day.mpesa_code()})
    day.pay_out(8000, "Banked at the agent: midday cash")
    step("midday: 16 sales, Mama Wanjiku and the school staffroom on deni, the kiosk's bales at wholesale, Otieno's payment, 8,000 banked")

    # 4. A WhatsApp order for the apartments, for the rider
    pending = [o for o in day.api.call("GET", "/delivery-orders", params={"pageSize": 50}) or [] if o.get("status") == 0]
    if not pending:
        day.api.call("POST", "/delivery-orders", {
            "customerId": day.customers[SUNRISE], "deliveryAddress": "Sunrise Court, Block B, House 6, Kasarani",
            "contactPhone": "0700000406", "notes": "WhatsApp order. Call on arrival", "expectedPaymentMethod": MPESA_CODE,
            "lines": [line("LPG Refill 6kg", 1), line("Pembe Maize Meal 2kg", 2), line("Fresh Fri Cooking Oil 1L", 1),
                      line("Brookside Whole Milk 500ml", 4)]})
        step("delivery order for Sunrise Court, pending a rider")

    # 5. The evening supper rush
    for _ in range(26):
        sell([line(n, q) for n, q in pick(rng, SUPPER, 2, 5)], mpesa_share=0.35)
    for _ in range(3):
        sell([], bundle="SUPPER")
    sell([line("Kimbo Cooking Fat 500g", 1), line("Sukuma Wiki (bunch)", 2)], OTIENO, CREDIT, 7)
    sell([], method=MPESA_CODE, bundle="SCHOOL")
    step("evening rush: 31 sales, the supper pack, Otieno on deni, a back-to-school pack")

    # 6. Sour milk back, a mistake voided, what spoiled written off
    sour = sell([line("Fresha Milk 500ml", 2), line("Supaloaf White Bread 400g", 1)], method=CASH)
    milk = next(l for l in sour["lines"] if l["productName"] == "Fresha Milk 500ml")
    day.api.call("POST", f"/sales/{sour['saleId']}/returns", {
        "reason": "Milk sour before its date: back to the dairy van", "refundMethod": REFUND_CASH, "refundReference": None,
        "lines": [{"saleLineId": milk["saleLineId"], "quantity": 1, "restock": False}]})
    wrong = sell([line("Omo Multi-Active 1kg", 1)], method=CASH)
    day.api.call("POST", f"/sales/{wrong['saleId']}/void")
    for name, qty, reason in (("Tomatoes", -1.5, "Spoilt: soft and split"), ("Supaloaf White Bread 400g", -1, "Squashed in the crate")):
        p = day.products[name]
        day.api.call("POST", "/stock/adjustments", {"productId": p["productId"], "unitOfMeasureId": p["baseUnitOfMeasureId"],
                                                    "quantityChange": qty, "reason": reason})
    step(f"{sour['saleNumber']}: sour milk refunded; {wrong['saleNumber']} voided; tomatoes and a loaf written off")
    return {wrong["saleId"]}


if __name__ == "__main__":
    main()
