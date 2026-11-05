"""The minimart demo: Jirani Minimart, an estate minimart in Kasarani. Creates and loads it through the API (see demo_setup.py), with
a manager, a cashier and a storekeeper. DEMO_MINIMART_AND_CHEMIST.md §2 and §3.

    python docs/demo-data/setup_minimart_demo.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
from demo_setup import COUNTER, DELIVERIES, MANAGER, RETAIL, STOREKEEPER, WHOLESALE, run

run({
    "business": {
        "businessName": "Jirani Minimart (Demo)", "tradingName": "Jirani Minimart",
        "sector": 4, "businessType": 1, "kind": 1, "kraPin": "P000000003M",
        "physicalAddress": "Mwiki Road, Kasarani", "county": "Nairobi", "phoneNumber": "0700000003",
        "email": "info@jiraniminimart.demo", "isVatRegistered": False, "mpesaTillNumber": "000333", "maxDiscountPercent": 5,
        "allowNegativeStock": True, "etimsEnabled": False,
    },
    "owner": {"fullName": "Jirani Demo Owner", "email": "demo.minimart@ultimatepos.local", "phoneNumber": "0700000003",
              "password": "JiraniDemo@2026"},
    # A business registered before the Minimart sector had them does not have these yet.
    "units": [("Dozen", "doz"), ("Carton", "ctn"), ("Tray", "tray"), ("Bale", "bale")],
    "expense_categories": [("PKG", "Packaging & Carrier Bags", "Carrier bags and wrapping used at the till"),
                           ("REF", "Refrigeration", "Fridge and freezer servicing, repairs and gas")],
    "files": ("minimart-categories.xlsx", "minimart-products.xlsx"),
    "first_department": "DRY",
    "phone_prefix": "07000004",
    "invoice_base": 41200,
    # 2% off for the rest of the month once a customer has spent KES 10,000 in it: the regulars' reward.
    "monthly_discount": (10000, 2),
    # name: (payment term days, paid after receiving: "full" | "half" | None)
    "suppliers": {
        "Mwiki Wholesalers & Distributors": (14, "half"),
        "Mkate Fresh Bread Distributors": (0, "full"),
        "Maziwa Bora Dairy & Cold Chain": (7, "full"),
        "Kasarani Beverage Distributors": (14, None),
        "Githurai Market Produce (Mama Njeri)": (0, "full"),
        "Ruiru Poultry Farm": (0, "full"),
        "Mwiki Gas Point": (7, "full"),
    },
    # The deni book: (name, kra pin, contact person, phone, address, price type, credit limit, standing discount %, discount note)
    "customers": [
        ("Mama Wanjiku - House 14, Mwiki Gardens", None, None, "0700000401", "Mwiki Gardens", RETAIL, 5000, None, None),
        ("Otieno - Block C, Kasarani Estate", None, None, "0700000402", "Kasarani Estate", RETAIL, 8000, None, None),
        ("Baraka Kiosk (Chief's Road)", None, "Baraka", "0700000403", "Chief's Road, Mwiki", WHOLESALE, 20000, None, None),
        ("Mwiki Primary School Staffroom", None, "Deputy Head Teacher", "0700000404", "Mwiki", RETAIL, 15000, None, None),
        ("Jane Muthoni - regular", None, None, "0700000405", "Mwiki Gardens", RETAIL, None, 2, "Buys every day"),
        ("Sunrise Court Apartments (deliveries)", None, "Caretaker", "0700000406", "Sunrise Court, Kasarani", RETAIL, 10000, None, None),
    ],
    # (name, code, [(product, quantity in its base unit)])
    "bundles": [
        ("Breakfast Combo", "BREAKFAST", [
            ("Supaloaf White Bread 400g", 1), ("Brookside Whole Milk 500ml", 2), ("Blue Band Original 250g", 1)]),
        ("Ugali & Sukuma Supper Pack", "SUPPER", [
            ("Soko Maize Meal 2kg", 1), ("Sukuma Wiki (bunch)", 3), ("Tomatoes", 0.5), ("Red Onions", 0.25)]),
        ("Back-to-School Pack", "SCHOOL", [("Kasuku Exercise Book 96 Pages", 6), ("Bic Pen Blue (single)", 3)]),
    ],
    # role: (description, permissions)
    "roles": {
        "Manager": ("Runs the shop day to day: prices, receiving, voids, the day's cash", MANAGER + DELIVERIES),
        "Cashier": ("The till: sales, deni payments, delivery orders", COUNTER + ["DeliveryOrders.View", "DeliveryOrders.Create"]),
        "Storekeeper": ("Receives the vans and the wholesaler's deliveries", STOREKEEPER),
    },
    "users": [
        ("manager.mm@ultimatepos.local", "Peter Kamau (Manager)", "0700000411", "Manager"),
        ("cashier.mm@ultimatepos.local", "Faith Achieng (Cashier)", "0700000412", "Cashier"),
        ("store.mm@ultimatepos.local", "Brian Mutua (Storekeeper)", "0700000413", "Storekeeper"),
    ],
    "staff_password": "JiraniStaff2026",
})
