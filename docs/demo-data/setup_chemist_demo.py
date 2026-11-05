"""The chemist demo: Tiba Bora Chemist, a retail pharmacy in Juja. Creates and loads it through the API (see demo_setup.py), with a
pharmacist, a pharmaceutical technologist and a counter attendant who cannot dispense. DEMO_MINIMART_AND_CHEMIST.md §2 and §3.

Every medicine is batch-tracked and its opening stock is received in batches with expiry dates, a few of them expiring within two
months; prescription-only medicines need the prescription recorded at the till.

    python docs/demo-data/setup_chemist_demo.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
from demo_setup import COUNTER, DISPENSING, MANAGER, RETAIL, WHOLESALE, run

run({
    "business": {
        "businessName": "Tiba Bora Chemist (Demo)", "tradingName": "Tiba Bora Chemist",
        "sector": 1, "businessType": 1, "kind": 1, "kraPin": "P000000004C",
        "physicalAddress": "Kenyatta Road, Juja", "county": "Kiambu", "phoneNumber": "0700000004",
        "email": "info@tibaborachemist.demo", "isVatRegistered": False, "mpesaTillNumber": "000444", "maxDiscountPercent": 10,
        "allowNegativeStock": True, "etimsEnabled": False,
    },
    "owner": {"fullName": "Tiba Bora Demo Owner", "email": "demo.chemist@ultimatepos.local", "phoneNumber": "0700000004",
              "password": "TibaDemo@2026"},
    # The Pharmacy sector's units; a business registered before the sachet was added does not have it.
    "units": [("Tablet", "tab"), ("Capsule", "cap"), ("Bottle", "btl"), ("Strip", "strip"), ("Tube", "tube"), ("Sachet", "sachet")],
    "files": ("chemist-categories.xlsx", "chemist-products.xlsx"),
    "first_department": "PCA",
    "phone_prefix": "07000005",
    "invoice_base": 52700,
    # name: (payment term days, paid after receiving: "full" | "half" | None)
    "suppliers": {
        "Kilimani Pharmaceutical Distributors": (30, "half"),
        "Afya Generics Wholesalers": (30, None),
        "Mediplus Surgical Supplies Ltd": (30, "full"),
        "Uzuri Cosmetics Distributors": (14, "full"),
    },
    # (name, kra pin, contact person, phone, address, price type, credit limit, standing discount %, discount note)
    "customers": [
        ("Juja Road Medical Clinic", "P000000501J", "Clinical Officer in charge", "0700000501", "Juja", WHOLESALE, 80000, None, None),
        ("Kalimoni Mission Dispensary", "P000000502K", "Sister in charge", "0700000502", "Kalimoni", WHOLESALE, 50000, None, None),
        ("Afya Bora Medical Cover (insurer)", "P000000503A", "Claims Office", "0700000503", "Nairobi", RETAIL, 150000, None, None),
        ("Mzee Kamau - diabetes & BP", None, None, "0700000504", "Juja", RETAIL, 10000, 5, "Chronic patient, monthly refill"),
        ("Grace Wambui - asthma", None, None, "0700000505", "Juja Farm", RETAIL, None, None, None),
        ("Mama Akinyi - young family", None, None, "0700000506", "Gachororo", RETAIL, None, None, None),
    ],
    "bundles": [],
    # role: (description, permissions). Only staff who may dispense can sell a prescription-only medicine.
    "roles": {
        "Pharmacist": ("Superintendent: dispenses, prices, receives, the day's cash", MANAGER + DISPENSING),
        "Pharmaceutical Technologist": ("Dispenses at the counter and takes returns", COUNTER + ["Sales.Return"] + DISPENSING),
        "Counter Attendant": ("Over-the-counter, cosmetics and the till; cannot dispense", COUNTER),
    },
    "users": [
        ("pharmacist.ch@ultimatepos.local", "Wanjiru Njoroge (Pharmacist)", "0700000511", "Pharmacist"),
        ("pharmtech.ch@ultimatepos.local", "Kevin Omondi (Pharm. Tech.)", "0700000512", "Pharmaceutical Technologist"),
        ("counter.ch@ultimatepos.local", "Mercy Chebet (Counter)", "0700000513", "Counter Attendant"),
    ],
    "staff_password": "TibaStaff2026",
})
