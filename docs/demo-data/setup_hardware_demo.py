"""The hardware demo: Imara Hardware & Building Supplies. Creates and loads it through the API (see demo_setup.py).

    python docs/demo-data/setup_hardware_demo.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
from demo_setup import RETAIL, WHOLESALE, run

run({
    "business": {
        "businessName": "Imara Hardware & Building Supplies (Demo)", "tradingName": "Imara Hardware",
        "sector": 6, "businessType": 1, "kind": 1, "kraPin": "P000000001H",
        "physicalAddress": "Namanga Road, Kitengela", "county": "Kajiado", "phoneNumber": "0700000001",
        "email": "info@imarahardware.demo", "isVatRegistered": True, "mpesaTillNumber": "000111", "maxDiscountPercent": 5,
        "allowNegativeStock": True, "etimsEnabled": False,
    },
    "owner": {"fullName": "Imara Demo Owner", "email": "demo.hardware@ultimatepos.local", "phoneNumber": "0700000001",
              "password": "ImaraDemo@2026"},
    # New Hardware businesses start with these; kept for businesses registered before they were added.
    "units": [("Foot", "ft"), ("Tonne", "t")],
    "files": ("hardware-categories.xlsx", "hardware-products.xlsx"),
    "first_department": "BLD",
    "phone_prefix": "07000002",
    "invoice_base": 24500,
    # name: (payment term days, paid after receiving: "full" | "half" | None)
    "suppliers": {
        "Mavoko Cement Depot": (7, "full"),
        "Kasarani Steel & Roofing Distributors": (30, "half"),
        "Mashuru Quarry & Sand Suppliers": (0, "full"),
        "Kitengela Timber Yard": (0, "full"),
        "Industrial Area Plumbing Wholesalers": (30, None),
        "Paints & Coatings Stockists Ltd": (30, "half"),
        "Tools & Fasteners Wholesalers Ltd": (30, None),
    },
    # (name, kra pin, contact person, phone, address, price type, credit limit, standing discount %, discount note)
    "customers": [
        ("Kamau Builders & Contractors Ltd", "P000000101K", "John Kamau", "0700000101", "Kitengela", WHOLESALE, 300000, None, None),
        ("St. Teresa's Mixed Secondary School", "P000000102S", "The Bursar", "0700000102", "Isinya Road", RETAIL, 150000, None, None),
        ("Baraka Plumbing Works", None, "Baraka Mwangi", "0700000103", "Kitengela", WHOLESALE, 80000, None, None),
        ("Otieno the mason (fundi)", None, None, "0700000104", "Kitengela", RETAIL, 20000, 3, "Brings clients every week"),
        ("Wanjiru - Plot 45 self-build", None, None, "0700000105", "Plot 45, New Valley", RETAIL, 50000, None, None),
        ("Mutua Welding & Fabrication", None, "Peter Mutua", "0700000106", "Kitengela", WHOLESALE, None, None, None),
    ],
    # (name, code, [(product, quantity in its base unit)])
    "bundles": [
        ("Toilet Installation Set", "TOILET-SET", [
            ("Close-Coupled WC Set White", 1), ("Float Valve 1/2in", 1), ("Teflon Tape", 1), ("PVC Elbow 4in", 1),
            ("PVC Solvent Cement 250ml", 1)]),
        ("Roofing Starter Pack (1 room)", "ROOF-1RM", [
            ("Dumuzas G30 Corrugated 3m", 10), ("Ridge Cap G30 Plain 1.8m", 2), ("Roofing Nails", 3)]),
    ],
})
