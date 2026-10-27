# Onboarding a minimart (Kenya)

How to set up a neighbourhood minimart in Ultimate POS. The shop sells several hundred to a few thousand items across groceries,
fresh food, drinks, household goods and personal care. It has **never used a POS**: sales are rung on a calculator or written in a
receipt book, credit is kept in an exercise book (the *deni* book), and stock is known by looking at the shelves. That shapes the
plan. There is nothing to migrate, so the work is:

1. **Site readiness:** hardware, power and internet (§1.1).
2. **Building the catalogue from the shelves:** every item scanned, priced and categorised for the first time (§5).
3. **The first stock count and the opening balances** from the credit book and supplier invoices.
4. **Changing habits:** staff who have never used a till system, and an owner who has never had reports (§11).

Expect the catalogue build and the training to take longer than any configuration.

Follows the pattern of [WATER_REFILL_STATION_ONBOARDING.md](WATER_REFILL_STATION_ONBOARDING.md). Its sections on M-Pesa (§8),
cash-up (§8.1), customers and credit (§6), customer insights and the monthly-spend reward (§6.3), expenses (§7) and the
reconciliation rules (§4, §5) apply unchanged and are referenced rather than repeated. What a minimart needs that the API does not do
yet is in [§13](#13-gaps-found-for-this-onboarding).

All routes are under `/api/v1`. Enums are sent as numbers.

> **Status of facts.** Legal and tax points in §12 were checked against public sources in October 2026, and several are moving
> (the VAT threshold, zero-rated vs exempt staples). Each is marked **confirm** where the owner's accountant or county must give the
> final answer. Nothing here is a tax ruling.

---

## 1. Onboarding plan at a glance

| Phase | When | What | Done when |
|---|---|---|---|
| **0. Discovery** | Week 1, day 1–2 | Site visit. Walk the shelves, watch two hours at the counter, see the receipt book, *deni* book and supplier invoices. Measure the counter space. Fill the checklist in §2. | Checklist answered or owner-dated |
| **1. Site readiness** | Week 1–2 | Buy and install hardware (§1.1), power backup, internet. Confirm the M-Pesa till/paybill. | A test sale prints a receipt on the counter |
| **2. Catalogue build** | Week 1–3 | Shelf by shelf: scan each barcode, record name, size, category, unit, carton size, buying price (from supplier invoices), selling price (from the owner). Items without a barcode get a quick-key. (§5) | Every shelf section ticked off; owner signs off prices |
| **3. Configure** | Week 2 | Register the business (§4), roles and users (§10), expense categories, units. Tax class per category with the accountant. | Test sale end-to-end on the test environment |
| **4. Load** | Week 2–3 | Upload categories and products from the build sheet; add barcodes and carton conversions; suppliers; credit customers (§5). | Spot check: 50 random items scan at the right price |
| **5. Train** | Week 2–3, on the real counter | Basics first (§11): scanning, cash with change, M-Pesa. Then credit, returns, receiving, end of day. | Each cashier rings 30 practice sales without help |
| **6. Go-live** | After closing the night before | Full stock count, opening stock with cost, credit-book balances, supplier balances (§5.4, §5.5). | Stock value matches the count sheet; *deni* totals match |
| **7. Hand-holding** | Week 1–2 after go-live | Someone from our team at the counter at least the first 3 days, then daily calls. Daily: cash variance, items below zero, unknown barcodes, price errors. | Five days in a row with no catalogue fixes and no help needed |
| **8. Review** | Day 30 | The owner's first month of real figures: profit, top sellers, slow stock, losses, who owes. Decide on the overselling setting and the monthly-spend reward. | Owner meeting held |

**No parallel paper system.** Once live, every sale goes through the till. Keep the receipt book only as a fallback for a power or
system outage (§1.1), and enter those sales the same day.

### 1.1 Site readiness (the shop has no equipment yet)

| Item | Notes |
|---|---|
| Till computer or tablet | One per counter. A tablet or a small PC both work with a web frontend; confirm what our frontend supports |
| Barcode scanner | USB or Bluetooth, 1D/2D. One per counter, plus one handheld or phone for the catalogue build and stock counts |
| Receipt printer | 80 mm thermal is standard; stock receipt rolls (expense `OFF`) |
| Cash drawer | Opened by the printer |
| Weighing scale | Only if goods are sold by weight; verified and stamped by the county (§12.5) |
| **Power backup** | A UPS for the till, printer and router at least. Power cuts are common; a till that goes off loses sales and trust in the system |
| **Internet** | The API is online. A Wi-Fi router with a Safaricom/Airtel data line, plus a phone hotspot as backup. There is **no offline till mode** (gap M13). Agree what happens in an outage: write sales in the receipt book and enter them when back |
| Phone for M-Pesa | The till/paybill phone that receives the confirmation SMS |

Buy the hardware before the catalogue build: the scanner is needed to capture barcodes.

---

## 2. Owner checklist

Nothing below is invented. Where a value is not known, leave it out; do not enter a placeholder.

| # | Item | Needed for | Status |
|---|------|-----------|--------|
| 1 | Legal and trading name, KRA PIN, address, county, phone | Registration, receipts | ☐ Owner |
| 2 | **VAT registered?** If not, on Turnover Tax? | `isVatRegistered`; reports for the accountant | ☐ Accountant |
| 3 | **eTIMS: how are invoices issued today?** (eTIMS app/portal, by the accountant, not at all) | Compliance (§12.1). The API does not send invoices to KRA yet (gap M1) | ☐ Owner / accountant |
| 4 | M-Pesa till and/or paybill | `mpesaTillNumber`, `mpesaPaybillNumber` | ☐ Owner |
| 5 | **The *deni* (credit) book, supplier invoices and any price list** the owner keeps | Opening balances, buying prices (§5) | ☐ Owner |
| 6 | Number of counters, branches, and whether stock is held in a back store | Hardware, branches, roles | ☐ Owner |
| 6a | **Budget for hardware**, power backup and internet; who buys it | Site readiness (§1.1) | ☐ Owner |
| 6b | Staff comfort with phones and computers (do they use M-Pesa, smartphones?) | Training plan (§11) | ☐ Site visit |
| 7 | Staff and their duties (cashier, shelf/store, manager), shifts per day | Users, roles, cash-up (§9, §10) | ☐ Owner |
| 8 | Hardware: barcode scanners, receipt printers, cash drawers, **weighing scale** (make, model, prints barcodes?) | Till setup, weighed items (§6.3) | ☐ Site visit |
| 9 | Items sold by weight or loose (sugar, rice, beans, produce, meat) | Units and quantities (§6.3) | ☐ Owner |
| 10 | Items with expiry that must be tracked (dairy, baby food, cooked foods, OTC remedies) | `isBatchTracked` (§6.4) | ☐ Owner |
| 11 | Alcohol, cigarettes, LPG, OTC medicines sold? | Licences, restricted items (§12) | ☐ Owner |
| 12 | Who buys on credit (deni), their limits and current balances | Customers, opening balances | ☐ Owner |
| 13 | Suppliers: distributors and van sales, their terms, balances owed, and **which take back expired or stale goods** | Suppliers, returns (gap M4) | ☐ Owner |
| 14 | Wholesale price for bulk buyers (e.g. kiosks buying cartons)? | Wholesale price tier | ☐ Owner (optional) |
| 15 | Promotions today (bundles, "buy 2", happy hour, loyalty card)? | Bundles, monthly reward, gap M7 | ☐ Owner |
| 16 | Maximum discount a cashier may give | `maxDiscountPercent` | ☐ Owner |
| 17 | Overselling: block a sale when the system shows no stock? | `allowNegativeStock` (§4) | ☐ Owner, after §4 explained |
| 18 | Agency services at the counter (M-Pesa agent, airtime, bill pay)? | Kept **outside** POS sales (§8.4) | ☐ Owner |

---

## 3. Categories in a Kenyan minimart

Categories have one parent level (`parentCategoryId`), so the tree below is two levels: **department → category**. Codes are
suggestions; the bulk upload matches on code. The tax column is the **usual** treatment, as a starting point for the accountant, never
applied without them (§12.2).

| Department (code) | Categories (code) | Typical items | Usual VAT treatment — **confirm** |
|---|---|---|---|
| **Staples & Dry Food** (`DRY`) | Flour (`FLR`), Rice (`RCE`), Sugar (`SUG`), Cooking Oil & Fats (`OIL`), Pulses & Cereals (`PUL`), Pasta & Noodles (`PST`), Salt & Spices (`SPC`) | Unga (maize, wheat), basmati/pishori, sugar 1/2 kg, Kimbo, Elianto, beans, ndengu, spaghetti, Royco, salt | Maize and wheat flour usually zero-rated; rice, sugar and oils mixed: check each |
| **Bakery** (`BKY`) | Bread (`BRD`), Cakes & Buns (`CKE`) | Supaloaf, Festive, Broadways, mandazi | Ordinary bread usually zero-rated; cakes standard |
| **Dairy & Eggs** (`DRY2` or `DAI`) | Milk (`MLK`), Yoghurt & Mala (`YGT`), Butter & Cheese (`BTR`), Eggs (`EGG`) | Brookside, KCC, Fresha, Tuzo, Daima, eggs by tray | Plain milk usually zero-rated; flavoured/yoghurt usually standard; eggs often exempt |
| **Fresh Produce** (`FRS`) | Vegetables (`VEG`), Fruit (`FRT`), Herbs (`HRB`) | Sukuma, tomatoes, onions, potatoes, bananas | Unprocessed produce usually exempt |
| **Meat & Frozen** (`MTF`) | Meat & Poultry (`MEA`), Sausages & Processed (`SSG`), Frozen (`FRZ`) | Farmer's Choice, Kenchic | Fresh meat usually exempt; processed standard |
| **Beverages** (`BEV`) | Soft Drinks (`SFT`), Water (`WTR`), Juice (`JUC`), Tea & Coffee (`TEA`), Energy Drinks (`NRG`) | Coca-Cola range, Keringet, Delmonte, Ketepa, Nescafé, Red Bull | Standard; some also carry excise (already in the supplier's price) |
| **Alcohol** (`ALC`) — only with a liquor licence | Beer (`BER`), Spirits (`SPR`), Wine (`WNE`) | Tusker, KBL, Kenya Cane | Standard + excise; restricted sale (§12.4) |
| **Snacks & Confectionery** (`SNK`) | Biscuits (`BSC`), Crisps (`CRS`), Sweets & Gum (`SWT`), Chocolate (`CHC`) | Nuvita, Tropical, Big G, Cadbury | Standard |
| **Breakfast & Spreads** (`BRK`) | Cereals & Porridge (`CER`), Spreads & Jam (`SPD`), Honey (`HNY`) | Weetabix, Jungle Oats, Blue Band, Zesta | Mostly standard |
| **Baby Care** (`BBY`) | Diapers (`DPR`), Baby Food (`BFD`), Baby Toiletries (`BTL`) | Pampers, Softcare, Cerelac, Nan | Mixed: some infant foods zero-rated or exempt |
| **Personal Care** (`PER`) | Soap & Body (`SOP`), Oral Care (`ORL`), Hair (`HAR`), Sanitary (`SAN`), Deodorants & Lotions (`DEO`) | Geisha, Colgate, Dark & Lovely, Always, Nivea | Standard; sanitary towels often exempt or zero-rated |
| **Household & Cleaning** (`HSE`) | Laundry (`LND`), Cleaning (`CLN`), Tissue (`TIS`), Insecticides (`INS`), Kitchenware (`KIT`) | Omo, Ariel, Jik, Harpic, Rosy, Doom | Standard |
| **Health & OTC** (`OTC`) — only general-sale items | Pain & Cold (`PCL`), First Aid (`FAD`), Contraceptives (`CNT`) | Panadol, Strepsils, plasters | Check each; non-pharmacies may only sell general-sale medicines (§12.4) |
| **Gas & Energy** (`GAS`) | LPG Refill (`LPR`), LPG Cylinders (`LPC`), Charcoal & Paraffin (`CHR`), Batteries & Bulbs (`BAT`) | 6 kg / 13 kg refills, cylinders, Eveready | LPG treatment has changed in recent Finance Acts: **confirm** |
| **Tobacco** (`TOB`) — if sold | Cigarettes (`CIG`) | — | Standard + excise; restricted (§12.4) |
| **Stationery & Airtime-free items** (`STN`) | School & Office (`SCH`) | Exercise books, pens | Exercise books often exempt or zero-rated |
| **Bags & Packaging** (`BAG`) | Carrier Bags (`CRB`) | Non-woven carrier bags | Standard. Plastic carrier bags are banned in Kenya |
| **Pet & Garden** (`PET`) — optional | Pet Food (`PTF`) | — | Standard |
| **Services** (`SVC`) — optional | Delivery Fee (`DLV`), Photocopy/Printing (`PRT`) | — | Standard |

Create departments first, then categories with `parentCategoryId`
(`POST /categories/create-category { name, code, parentCategoryId }`), or use the category bulk upload. Leave
departments the shop does not stock out; it is easier to add one later than to empty one. Getting the categories right **now**,
while nothing is in the system, is what makes the owner's reports useful later.

---

## 4. Business configuration

| Setting | Value | Notes |
|---|---|---|
| **Sector** | **`Minimart` (4)** | Modules: core + `batches` + `bundles`, plus `cashUp`. No production, containers or prescriptions. Adds `doz` and `crate` units to the defaults |
| Business type | `Retailer` (1) | Use wholesale price tiers for bulk buyers rather than changing the type |
| Kind | `Trader` (1) | Suggested by the sector |
| **Allow negative stock** | **`true` (sector default) for the first month** | See below |
| VAT, till, paybill, discount cap | From the checklist | |

**Overselling.** A minimart counting its stock for the first time, with thousands of items, will have wrong balances in week one. With overselling
off, a cashier holding a real packet of sugar cannot sell it if the system says zero, and the queue stops. Leave it **on** for the
first month, and every day list items that went below zero: each one is a receiving, count or barcode mistake to fix. Review at
day 30. A shop with good receiving discipline may then switch it off.

Register (super user):

```http
POST /businesses
{ "businessName": "<legal>", "tradingName": "<trading>", "sector": 4, "businessType": 1,
  "kraPin": "<PIN>", "physicalAddress": "<address>", "county": "<county>", "phoneNumber": "<phone>",
  "isVatRegistered": <accountant>, "mpesaTillNumber": "<till>", "mpesaPaybillNumber": "<paybill or null>",
  "maxDiscountPercent": <owner or null>,
  "owner": { "fullName": "<owner>", "email": "<email>", "phoneNumber": "<phone>" } }
```

Add the expense categories a minimart uses that the defaults lack:

```http
POST /expense-categories { "code": "PKG", "name": "Packaging & Carrier Bags", "description": "Carrier bags and wrapping used at the till" }
POST /expense-categories { "code": "SHR", "name": "Stock Write-offs", "description": "Only if the accountant wants write-offs as an expense; otherwise adjustments carry them" }  -- confirm with accountant
POST /expense-categories { "code": "REF", "name": "Refrigeration", "description": "Fridge and freezer servicing and gas" }  -- optional, else REP
```

Add units the shop sells in: `POST /units-of-measure/create-unit` for **Tray** (`tray`, eggs ×30), **Carton** (`ctn`), **Bale**
(`bale`, e.g. 12 × 2 kg flour), **Outer** (`outer`), **Bunch** (`bunch`).

---

## 5. Building the catalogue from the shelves

There is no product list anywhere but on the shelves and in the owner's head. We build it.

### 5.1 The build sheet

One spreadsheet row per item, filled **at the shelf**, department by department (§3), by two people: one scans and reads, one types.
A phone or handheld scanner into a spreadsheet on a laptop is enough.

| Column | Where it comes from |
|---|---|
| Barcode | Scanned from the pack (blank if none) |
| Name | As printed: brand, variant, size ("Omo Multi-active 500 g") |
| Brand | The brand on the pack (Soko, Pembe, Omo). Blank for unbranded goods (produce, posho-mill flour) |
| Department / category code | §3 |
| Base unit | `pcs`, or `kg`/`l` for loose goods |
| Carton or crate size, if bought or sold that way | Read from the outer or the supplier invoice |
| Buying price | Latest supplier invoice; ask the owner where there is none |
| Retail price (and wholesale, if any) | The owner. **Many items will have no fixed price yet**: this is the moment to set one |
| Tax class | Accountant, by category (§12.2) |
| Reorder level | The owner's rough "when I'm down to …" |
| Expiry tracked? | §6.4 |

A shelf section is ticked off when every item on it is a row. Expect **1–2 days per 500 items**. The owner must review prices: a
price typed wrong is charged on every sale until someone notices.

### 5.2 Loading it

`GET /products/bulk-upload/template`, then `POST /products/bulk-upload`. The template columns are:
**Name, Category Code, Item Type, Tax Classification, Unit Symbol, Reorder Level, Wholesale Price, Retail Price, Description,
Brand.** A brand named in the file that does not exist yet is created with the products.

It does **not** take barcode, cost, opening stock or carton conversions (gap M2). Until M2 is built:

1. Bulk upload the products from the build sheet.
2. Add barcodes with `PUT /products/{id}`. For hundreds of items this is a script we run against the API from the build sheet, not
   something staff type.
3. Add carton conversions (`POST /products/{id}/unit-conversions`) for items bought or sold by carton, crate, tray or bale.
4. Opening stock with cost via `POST /stock/adjustments` (§5.5), also scripted.

**Build M2 before this client** if the catalogue is over ~500 items. Every new client without a POS needs the same build sheet, so an
upload that takes it whole pays for itself.

### 5.3 Rules for the build

- **One product per size and variant.** Coca-Cola 500 ml and 1.25 L are two products with two barcodes. (Unlike water at the refill
  station, they are not one stock in different sizes.)
- **Category says what it is, brand says whose.** Soko, Pembe and Jogoo 2 kg are three products in category *Maize Flour*, each with its
  brand. Never make a category per brand ("Soko Products"): the owner wants to see how much maize flour sells, across brands. Keep the
  brand in the product name too, since the name is what prints on the receipt. Client guide: [Minimart Demo Guide](https://claude.ai/artifact/UMMrpUyUz5Gu3kP3pmX56S).
- **A carton is a pack of the piece, not a product.** "Omo 500 g" is the product in `pcs`; a carton of 24 is a unit conversion
  (`ctn`, 24), with its own carton price if the shop sells cartons.
- **Same barcode twice** is refused (barcodes are unique per business). It usually means the same item was captured on two shelves.
- **Items without a barcode** (produce, loaves from small bakers, loose items): keep the SKU the system generates and put them on the
  till's quick-key panel (§6.2).
- **Prices are VAT-inclusive** shelf prices if the shop is VAT-registered.
- New items that arrive after go-live are added by the manager the day they arrive, before they reach the shelf.

### 5.4 Customers, suppliers and what is owed

- **Credit customers from the *deni* book:** `POST /customers` (name, phone, credit limit). Agree each balance with the owner; old
  balances nobody expects to collect are written off by the owner, not loaded.
- **Suppliers** from the invoices and the vans that call: `POST /suppliers` with payment terms.
- **Opening balances.** There is no opening-balance entry for customer or supplier accounts (gap M10). Until it exists, record what
  a customer owes as a **credit sale of a non-stock "Opening balance" service item** dated go-live, and what is owed to a supplier
  as a purchase order of a non-stock item received at the balance. Both keep the ledgers correct; label them clearly. Prefer
  building M10 if the *deni* book has more than a handful of names.
- From go-live, the *deni* book is closed. Every credit sale is on the till, with the customer named.

### 5.5 Go-live night

1. Close the shop. The last day's sales are totalled from the receipt book as usual.
2. **Full count**, shelf by shelf, two-person teams, using a count sheet printed from the catalogue, by department. Anything on the
   shelf that is not on the sheet is added now.
3. Load opening stock with **unit cost** (`POST /stock/adjustments { …, reason: "Opening stock", unitCost }`). Stock with no cost makes
   every margin wrong (`isComplete: false` on profit).
4. Batch-tracked items: opening stock per batch with its expiry.
5. Check: `GET /reports/inventory/stock-status` total value against the count sheet total.
6. Open the first cash session with the float (`POST /cash-sessions/open`).

---

## 6. Products, pricing and the till

### 6.1 Barcodes and scanning

The till scans with `GET /products/lookup?code=` (exact barcode or SKU; inactive items are found too, so the till can say why it
won't sell). Product search also matches barcodes. **A barcode belongs to the product, not to a pack**: scanning a carton's own
barcode finds nothing unless it is the product's barcode (gap M3). Until M3, cashiers pick the carton unit after scanning a piece.

**Unknown barcode at the till:** the cashier should not invent an item. Hold the item, sell it by search if it exists without a
barcode, and pass the barcode to the manager, who adds it. Count these daily in week 1.

### 6.2 Fast items without barcodes

Bread, eggs, produce, single sweets and carrier bags need a **quick-key panel** at the till (frontend: a configurable grid of
products). No API change: each key is a product + unit.

### 6.3 Weighed and loose items

- Hold the product in `kg` and sell decimal quantities (0.75 kg of sugar). Price per kg is the retail tier on `kg`.
- Pre-packed by the shop (½ kg sugar bags): make **"Sugar 500 g (shop-packed)"** a product only if packing is a separate job; otherwise
  sell 0.5 kg of the loose product.
- **Scales:** there is no scale integration and no reading of price-embedded barcodes (the `2x` EAN-13 labels that label scales print)
  (gap M5). Until then the cashier types the weight shown on the scale. Scales used for trade must be verified (§12.5).

### 6.4 Expiry and batches

Set `isBatchTracked: true` only where expiry is actually managed: dairy, baby food, OTC remedies, processed meats. The batch number
and expiry are then required when receiving and issued first-expiry-first-out. `GET /stock/expiry-report` and the near-expiry card
show what to discount or return. **Bread is better not batch-tracked:** it turns over daily and the van takes stale bread back; track
it through returns to supplier instead (gap M4).

### 6.5 Prices, wholesale and promotions

- Retail and wholesale tiers per unit (`POST /products/{id}/price-tiers`). A price change is a new tier with a later `effectiveFrom`;
  past sales keep their price. Price changes from suppliers show in `GET /reports/purchases/price-trends`.
- **Bundles** (`bundles` module) for fixed combos: "Unga 2 kg + Elianto 1 L = 450", "Back-to-school pack". Each part leaves its own
  stock; revenue is split per product (water station §3.5).
- **Monthly-spend reward and standing discounts** as at the water station (§6.3 there).
- **Not available:** time-limited promotions ("10% off Omo this weekend"), buy-X-get-Y, and a price that ends automatically (gap M7).
  Today a promotion is a new price tier and a manual one back after it ends.
- **Shelf labels:** no label printing (gap M9).

### 6.6 Payments at the till

Cash with change, M-Pesa till (matched), STK push, M-Pesa code typed from the SMS (`MpesaReference`, reconcile against the
statement), and credit for registered customers. **Split payment** ("500 cash, rest M-Pesa") is common at a minimart and not
supported (gap G2): raise its priority for this client.

### 6.7 Returns, voids, carrier bags

- Customer returns: `POST /sales/{id}/returns` with a reason; restock or not per line; refund in cash, M-Pesa or credit note.
- Voids are a manager action (`Sales.Void`).
- Carrier bags are a product (category `CRB`) rung up like any item, so they are counted and costed.

---

## 7. Purchasing and receiving

How minimarts buy in Kenya: **distributor van sales** paid cash on delivery (soft drinks, bread, milk, snacks), **distributors on
credit** with 7–30 day terms (FMCG), **cash-and-carry or wholesaler trips** by the owner, and the occasional supplier who issues
no eTIMS invoice.

| Situation | How |
|---|---|
| Ordered goods | `POST /purchase-orders`, then `POST /purchase-orders/{id}/receive` with the delivery note number |
| Van sale, paid on the spot | Create and receive the order at once, then `POST /suppliers/{id}/payments` in cash. Two calls (gap G8 makes it one) |
| Owner's wholesale trip | Same as a van sale, supplier "Cash & Carry – <name>". Transport is an expense (`TRN`), not added to cost |
| Short delivery | Receive what came; close-short is gap G7 |
| Stale bread, expired milk taken back by the van | **No supplier return yet** (gap M4). Today: a stock adjustment out with reason "Returned to supplier <name>", plus a supplier ledger correction. Both by the manager |
| Paying a supplier | "Pay supplier", never "Record expense" (water station §4) |
| Supplier's eTIMS invoice | Record its number on the receipt (`supplierInvoiceNumber`). Purchases without one may not be deductible (§12.1) |

Reorder levels raise *below reorder* and *out of stock* alerts; the stock-status report shows days of cover. Reorder suggestions
by supplier (G13) are worth building for a minimart: the storekeeper orders from 15+ suppliers.

---

## 8. Inventory, shrinkage and daily operations

### 8.1 A minimart's day

| Time | Who | In the system |
|---|---|---|
| Opening | Manager | Open cash session with float; check alerts (low stock, near expiry, overdue promises) |
| Morning | Storekeeper | Receive bread and milk vans; return stale/expired; shelf fill |
| Through the day | Cashiers | Sales; pay-ins/outs on the session; register new credit customers |
| Shift change | Cashier → cashier | **Cash sessions are per branch, not per cashier** (gap G10/M6). Until then: one session per day, `byCashier` on the sales report to see each cashier's takings |
| Afternoon | Manager | Spot counts of one department; near-expiry markdowns |
| Closing | Manager | Count drawer, close session (variance alerts); M-Pesa check against the statement |
| Weekly | Owner | Sales, profit, slow movers, supplier balances, customer ageing |
| Monthly | Owner + accountant | Full or rotating stock count; VAT/TOT figures; eTIMS checks |

### 8.2 Shrinkage

Minimart losses come from theft, breakage, expiry, spoilage of produce and staff consumption. Record each as a stock adjustment with
a **specific** reason ("Expired – Brookside 500 ml", "Breakage", "Spoilage – produce", "Staff use"), and keep `Stock.Adjust` with
the manager only. `GET /reports/inventory/adjustments` is the shrinkage report. A stock-take session that freezes the counted
quantities and posts the differences in one go would help a shop this size (gap M8).

### 8.3 Stock counts

Rotating counts: one department per week, full count each quarter. Count, compare with `GET /stock/{productId}`, adjust with reason
"Stock count".

### 8.4 What is not a POS sale

M-Pesa agent deposits and withdrawals, airtime float, bill payments and parcel services are **agency business**, not stock sales.
Do not ring them through the till: they would inflate revenue and break the cash-up. Keep them in the agent's own book. Record only
the **commission** as income if the owner wants it in the POS (a service item), and the cash moved for agency use as a session
pay-in or pay-out with a reason.

---

## 9. Dashboard and reports a minimart owner asks for

| Question | Source today |
|---|---|
| Today's takings, by cash/M-Pesa/credit and by cashier | `GET /reports/sales` → `byPaymentMethod`, `byCashier` |
| Top sellers | `GET /reports/sales` → `topProducts` |
| Gross profit and net operating profit | `GET /dashboard/profit` |
| **Profit and sales by department/category** | **Not available** (gap M11). The stock-status report carries the category; sales do not group by it |
| Slow and dead stock | `GET /reports/inventory/stock-status` (average daily usage, days of cover) |
| Low and out of stock | `GET /dashboard/stock`, alerts |
| Near expiry | `GET /stock/expiry-report` |
| Shrinkage | `GET /reports/inventory/adjustments` |
| Who owes the shop | `GET /reports/customer-aging` |
| What the shop owes | `GET /suppliers/balances` |
| Supplier price rises | `GET /reports/purchases/price-trends` |
| Cash variance | `GET /cash-sessions/{id}` |
| Best customers, who stopped coming | `GET /customers/insights` |

---

## 10. Users and roles

As at the water station (§9 there), minus deliveries and containers. Add a **Shelf/Store** role for staff who receive, count and
adjust only with approval from the manager in person (no approval workflow: gap G11).

| Role | Permissions |
|---|---|
| Owner | Admin |
| Manager | Sales.*, Customers.*, Suppliers.*, PurchaseOrders.*, Stock.View, Stock.Adjust, Expenses.*, Costing.View, reports, Sales.ApplyDiscount, Sales.Void, Sales.OverrideCreditLimit, CashSessions.*, Products.* (prices, barcodes) |
| Cashier | Sales.Create, Sales.View, Customers.View, Customers.Register, Customers.RecordPayment, Products.View, Stock.View, Mpesa.View, CashSessions.View, .RecordMovement |
| Storekeeper | Suppliers.View, PurchaseOrders.View, PurchaseOrders.Receive, Stock.View, Products.View |
| Accountant | As at the water station |

Products.ManagePricing belongs to the manager only: a cashier who can change a price can sell at any price.

---

## 11. Training checklist

Nobody here has used a till system. Train on the real counter, with real products, in short sessions, in English or Swahili as the
staff prefer. Start with the three things done a hundred times a day (scan, cash with change, M-Pesa), and add the rest only once
those are automatic. Give each cashier a one-page card at the till (how to sell, M-Pesa code, what to do with an unknown barcode, who
to call). The owner also needs teaching: **reading reports is new to them**. Go through the daily and weekly figures with them in the
first two weeks (§9), not only at day 30.

| Who | Must be able to |
|---|---|
| Cashier | Scan; sell a weighed item; choose carton vs piece; take cash with change, till payment, M-Pesa code; credit sale with a promise date; register a customer; return an item; handle "not enough stock" and "unknown barcode"; record a pay-out |
| Storekeeper | Receive a PO with delivery note and batch/expiry; van sale in two steps; record breakage/expiry with the right reason |
| Manager | Open/close the day; void; give a discount; add a product with barcode and carton conversion; change a price; read the daily reports; set a customer's credit limit |
| Owner | Profit, ageing, supplier balances, customer insights, monthly reward |

---

## 12. Kenyan compliance a minimart must meet

### 12.1 eTIMS (KRA)

- Every person carrying on business, VAT-registered or not, must issue **electronic tax invoices through eTIMS** (Tax Procedures Act
  and the Electronic Tax Invoice Regulations, 2024).
- Since 2024, expenses not supported by an eTIMS invoice are not deductible for income tax, and from 1 January 2026 KRA validates
  income tax returns against eTIMS data. A shop's **purchases** therefore need suppliers' eTIMS invoices, and its **sales** must be
  invoiced through eTIMS.
- Integration options: OSCU (online) or VSCU (bulk / not always online), through self-integration after KRA certification or a
  certified third-party integrator.
- **Ultimate POS does not transmit sales to eTIMS** (`Infrastructure/Etims` is empty). Branch numbers already follow eTIMS
  (00, 01, …) and expenses record the payee's eTIMS control number. This is **gap M1**. A shop with no POS is often not issuing
  eTIMS invoices at all, or only for customers who ask, from KRA's eTIMS app or portal (checklist #3). Going live on the POS without
  M1 does not make that worse, but it does not fix it either. Options: (a) build M1, so every till sale is an eTIMS invoice; (b) keep
  issuing from KRA's own tools for customers who ask, confirmed with the accountant. Raise this with the owner at discovery:
  moving to a POS is the natural time to become compliant.

### 12.2 VAT and Turnover Tax

- VAT registration at taxable turnover above **KES 5 million** a year (Finance Act 2025 proposals to change this were not enacted;
  KRA has floated removing the threshold: **confirm** at go-live).
- Standard rate **16%**. Many staples are zero-rated or exempt, and items have moved between the two lists in recent Finance Acts
  (§3 table): the accountant sets `taxClassification` per product before import. The product's class is what prints on the receipt.
- A shop below the threshold usually pays **Turnover Tax at 3%** of gross sales (turnover between KES 1 million and 25 million):
  **confirm**. `GET /reports/sales` gives the gross figure.
- Excise duty on drinks, alcohol and tobacco is in the supplier's price; the shop does not charge it separately.

### 12.3 County and national licences

| Licence | From | Notes |
|---|---|---|
| Single / Unified Business Permit | County | Annual; fee by size and location |
| Public health / food hygiene certificate, staff medical certificates | County health | Needed for food handling |
| Fire safety certificate | County | |
| Liquor licence | County liquor board | Only if alcohol is sold (§12.4) |
| Weights and measures verification | County W&M office | Annual stamping of every scale used for trade (§12.5) |
| Advertising/signage permit | County | Signboards |

Use the licence-expiry reminders proposed in gap G6 (record name, number, expiry) for these; the same module serves the minimart.

### 12.4 Restricted items

- **Alcohol:** licensed hours and no sale to under-18s. **Tobacco:** no sale to under-18s, and Kenyan tobacco rules restrict how
  cigarettes are displayed and sold (single sticks): **confirm** with the county. The till has no age-check prompt (gap M12).
- **Medicines:** a shop that is not a pharmacy may sell only general-sale (over-the-counter) medicines. Do not put prescription-only
  items in the catalogue; the minimart has no `prescriptions` module.
- **Plastic carrier bags** are banned; sell non-woven bags.

### 12.5 Weights and measures

Scales used to sell by weight must be verified and stamped yearly by the county (Weights and Measures Act, Cap 513). Record the
certificate with the licences.

### 12.6 Customer data

Customers' names and phone numbers are personal data under the Data Protection Act, 2019. The shop should register with the Office
of the Data Protection Commissioner if its size requires it (**confirm**), collect only what it uses, and never use the list for
marketing SMS without consent.

---

## 13. Gaps found for this onboarding

Existing gaps whose priority this client raises: **G2** split payment (common at minimart tills), **G7** close a PO short, **G8**
receive without an order (van sales), **G10** per-cashier cash-up, **G11** approvals, **G13** reorder suggestions, **G6** licence
reminders. New ones:

| # | Gap | Priority for this client |
|---|---|---|
| **M1** | **eTIMS sales invoicing** (OSCU/VSCU), invoice number and QR on the receipt, retry queue when offline | High: compliance, the owner's decision (§12.1) |
| **M2** | Bulk product upload with barcode, cost, opening quantity and carton conversions, taking the build sheet (§5.1) whole | **High: every client without a POS** |
| M3 | Barcode per pack (carton/crate barcode on the unit conversion), scanned to that unit | High |
| M4 | Return to supplier / supplier credit note: stock out, supplier balance down, one document | High: bread, milk and expired goods |
| M5 | Scale integration and price/weight-embedded barcodes (EAN-13 `2x` prefix) | Medium: depends on the shop's scales |
| M6 | Shifts: cash session per cashier or per till, mid-day handover | Medium (extends G10) |
| M7 | Time-limited promotions (start/end, % or fixed price), buy-X-get-Y | Medium |
| M8 | Stock-take sessions: count sheets per department, freeze, post differences once | Medium |
| M9 | Shelf-label and barcode-label printing | Low–medium |
| M10 | Opening balances for customer and supplier accounts | High for go-live if the *deni* book is long |
| M11 | Sales, margin and stock by department/category | High: the first report an owner asks for |
| M12 | Age-restricted product flag with a till confirmation | Low (training covers it meanwhile) |
| M13 | Offline till: keep selling through an internet outage and sync after | Medium–high where the connection is unreliable (§1.1) |
| M14 | Printable count sheet per department from the catalogue | Medium: go-live count and every later count (part of M8) |

Each needs a short contract proposal in [BACKEND_API_GAPS.md](BACKEND_API_GAPS.md) before work starts. Recommended order before
go-live: **M2**, **M10**, **M11**, **M3**; then M1 (owner's decision), G2, M4, G8, M13.

---

## Sources (checked October 2026)

- KRA eTIMS: [eTIMS taxpayer guidelines 2024](https://www.kra.go.ke/images/publications/eTIMS-Taxpayer-Guidelines_2024.pdf),
  [eTIMS portal](https://etims.kra.go.ke/main/signup/indexLearnMore), [KPMG on eTIMS validation 2026](https://assets.kpmg.com/content/dam/kpmgsites/ke/pdf/thought_leaderships/tax/2026/eTIMS.pdf.coredownload.inline.pdf),
  [EY on non-VAT onboarding](https://taxnews.ey.com/news/2023-1897), [CM Advocates](https://cmadvocates.com/blog/demystifying-etims-why-every-business-in-kenya-should-pay-attention/)
- VAT and TOT: [PwC tax summaries](https://taxsummaries.pwc.com/Kenya/Corporate/Other-taxes), [Avalara](https://www.avalara.com/vatlive/en/country-guides/africa-and-middle-east/kenya/kenyan-vat-rates.html),
  [Kenyans.co.ke on the threshold proposal](https://www.kenyans.co.ke/news/121905-kra-targets-removal-vat-threshold-all-businesses),
  [Business Daily on staples](https://www.businessdailyafrica.com/bd/economy/mbadi-spares-milk-bread-tycoons-in-new-tax-plan-4814726)
- Licences: [Kenyans.co.ke licence list](https://www.kenyans.co.ke/news/70721-list-licenses-required-set-business-nairobi), [Laikipia County](https://laikipia.go.ke/starting-a-business/)
- Weights and measures: [Makueni County brochure](https://makueni.go.ke/sandbox/site/files/2024/06/Weights-and-Measures-Brochure-Updated-2-2-1.pdf),
  [Nairobi County notice](https://www.standardmedia.co.ke/counties/article/2001500826/nairobi-county-notice-of-weights-and-measures)
