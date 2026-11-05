"""The liquor-store demo: Baridi Wines & Spirits, an off-licence on Kamiti Road, Zimmerman. Creates and loads it through the API (see
demo_setup.py), with a manager, a cashier and a storekeeper. DEMO_LIQUOR_STORE.md §2 and §3.

    python docs/demo-data/setup_liquor_demo.py [--owner-password <changed password>] [--api http://localhost:5062/api/v1]
"""
from demo_setup import COUNTER, DELIVERIES, MANAGER, RETAIL, STOREKEEPER, WHOLESALE, run

run({
    "business": {
        "businessName": "Baridi Wines & Spirits (Demo)", "tradingName": "Baridi Wines & Spirits",
        "sector": 8, "businessType": 1, "kind": 1, "kraPin": "P000000005W",
        "physicalAddress": "Kamiti Road, Zimmerman", "county": "Nairobi", "phoneNumber": "0700000005",
        "email": "info@baridiwines.demo", "isVatRegistered": True, "mpesaTillNumber": "000555", "maxDiscountPercent": 5,
        "allowNegativeStock": False, "etimsEnabled": False,
    },
    "owner": {"fullName": "Baridi Demo Owner", "email": "demo.liquor@ultimatepos.local", "phoneNumber": "0700000005",
              "password": "BaridiDemo@2026"},
    # A business registered before the Liquor sector had them does not have these yet.
    "units": [("Bottle", "btl"), ("Crate", "crate"), ("Carton", "ctn"), ("Case", "case")],
    "expense_categories": [("PKG", "Packaging & Carrier Bags", "Carrier bags, bottle sleeves and gift bags used at the till"),
                           ("REF", "Refrigeration", "Fridges and coolers: servicing, repairs and gas")],
    # 18 and over; sold 10:00-23:00 so the demo works in the day. Put the hours on the prospect's licence here (§1.3).
    "age_rules": {"minimumAge": 18, "weekdayFrom": "10:00", "weekdayTo": "23:00", "weekendFrom": None, "weekendTo": None},
    "files": ("liquor-categories.xlsx", "liquor-products.xlsx"),
    "first_department": "BER",
    "phone_prefix": "07000006",
    "invoice_base": 63400,
    # No monthly-spend reward: a discount for drinking more is not something to demo. Businesses get the wholesale price instead.
    # name: (payment term days, paid after receiving: "full" | "half" | None)
    "suppliers": {
        "Thika Road Beverages Ltd": (0, "full"),            # the brewer's distributor: paid before the lorry leaves
        "Mlimani Wines & Spirits Wholesalers": (14, "half"),
        "Roysambu Soft Drinks Depot": (7, "full"),
        "Githurai Snacks & Ice Supplies": (0, "full"),
        "Kahawa Tobacco Distributors": (0, "full"),
    },
    # (name, kra pin, contact person, phone, address, price type, credit limit, standing discount %, discount note). Credit only to
    # businesses, each with a limit: no deni for drinkers.
    "customers": [
        ("Kona Bar & Grill (Zimmerman)", None, "Wambui (owner)", "0700000601", "Zimmerman, 44 stage", WHOLESALE, 60000, None, None),
        ("Wanjiru Events & Catering", None, "Wanjiru", "0700000602", "Kahawa West", WHOLESALE, 150000, None, None),
        ("Thika Road Sacco (staff events)", None, "Admin Officer", "0700000603", "Roysambu", RETAIL, 40000, None, None),
        ("Sunset Gardens Apartments (deliveries)", None, "Caretaker", "0700000604", "Zimmerman, off Kamiti Road", RETAIL, None, None, None),
        ("Roysambu Members Club", None, "Club Secretary", "0700000605", "Roysambu", WHOLESALE, 80000, None, None),
    ],
    # (name, code, [(product, quantity in its base unit)])
    "bundles": [
        ("Sundowner Gin Pack", "SUNDOWNER", [
            ("Gilbey's Gin 750ml", 1), ("Schweppes Tonic Water 500ml", 2), ("Ice Cubes 2kg Bag", 1)]),
        ("Whisky Night Pack", "WHISKY", [
            ("Johnnie Walker Red Label 750ml", 1), ("Coca-Cola 1.25L", 2), ("Ice Cubes 2kg Bag", 1)]),
        ("Match Day Pack", "MATCHDAY", [
            ("Tusker Lager 500ml Can", 6), ("Guinness Smooth 500ml Can", 6), ("Roasted Groundnuts 100g", 2)]),
    ],
    # role: (description, permissions)
    "roles": {
        "Manager": ("Runs the shop: prices, receiving, voids, the day's cash, deliveries, credit to bars", MANAGER + DELIVERIES +
                    ["DeliveryOrders.AcceptCredit", "Businesses.View"]),
        "Cashier": ("The till: sales with the age check, empties, delivery orders", COUNTER + ["DeliveryOrders.View", "DeliveryOrders.Create"]),
        "Storekeeper": ("Receives the distributor's lorry, counts the stock", STOREKEEPER),
    },
    "users": [
        ("manager.lq@ultimatepos.local", "Samuel Kiprono (Manager)", "0700000611", "Manager"),
        ("cashier.lq@ultimatepos.local", "Mercy Atieno (Cashier)", "0700000612", "Cashier"),
        ("store.lq@ultimatepos.local", "Dennis Mwangi (Storekeeper)", "0700000613", "Storekeeper"),
    ],
    "staff_password": "BaridiStaff2026",
})
