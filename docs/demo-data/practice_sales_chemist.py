"""A day of trading at the chemist demo (DEMO_MINIMART_AND_CHEMIST.md §3.6), through the API as the owner (who may dispense):

- about 40 sales: over-the-counter walk-ins (painkillers by the tablet or the strip, cold and flu, ORS and zinc for a child, family
  planning, cosmetics), cash with change or an M-Pesa code;
- prescriptions dispensed against a clinic's prescription, recorded with prescriber, patient and date: antibiotic courses, a child's
  suspension, an insured patient billed to the insurer;
- Mzee Kamau's monthly refill for diabetes and blood pressure with his 5%, on account; the clinic and the mission dispensary buying
  stock at the wholesale price;
- a sealed cream returned and restocked, a sale voided, a broken syrup bottle written off from its batch;
- the day closed KES 50 over, then two months and this month of expenses (rent, salaries, PPB licence, waste disposal).

Batch-tracked stock is sold first-expiry-first-out by the API; a medicine is never sold from an expired batch.

    python docs/demo-data/practice_sales_chemist.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
import datetime as dt

from demo_practice import CASH, CREDIT, EXP_BANK, EXP_CASH, EXP_MPESA, MPESA_CODE, REFUND_MPESA, SETTLE_BANK, SETTLE_MPESA, Day, pick
from demo_setup import step

COUNTER = [("Panadol Extra Tablets", (2, 10)), ("Paracetamol 500mg Tablets", (6, 20)), ("Mara Moja Tablets (2)", (1, 3)),
           ("Ibuprofen 400mg Tablets", (6, 10)), ("Brufen 400mg Tablets", (6, 10)), ("Coldcap Capsules", (4, 10)),
           ("Cetirizine 10mg Tablets", (5, 10)), ("Piriton 4mg Tablets", (6, 12)), ("Strepsils Lozenges (8)", (1, 1)),
           ("Benylin 4 Flu Syrup 100ml", (1, 1)), ("Expectorant Cough Syrup 100ml", (1, 1)), ("Vicks VapoRub 25g", (1, 1)),
           ("Eno Fruit Salt Sachet", (1, 3)), ("Omeprazole 20mg Capsules", (7, 14)), ("Relcer Gel 180ml", (1, 1)),
           ("Loperamide 2mg Capsules", (4, 6)), ("Buscopan 10mg Tablets", (6, 10)), ("Albendazole 400mg Tablet", (1, 4)),
           ("Artemether/Lumefantrine 20/120mg Tablets (24)", (1, 1)), ("Clotrimazole Cream 1% 20g", (1, 1)),
           ("Hydrocortisone Cream 1% 15g", (1, 1)), ("Calamine Lotion 100ml", (1, 1)), ("Vitamin C 1000mg Effervescent (10)", (1, 1)),
           ("Fabric Plaster (single)", (2, 10)), ("Cotton Wool 100g", (1, 1)), ("Surgical Spirit 100ml", (1, 1)),
           ("Dettol Antiseptic Liquid 125ml", (1, 1)), ("Deep Heat Rub 35g", (1, 1)), ("Surgical Face Mask (single)", (1, 5))]
PERSONAL = [("Vaseline Petroleum Jelly 250ml", (1, 1)), ("Nivea Soft Cream 200ml", (1, 1)), ("Vaseline Lip Therapy 10g", (1, 2)),
            ("Aqueous Cream 500g", (1, 1)), ("Trust Condoms (3)", (1, 3)), ("Durex Condoms (3)", (1, 1)),
            ("Postinor-2 Tablets (2)", (1, 1)), ("Pregnancy Test Strip", (1, 2)), ("Combined Oral Contraceptive (28)", (1, 3)),
            ("Johnson's Baby Oil 200ml", (1, 1)), ("Woodward's Gripe Water 150ml", (1, 1)), ("Digital Thermometer", (1, 1))]

CLINIC = "Juja Road Medical Clinic"
MISSION = "Kalimoni Mission Dispensary"
INSURER = "Afya Bora Medical Cover (insurer)"
KAMAU = "Mzee Kamau - diabetes & BP"
GRACE = "Grace Wambui - asthma"
AKINYI = "Mama Akinyi - young family"

# Prescriptions brought in from the clinic: (patient, age, phone, [(product, quantity)], prescriber, registration number)
PRESCRIPTIONS = [
    ("Samuel Kiprono", 34, "0700000521", [("Amoxicillin 500mg Capsules", 15), ("Paracetamol 500mg Tablets", 18)]),
    ("Esther Nduta", 27, "0700000522", [("Co-Amoxiclav 625mg Tablets", 14), ("Ibuprofen 400mg Tablets", 10)]),
    ("Joseph Mwangi", 45, "0700000523", [("Ciprofloxacin 500mg Tablets", 10)]),
    ("Lucy Atieno", 31, "0700000524", [("Metronidazole 400mg Tablets", 21), ("Fluconazole 150mg Capsule", 1)]),
    ("Daniel Kariuki", 52, "0700000525", [("Doxycycline 100mg Capsules", 14)]),
]
PRESCRIBERS = [("Dr. P. Mwangi", "A12345 (demo)"), ("Clinical Officer S. Wekesa", "CO-4521 (demo)")]


def main():
    day = Day("demo.chemist@ultimatepos.local", "TibaDemo@2026", "tibabora")
    day.open(2000, "Float from the safe")
    voided = set()
    if not day.args.finish_only:
        voided = trade(day)
    day.close(50, "50 over")

    # Expenses, dated when they were paid; the PPB premises licence and the waste collection in the first of the three months
    first = day.today.replace(day=1)
    oldest = ((first - dt.timedelta(days=1)).replace(day=1) - dt.timedelta(days=1)).replace(day=1)

    def month(m, current):
        d = lambda n: m.replace(day=min(n, day.today.day) if current else n)
        entries = [(m, "RENT", "Shop rent, Kenyatta Road", 30000, EXP_BANK, "Juja Business Centre Ltd"),
                   (d(3), "UTIL", "Electricity tokens", 2500, EXP_MPESA, "Kenya Power"),
                   (d(3), "UTIL", "Internet", 2500, EXP_MPESA, "Safaricom Home")]
        if not current:
            entries += [(m.replace(day=28), "SAL", "Salaries: pharmaceutical technologist and counter attendant", 53000, EXP_BANK, None),
                        (m.replace(day=28), "FIN", "M-Pesa till charges", 700, EXP_MPESA, None),
                        (m.replace(day=15), "SEC", "Night guard (shared with the block)", 3000, EXP_CASH, "Juja Business Centre Ltd")]
        if m == oldest:
            entries += [(m.replace(day=6), "LIC", "PPB retail pharmacy premises licence", 7500, EXP_MPESA, "Pharmacy and Poisons Board"),
                        (m.replace(day=6), "LIC", "Kiambu County single business permit", 10000, EXP_MPESA, "Kiambu County"),
                        (m.replace(day=20), "DSP", "Collection of expired medicines for disposal", 1500, EXP_CASH, "Licensed waste handler")]
        return entries
    day.expenses(month)
    day.summary(voided)


def trade(day):
    rng, line, sell = day.rng, day.line, day.sell

    def prescription(patient, age, phone, notes=None):
        prescriber, registration = rng.choice(PRESCRIBERS)
        return {"prescriptionNumber": f"JRC-{rng.randint(10000, 99999)}", "prescriberName": prescriber,
                "prescriberRegistrationNumber": registration, "prescriberFacility": CLINIC, "patientName": patient,
                "patientPhoneNumber": phone, "patientAgeYears": age, "prescriptionDate": day.today.isoformat(), "notes": notes}

    # 1. Morning: over the counter, by the tablet or the strip
    for _ in range(12):
        lines = [line(n, q) for n, q in pick(rng, COUNTER, 1, 3)]
        if rng.random() < 0.3:
            lines.append(line(rng.choice(["Panadol Extra Tablets", "Paracetamol 500mg Tablets", "Cetirizine 10mg Tablets"]), 1, "strip"))
        sell(lines)
    sell([line("ORS Sachet", 4), line("Zinc 20mg Dispersible Tablets (10)", 1), line("Paracetamol Syrup 120mg/5ml 60ml", 1)], AKINYI,
         MPESA_CODE)
    step("morning: 13 over-the-counter sales, ORS and zinc for Mama Akinyi's child")

    # 2. Prescriptions from the clinic
    for patient, age, phone, items in PRESCRIPTIONS:
        sell([line(n, q) for n, q in items], prescription=prescription(patient, age, phone), mpesa_share=0.6)
    sell([line("Amoxicillin Suspension 125mg/5ml 100ml", 1), line("Calpol Infant Suspension 60ml", 1)], AKINYI, MPESA_CODE,
         prescription=prescription("Baby Otieno (child of Mama Akinyi)", 2, "0700000506", "Weight 12 kg"))
    sell([line("Augmentin 625mg Tablets", 1, "strip"), line("Brufen 400mg Tablets", 10)], INSURER, CREDIT, 45,
         "Member AB-44821, Peter Ochieng. Claim with the prescription copy",
         prescription=prescription("Peter Ochieng", 39, "0700000526", "Insured: Afya Bora member AB-44821"))
    step(f"{len(PRESCRIPTIONS) + 2} prescriptions dispensed, one billed to Afya Bora on account")

    # 3. The chronic patients
    sell([line("Metformin 500mg Tablets", 60), line("Amlodipine 5mg Tablets", 30), line("Aspirin 75mg Tablets", 30)], KAMAU, CREDIT, 30,
         "Monthly refill", prescription=prescription("John Kamau", 67, "0700000504", "Repeat prescription, diabetes clinic"))
    sell([line("Salbutamol Inhaler 100mcg", 1)], GRACE, MPESA_CODE,
         prescription=prescription("Grace Wambui", 23, "0700000505", "Repeat: asthma"))
    owed = day.api.call("GET", f"/customers/{day.customers[KAMAU]}/balance")["outstandingBalance"]
    if owed > 1500:
        day.api.call("POST", f"/customers/{day.customers[KAMAU]}/payments", {
            "amount": 1000, "notes": "From his son, toward the account", "paymentMethod": SETTLE_MPESA, "paymentReference": day.mpesa_code()})
    step("Mzee Kamau's monthly refill with his 5%, on account; Grace's inhaler")

    # 4. The clinic and the mission dispensary restock at the wholesale price
    sell([line("Paracetamol 500mg Tablets", 10, "strip"), line("ORS Sachet", 20), line("Gauze Swabs (100)", 2),
          line("Fabric Plaster (single)", 1, "box"), line("Surgical Face Mask (single)", 1, "box"), line("Surgical Spirit 100ml", 4)],
         CLINIC, CREDIT, 30, "LPO JRC-0311")
    sell([line("Cotton Wool 100g", 6), line("Crepe Bandage 10cm", 6), line("Zinc 20mg Dispersible Tablets (10)", 6),
          line("Calamine Lotion 100ml", 3)], MISSION, CREDIT, 30, "Monthly order")
    insurer = day.api.call("GET", f"/customers/{day.customers[INSURER]}/balance")["outstandingBalance"]
    if insurer > 5000:
        day.api.call("POST", f"/customers/{day.customers[INSURER]}/payments", {
            "amount": round(insurer / 2, 2), "notes": "Claims batch paid", "paymentMethod": SETTLE_BANK, "paymentReference": f"EFT-{day.today:%y%m%d}"})
    step("the clinic and the dispensary on account at wholesale")

    # 5. Afternoon and evening
    for _ in range(14):
        menu = PERSONAL if rng.random() < 0.4 else COUNTER
        sell([line(n, q) for n, q in pick(rng, menu, 1, 2)], mpesa_share=0.55)
    step("afternoon and evening: 14 sales")

    # 6. A sealed cream back on the shelf, a mistake voided, a broken bottle written off from its batch
    sale = sell([line("Nivea Soft Cream 200ml", 1), line("Vaseline Lip Therapy 10g", 1)], method=MPESA_CODE)
    cream = next(l for l in sale["lines"] if l["productName"] == "Nivea Soft Cream 200ml")
    day.api.call("POST", f"/sales/{sale['saleId']}/returns", {
        "reason": "Bought the wrong size; returned sealed", "refundMethod": REFUND_MPESA, "refundReference": day.mpesa_code(),
        "lines": [{"saleLineId": cream["saleLineId"], "quantity": 1, "restock": True}]})
    wrong = sell([line("Brufen 400mg Tablets", 20)], method=CASH)
    day.api.call("POST", f"/sales/{wrong['saleId']}/void")
    syrup = day.products["Benylin 4 Flu Syrup 100ml"]
    lot = min((b for b in day.api.call("GET", "/stock/batches", params={"productId": syrup["productId"], "includeExpired": "false"})
               if b["quantityOnHand"] > 0), key=lambda b: b["expiryDate"])
    day.api.call("POST", "/stock/adjustments", {"productId": syrup["productId"], "unitOfMeasureId": syrup["baseUnitOfMeasureId"],
                                                "quantityChange": -1, "reason": "Bottle broken on the shelf", "stockBatchId": lot["stockBatchId"]})
    step(f"{sale['saleNumber']}: sealed cream returned and restocked; {wrong['saleNumber']} voided; "
         f"a Benylin bottle written off from batch {lot['batchNumber']}")
    return {wrong["saleId"]}


if __name__ == "__main__":
    main()
