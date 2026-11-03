"""The electrical demo: Mwangaza Electricals & Solar. Creates and loads it through the API (see demo_setup.py).

Registered as a Hardware business (sector 6) until the Electrical sector exists (DEMO_HARDWARE_AND_ELECTRICAL.md §6, B1); then
reclassify it from the platform. It needs no units beyond the Hardware sector's.

    python docs/demo-data/setup_electrical_demo.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
from demo_setup import RETAIL, WHOLESALE, run

run({
    "business": {
        "businessName": "Mwangaza Electricals & Solar (Demo)", "tradingName": "Mwangaza Electricals",
        "sector": 7, "businessType": 1, "kind": 1, "kraPin": "P000000002E",
        "physicalAddress": "Kamakis Road, Ruiru", "county": "Kiambu", "phoneNumber": "0700000002",
        "email": "info@mwangazaelectricals.demo", "isVatRegistered": True, "mpesaTillNumber": "000222", "maxDiscountPercent": 10,
        "allowNegativeStock": True, "etimsEnabled": False,
    },
    "owner": {"fullName": "Mwangaza Demo Owner", "email": "demo.electrical@ultimatepos.local", "phoneNumber": "0700000002",
              "password": "MwangazaDemo@2026"},
    "units": [("Coil", "coil"), ("Set", "set")],
    "files": ("electrical-categories.xlsx", "electrical-products.xlsx"),
    "first_department": "CBL",
    "phone_prefix": "07000003",
    "invoice_base": 31800,
    # name: (payment term days, paid after receiving: "full" | "half" | None)
    "suppliers": {
        "Industrial Area Cables Distributors": (30, "half"),
        "Nyota Electrical Wholesalers": (30, None),
        "Jua Power Solar Distributors": (14, "full"),
        "Home Appliances Distributors Ltd": (30, "half"),
    },
    # (name, kra pin, contact person, phone, address, price type, credit limit, standing discount %, discount note)
    "customers": [
        ("Kariuki Electrical Contractors", "P000000201K", "James Kariuki", "0700000201", "Ruiru", WHOLESALE, 200000, None, None),
        ("Brightline Solar Installers", "P000000202B", "Faith Achieng", "0700000202", "Juja", WHOLESALE, 150000, None, None),
        ("Omondi the electrician (fundi wa stima)", None, None, "0700000203", "Ruiru", RETAIL, 30000, 5, "Regular electrician"),
        ("Greenview Apartments Management", "P000000204G", "Property Manager", "0700000204", "Membley", RETAIL, 100000, None, None),
        ("Njeri - new house, Membley", None, None, "0700000205", "Membley", RETAIL, None, None, None),
    ],
    # (name, code, [(product, quantity in its base unit)])
    "bundles": [
        ("Solar Kit 1kW", "SOLAR-1KW", [
            ("Jinko 450W Mono Solar Panel", 2), ("Hybrid Inverter 3kVA 24V", 1), ("Gel Battery 12V 200Ah", 2),
            ("Solar PV Cable 4mm", 10), ("MC4 Connector Pair", 1), ("Panel Mounting Bracket Set", 1)]),
        ("Single-Room Wiring Kit", "WIRE-1RM", [
            ("Ready Board 2-Socket with MCB", 1), ("EAC Single Core 2.5mm Red", 20), ("EAC Single Core 2.5mm Black", 20),
            ("Tronic 1-Gang 1-Way Switch", 2), ("Tronic 13A Twin Switched Socket", 1), ("Ceiling Rose", 1),
            ("Tronic LED Bulb 9W B22", 1)]),
    ],
})
