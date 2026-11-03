# Demo accounts: a general hardware and an electrical shop (Kenya)

How to build two convincing demo businesses in Ultimate POS and walk a prospect through them:

- **Imara Hardware & Building Supplies (Demo)**: a general hardware selling cement, steel, roofing, timber, plumbing, paint, tools
  and fasteners to walk-in self-builders, fundis and contractors.
- **Mwangaza Electricals & Solar (Demo)**: an electrical shop selling cable by the metre and the roll, wiring accessories,
  breakers, lighting, solar and small appliances to electricians, solar installers and homeowners.

The catalogues are ready to upload (§3.4): [docs/demo-data/](demo-data/). §1 explains how these shops work, so the demo follows their
day. Code changes, backend and frontend, are in §6. Some of them (marked **Tier 0**) should be made **before** the demo.

> **Prices are demo figures.** They are set from public 2025–2026 Kenyan price reports and the margins that hardware guides give
> (see Sources). They are close enough to look right to a shop owner, but not a price list. Before each demo, ask the prospect for
> 10 to 15 of their own prices and change those products (§3.10). An owner who sees their own cement price stops checking the
> numbers and starts paying attention to the system.

---

## 1. How these shops run

### 1.1 A general hardware

| Topic | How it works | What the demo must show |
|---|---|---|
| **Customers** | Walk-in self-builders building in stages; **fundis** (masons, carpenters, plumbers) buying for a client; **contractors** buying in bulk, often on credit; institutions (schools, churches, county offices) buying on an **LPO**. | Retail and contractor prices, credit with a limit and a promised date, customer KRA PIN on the invoice |
| **How goods are sold** | Cement by the 50 kg bag; steel by the 12 m bar (Y8–Y16); iron sheets by the sheet, by gauge (G30, G28), profile and length; nails and binding wire **by the kg** and by the 25 kg bag/roll; timber **by the foot**; sand, ballast and hardcore **by the tonne** or the lorry; pipes by the length; cable by the metre. | Decimal quantities (2.5 kg of nails), two units on one product (kg and bag), many sizes of one item |
| **Margins** | Cement and steel are sold close to cost to bring customers in: about 2–6% on cement and 5–10% on steel. The money is in the rest: roofing 8–15%, paint 15–25%, plumbing and electrical 20–40%, nails and small hardware 30–50%. | The **margin report**: the owner sees that cement brings people in and fittings make the profit |
| **Quotations** | A self-builder or fundi brings a materials list (a "BOQ") and asks for a price before buying, often comparing three shops. | **Not in the system yet** (§6, Q1). Say it is coming; do not pretend |
| **Credit** | Contractors and known fundis buy on account and pay on completion or monthly, by M-Pesa, cheque or bank transfer. The shop keeps a credit book. Suppliers give the shop 30–60 days once it has a payment record. | Credit sale, customer balance, ageing, customer payment by cheque, supplier balances |
| **Delivery** | Heavy goods (cement, steel, sheets, ballast) go to site on the shop's pickup or a hired lorry. Free delivery on big orders is a selling point. | Delivery order with a delivery note, and payment on delivery |
| **Suppliers** | Cement from manufacturer depots and distributors; steel and roofing from mills and their distributors; plumbing and electrical from wholesalers in Nairobi's Industrial Area. | Purchase orders, receiving with the supplier's invoice number, what the shop owes |
| **Losses** | Bags short on delivery, cement that sets in the rain, broken fittings, staff "borrowing" and miscounted nails sold loose. | Stock adjustments with reasons, the adjustments report, a spot count |
| **Compliance** | County business permit; KRA PIN; VAT at KES 5 million turnover; **eTIMS** invoices; scales used to sell by weight must be certified; fire safety for paint and timber. | Mention eTIMS is supported (§5). Do not switch it on in a demo |

**A hardware's day**

| Time | Who | What |
|---|---|---|
| 7:00 | Owner or manager | Opens, counts the float, checks yesterday's credit and what is low |
| Morning | Yard staff | Receive cement and steel lorries, count bags and bars against the delivery note |
| All day | Counter | Fundis and walk-ins: small, fast sales (nails by the kg, fittings, a few bags) |
| All day | Manager | Contractor orders by phone or WhatsApp, quotes, loading the pickup for site deliveries |
| Afternoon | Driver | Delivers to sites; collects payment or a signed delivery note |
| Evening | Owner | Counts cash and M-Pesa, writes up credit, decides what to reorder |

### 1.2 An electrical shop

| Topic | How it works | What the demo must show |
|---|---|---|
| **Customers** | **Electricians** (who bring most of the business and expect a discount); contractors wiring new buildings; solar installers; landlords and property managers; homeowners replacing a socket or bulb. | Standing discount for electricians, contractor (wholesale) price, credit |
| **How goods are sold** | Cable is **cut by the metre from a roll** (100 m, sometimes 90 m) or sold as a whole roll at a lower price per metre. Fittings by the piece; bulbs by the piece or the box; Cat6 by the metre or the 305 m box. | One product, two units: sell 23 m, then sell a full roll at the roll price, and stock goes down in metres for both |
| **Brands and genuine goods** | The same item is sold in a premium brand (MK, Hager, Philips, East African Cables) and an economy brand (Tronic, Chint, Kenwest). **Cables are the most counterfeited goods in Kenya**, and sockets, breakers and extensions are not far behind. KEBS seizes substandard extension sockets regularly. A shop that sells genuine goods sells on that trust. | Brands on products, brand filter, the same item at two prices; purchases from named suppliers with invoice numbers (the trail behind "genuine") |
| **Solar** | Panels, batteries, inverters and controllers sold as a **system**, often with installation by the shop's own technicians. | A solar kit as a bundle (§3.8) |
| **Warranty** | Inverters, batteries, panels, appliances and stabilisers carry 6–24 months of warranty. Faulty items come back; the shop swaps them and returns them to the supplier. | **Serial numbers and warranty are not in the system** (§6, S1). Show returns; say warranty tracking is coming |
| **Quotations** | An electrician sends a list for a house wiring job; the shop prices it, often for the electrician to pass on to the client. | Not in the system yet (§6, Q1) |
| **Kenya Power** | New connections need a ready board or a consumer unit, earthing and meter-side cable. | Ready board, earth rod and armoured cable in the catalogue |
| **Compliance** | As for hardware, plus: sell only KEBS-marked goods (Standards Act, Cap 496). VAT on solar equipment has been changing (zero-rated, then proposals to make it exempt): **confirm** with an accountant before setting a solar product's tax class. | All demo products are Standard rated |

**An electrical shop's day** is like the hardware's, without the yard: electricians come in early before going to site, a lot of
business comes in by phone and WhatsApp, and the afternoon is collecting items for jobs and handling returns.

### 1.3 What wins these owners over

From the research and from our other onboardings, these are the pains a POS removes for them. Build the demo around them:

1. **"Prices are in my head."** Every price, for every unit (kg and bag, metre and roll), and a contractor price list applied by
   itself.
2. **"I don't know who owes me."** Credit with limits, promised dates, overdue alerts and an ageing report.
3. **"Bags go missing."** Received quantities against the supplier's invoice, stock that goes down with every sale, adjustments
   that need a reason and a manager.
4. **"I don't know what makes money."** Margins by product: cement brings people in, fittings pay the rent.
5. **"I can't leave the shop."** Dashboard on the phone, cash-up with variance, notifications.

---

## 2. The two demo businesses

| | Hardware demo | Electrical demo |
|---|---|---|
| Business name | Imara Hardware & Building Supplies (Demo) | Mwangaza Electricals & Solar (Demo) |
| Trading name | Imara Hardware | Mwangaza Electricals |
| Sector | `Hardware` (6) | `Electrical` (7) |
| Business type / kind | Retailer (1) / Trader (1) | Retailer (1) / Trader (1) |
| County / address | Kajiado, Kitengela town | Kiambu, Ruiru town |
| KRA PIN | `P000000001H` (dummy) | `P000000002E` (dummy) |
| VAT registered | Yes (an established shop is over KES 5 million) | Yes |
| M-Pesa till / paybill | A dummy till, e.g. `000111` | `000222` |
| Max cashier discount | 5% | 10% (electricians bargain) |
| Allow negative stock | Yes (sector default) | Yes |
| eTIMS | **Off** | **Off** |
| Licence | Complimentary, 12 months | Complimentary, 12 months |
| Owner login | `demo.hardware@<your-domain>` | `demo.electrical@<your-domain>` |

**Personalise on demo day.** Change the trading name, town and logo to the prospect's (Administration → Business Profile) for the meeting: they
see their own name on the receipt. Change it back afterwards.

**Where to run it.** Use a demo or staging environment, not production. In production, demo sales would show up in the platform
dashboard's revenue and activity figures (§6, D2).

---

## 3. Building each demo, step by step

Allow about **3 hours per demo** the first time, most of it on the opening purchase orders and the practice sales.

**Scripted.** [setup_hardware_demo.py](demo-data/setup_hardware_demo.py) and
[setup_electrical_demo.py](demo-data/setup_electrical_demo.py) do §3.1–§3.8 through the API in about a minute each: business,
complimentary licence, units, categories, products, packs and their prices, suppliers, customers (with price types, credit limits
and the fundi's or electrician's discount), the bundles, and the opening stock as one purchase order per supplier received against an
invoice number, with some suppliers paid. Both describe their business and call [demo_setup.py](demo-data/demo_setup.py). They refuse
a non-local API unless given `--allow-remote`, and skip anything already there, so they can be run again; once the owner has changed
the password, pass it with `--owner-password`. Users (§3.9) and history (§3.11) are still by hand.

| Demo | Owner sign-in (first time) |
|---|---|
| Hardware | `demo.hardware@ultimatepos.local` / `ImaraDemo@2026` |
| Electrical | `demo.electrical@ultimatepos.local` / `MwangazaDemo@2026` |

### 3.1 Register the business (super user)

**Platform → Businesses → Register business**, with the values from §2. The owner gets a temporary password, shown once.

The same through the API:

```http
POST /api/v1/businesses
{ "businessName": "Imara Hardware & Building Supplies (Demo)", "tradingName": "Imara Hardware",
  "sector": 6, "businessType": 1, "kind": 1, "kraPin": "P000000001H",
  "physicalAddress": "Namanga Road, Kitengela", "county": "Kajiado", "phoneNumber": "0700000001",
  "isVatRegistered": true, "mpesaTillNumber": "000111", "maxDiscountPercent": 5,
  "owner": { "fullName": "Demo Owner", "email": "demo.hardware@<your-domain>", "phoneNumber": "0700000001" } }
```

### 3.2 Licence

The business starts on a 14-day trial. Replace it so the demo does not expire between meetings: **Platform → Businesses → (the
business) → Issue licence**, kind **Complimentary**, 12 months, plan "Demo".

### 3.3 Sign in as the owner

Change the temporary password. Upload a logo (Administration → Business Profile). Check **Settings → Point of sale**: paper 80 mm (or A4 for the
invoice look; see §6, F2).

### 3.4 Units, categories and products

1. **Units (hardware only).** The Hardware sector adds `m`, `bag`, `sheet` and `roll` to the standard units. The hardware catalogue
   also needs **Foot** (`ft`, timber) and **Tonne** (`t`, sand and ballast): **Catalog → Units of Measure → New unit** for each. The electrical catalogue
   needs no new units.
2. **Categories.** **Catalog → Bulk Import → Categories**, upload `hardware-categories.xlsx` or `electrical-categories.xlsx`. Run the dry run
   first; it must show no errors.
3. **Products.** **Catalog → Bulk Import → Products**, upload `hardware-products.xlsx` (129 products) or `electrical-products.xlsx` (126
   products). Dry run first.

The upload reads columns A–J only (the API's template). Columns K–Q, shaded yellow, are a **build sheet** the upload ignores: unit
cost, opening quantity, supplier and pack. They drive §3.5 and §3.7.

The files are generated by [build_demo_catalogues.py](demo-data/build_demo_catalogues.py) (needs `openpyxl`). To change a price or
add a product, edit the lists there and run `python docs/demo-data/build_demo_catalogues.py docs/demo-data`.

What the catalogues contain:

| | Hardware | Electrical |
|---|---|---|
| Departments → categories | 8 → 23 | 7 → 20 |
| Products | 129 | 126 |
| Brands | Bamburi, Simba, Rhino, Mabati Rolling Mills, Crown Paints, Basco, Kentank, Ingco, Total, Crocodile, Solex, Tronic, East African Cables | East African Cables, Kenwest Cables, MK, Tronic, Hager, Chint, Philips, Wago, Jinko, Longi, Ramtons, Lorenzetti |
| Opening stock at cost, without VAT | ≈ KES 4.9 million | ≈ KES 4.3 million |
| Contractor (wholesale) price | Cost + 60% of the retail margin, on every product | Same |

### 3.5 Packs

Add each pack on the product: **Products → (product) → Pack units → Add pack unit**, then add its retail and wholesale prices for that
pack on the **Pricing** tab. The prices below are in the build sheet too.

**Hardware**

| Product | Base unit | Pack | Size | Retail | Wholesale |
|---|---|---|---|---|---|
| Binding Wire | kg | roll | 25 | 5,150 | 4,950 |
| Wire Nails 2in, 3in, 4in | kg | bag | 25 | 4,750 | 4,300 |
| Wire Nails 6in | kg | bag | 25 | 5,000 | 4,500 |
| Wood Screw 8 x 1.5in | pcs | box | 100 | 250 | 210 |
| Drywall Screw 1.25in | pcs | box | 100 | 220 | 180 |
| Rawl Plug 6mm | pcs | pkt | 100 | 190 | 150 |
| HDPE Pipe 20mm | m | roll | 100 | 4,200 | 3,700 |
| Single Core Cable 2.5mm | m | roll | 100 | 5,850 | 5,400 |
| Single Core Cable 1.5mm | m | roll | 100 | 4,050 | 3,600 |
| Twin & Earth Cable 2.5mm | m | roll | 100 | 13,500 | 12,600 |

**Electrical** (do at least the ones in bold; the build sheet lists all 29)

| Product | Base unit | Pack | Size | Retail | Wholesale |
|---|---|---|---|---|---|
| **EAC Single Core 1.5mm Red / Black** | m | roll | 100 | 4,050 | 3,600 |
| **EAC Single Core 2.5mm Red / Black / Earth** | m | roll | 100 | 5,850 | 5,400 |
| EAC Single Core 4mm Red / Black | m | roll | 100 | 9,900 | 9,000 |
| EAC Single Core 6mm Red / Black | m | roll | 100 | 14,400 | 13,500 |
| **Kenwest Single Core 2.5mm Red / Black** | m | roll | 100 | 5,400 | 4,950 |
| **Kenwest Twin & Earth 2.5mm** | m | roll | 100 | 13,500 | 12,600 |
| Flex Cable 3-Core 1.5mm | m | roll | 100 | 8,100 | 7,200 |
| **Cat6 Network Cable** | m | box | 305 | 15,000 | 13,750 |
| **Tronic LED Bulb 9W B22** (and 12W, 18W, 9W E27) | pcs | box | 10 | 1,440 | 1,260 |
| Flush Box Single 3x3in | pcs | box | 50 | 1,700 | 1,490 |
| PVC Insulation Tape | pcs | pkt | 10 | 510 | 420 |
| Solar PV Cable 4mm | m | roll | 100 | 10,800 | 9,900 |

A roll is cheaper per metre than cut cable (10% here). That is how these shops price, and it is the moment in the demo where the
owner sees the system "gets" their trade.

### 3.6 Suppliers and customers

**Suppliers** (Purchasing → Suppliers). Names are fictitious on purpose.

| Hardware supplier | Terms | Electrical supplier | Terms |
|---|---|---|---|
| Mavoko Cement Depot | 7 days | Industrial Area Cables Distributors | 30 days |
| Kasarani Steel & Roofing Distributors | 30 days | Nyota Electrical Wholesalers | 30 days |
| Mashuru Quarry & Sand Suppliers | Cash (0) | Jua Power Solar Distributors | 14 days |
| Kitengela Timber Yard | Cash (0) | Home Appliances Distributors Ltd | 30 days |
| Industrial Area Plumbing Wholesalers | 30 days | | |
| Paints & Coatings Stockists Ltd | 30 days | | |
| Tools & Fasteners Wholesalers Ltd | 30 days | | |

**Customers** (Customers → New). Set **price type**, **credit limit** and, where shown, a **standing discount** with its note.

| Hardware customer | Price type | Credit limit | Discount | Why it is in the demo |
|---|---|---|---|---|
| Kamau Builders & Contractors Ltd (KRA PIN, contact person) | Wholesale | 300,000 | — | Contractor on account: bulk order, delivery to site, ageing |
| St. Teresa's Mixed Secondary School | Retail | 150,000 | — | Institution buying on an LPO |
| Baraka Plumbing Works | Wholesale | 80,000 | — | Plumber buying fittings weekly |
| Otieno the mason (fundi) | Retail | 20,000 | 3% "Brings clients every week" | Fundi loyalty |
| Wanjiru – Plot 45 self-build | Retail | 50,000 | — | Self-builder buying in stages |
| Mutua Welding & Fabrication | Wholesale | — | — | Cash contractor price, no credit |

| Electrical customer | Price type | Credit limit | Discount | Why it is in the demo |
|---|---|---|---|---|
| Kariuki Electrical Contractors | Wholesale | 200,000 | — | Wiring contractor on account |
| Brightline Solar Installers | Wholesale | 150,000 | — | Solar kits, big tickets |
| Omondi the electrician (fundi wa stima) | Retail | 30,000 | 5% "Regular electrician" | Electrician discount at the till |
| Greenview Apartments Management | Retail | 100,000 | — | Landlord replacing bulbs and breakers on an LPO |
| Njeri – new house, Membley | Retail | — | — | Homeowner paying by M-Pesa |

### 3.7 Opening stock, through purchase orders

Load the opening stock by **raising and receiving one purchase order per supplier**, from the build sheet (filter column M, the
supplier; quantity from column L, unit cost from column K). This is better for a demo than stock adjustments:

> **Costs go in without VAT.** Both demos are VAT-registered. The API reports revenue net of VAT and takes a purchase cost as
> entered, so a VAT-inclusive cost makes every margin about 14% too low and sells low-margin items (solar) below cost. Column K
> is already without VAT. The supplier's bill then also shows the cost without VAT, not the invoice total: see gap V1 in §6.


- the stock carries a cost, so margins and stock value are right;
- the purchases report, supplier balances and price trends have something in them;
- receiving against a supplier's invoice number is part of the story ("bags short on delivery").

Steps per supplier: **Purchase orders → New** → add lines → **Order** → **Receive** with a supplier invoice number (e.g.
`INV-24518`). Then pay part of some suppliers (**Suppliers → Pay**) so balances are realistic: the hardware pays the cement depot in
full and half of the steel distributor; the electrical shop pays half of the cables distributor.

For the long suppliers (Tools & Fasteners: 48 lines; Nyota: 73 lines), entering by hand takes time. Either split them over two
orders, or enter only the products you will show plus the top sellers, and accept that a few items show zero stock (that also
shows the out-of-stock alert). Bulk upload with opening stock and cost (gap M2) removes this step; see §6.

### 3.8 Bundles

The Hardware sector has bundles and deliveries (§6 B2, built 2026-11-03). Until the electrical demo has its own sector (B1), it is
registered as Hardware and gets them too. Add:

**Hardware**
- *Toilet installation set*: Close-Coupled WC Set + Float Valve + Teflon Tape + PVC Elbow 4in + PVC Solvent Cement.
- *Roofing starter (1 room)*: 10 × Dumuzas G30 3m + 2 × Ridge Cap + 3 kg Roofing Nails.

**Electrical**
- *Solar kit 1 kW*: 2 × Jinko 450W panel + 1 × Hybrid Inverter 3kVA + 2 × Gel Battery 200Ah + 10 m Solar PV Cable + MC4 pair + Panel
  Mounting Bracket Set. Price it about 3% below the parts.
- *Single-room wiring kit*: Ready Board + 20 m EAC 2.5mm Red + 20 m EAC 2.5mm Black + 2 × Tronic switch + Tronic twin socket +
  Ceiling Rose + LED bulb.

### 3.9 Users and roles

Create roles and one user each, with simple passwords you can type in front of the client:

| Role | Hardware user | Electrical user | Permissions |
|---|---|---|---|
| Owner | the owner account | the owner account | Admin |
| Manager | `manager.hw@…` | `manager.el@…` | As in [MINIMART_ONBOARDING.md §10](MINIMART_ONBOARDING.md#10-users-and-roles) |
| Cashier | `cashier.hw@…` | `cashier.el@…` | Same; **not** Products.ManagePricing, **not** Stock.Adjust |
| Yard / Storekeeper | `yard.hw@…` | — | PurchaseOrders.View, .Receive, Stock.View, Products.View |

Showing the cashier's screen without the price field is a strong moment: "your counter staff cannot change a price".

### 3.10 Make it theirs

Before each demo:

- Ask for 10–15 real prices and change them on the products (a new price tier; old sales keep their price).
- Put the prospect's name, town and logo on the business.
- Add one or two of their real suppliers by name, if they told you.

### 3.11 History: what makes the dashboard convincing

An owner judges the system by the dashboard and the reports. With no history they show today's two practice sales, which is not
convincing. There are two ways to give the demo a past:

**A. Without code (good enough for a first demo).** Over the **three days before** the demo, sign in as the cashier and ring
40–60 sales a day across the catalogue, as below. For the electrical demo,
[practice_sales_electrical.py](demo-data/practice_sales_electrical.py) rings one such day through the API (about 45 sales, a
return, a void, a cheque from a contractor, a fuel pay-out, a pending site delivery, three months of expenses, and the day
closed KES 200 short); run it once a day. The dashboard then has a few days, the reports work, and the alerts are real.

| Sale type | Share | Example |
|---|---|---|
| Walk-in cash or M-Pesa, 1–4 lines | 60% | 3 kg Wire Nails 3in, 2 hinges, a padlock; 12 m EAC 2.5mm Red + 2 Tronic switches |
| Fundi or electrician, with discount | 15% | Otieno: 10 bags cement, 5 kg nails |
| Contractor on credit, 5–15 lines | 15% | Kamau: 60 bags cement, 30 Y12, 40 sheets, with a promised date |
| Full packs | 5% | 2 rolls of 2.5mm cable; a bag of 4in nails |
| A return, a void, a pay-out | 5% | A wrong-size fitting returned; fuel paid from the till |

Also record two months of **expenses** (rent, salaries, fuel, electricity): an expense takes its own date, so the profit-and-loss
statement has two full months even without old sales.

**B. With code (recommended before the second demo).** A demo history generator that produces 90 days of trading with realistic
patterns (§6, D1). The dashboard then shows trends, top sellers, slow movers, customer insights and ageing as they would look after
three months on the system.

### 3.12 Checklist the day before

- [ ] Sign in as owner, manager and cashier: each works and sees the right menus.
- [ ] Open a cash session with a float, so the till is ready.
- [ ] At least one product below its reorder level, and one at zero (the alerts show).
- [ ] One contractor over 30 days in the ageing report (from a credit sale with a past promised date, made during history building).
- [ ] The 80 mm printer prints a receipt; Chrome is set for silent printing if you will show auto-print.
- [ ] Phone with the dashboard open, signed in as owner (the "away from the shop" moment).
- [ ] Prospect's prices, name and logo in (§3.10).

---

## 4. The demo scripts

Each takes 30–40 minutes. Follow the shop's day (§1). Let the owner drive the till for at least one sale.

### 4.1 Hardware

| # | Scene | What you do | What to say |
|---|---|---|---|
| 1 | Opening | Open the cash session with a KES 5,000 float | "Every day starts with the float and ends with a count. Differences are flagged to you." |
| 2 | The fundi | Search "nails": 2.5 kg Wire Nails 3in, 1 kg Roofing Nails, 2 hinges; cash with change | "Sell by the kg, by the piece, by the foot. No calculator." |
| 3 | Bag vs kg | Same product, unit **bag**: one 25 kg bag at the bag price | "One stock. Sell loose or by the bag, each at its own price." |
| 4 | Contractor on account | Customer Kamau: 50 bags Bamburi, 20 × Y12, 30 × Dumuzas G30 3m. The contractor price appears by itself. Credit, promised date in 30 days | "Your contractor price list is applied for you. The counter cannot forget it, or give it to the wrong person." |
| 5 | Credit limit | Try a sale that takes Kamau over the limit: refused; manager override | "Credit stops at the limit you set unless you or your manager says yes." |
| 6 | Delivery to site | Make it a delivery order: delivery note prints; dispatch with the driver; mark delivered | "The stock is held for the site. The sale happens when it is delivered." |
| 7 | Receiving a lorry | As yard user: receive the cement PO, 200 bags ordered, 195 received, supplier invoice number | "You see exactly what came, against what you ordered and what you owe." |
| 8 | Paying and being paid | Kamau pays 150,000 by cheque; you pay the steel distributor by bank transfer | "Who owes you, who you owe, at any time." |
| 9 | A return | A 4in PVC elbow returned, refunded in cash | "Returns go back into stock and out of the day's takings, with a reason." |
| 10 | Losses | Adjust 2 bags of cement out, reason "Set in rain", as manager | "Every loss has a name and a reason. Your cashier cannot do this." |
| 11 | Owner's view | Dashboard → sales by payment method, top products, profit. Reports → margins (cement 4% vs fittings 45%), stock status (slow movers, days of cover), customer ageing, supplier balances | "This is what you would know every evening, from your phone." |
| 12 | Closing | Close the session, count the cash, see the variance | "Short or over, you know the same night." |

### 4.2 Electrical

| # | Scene | What you do | What to say |
|---|---|---|---|
| 1 | Opening | Float and session | As hardware |
| 2 | Cut cable | Omondi (electrician): 23 m EAC 2.5mm Red, 23 m Black, 10 m Earth, 4 Tronic switches. His 5% is applied by itself | "Cut what they need. The roll goes down in metres. His discount is automatic, and only his." |
| 3 | Full roll | Kariuki Contractors: 3 rolls EAC 2.5mm at the roll **wholesale** price, on account | "A roll costs less per metre than cut cable. The system knows which is which." |
| 4 | Premium vs economy | Search "socket": MK at 1,350 and Tronic at 480 side by side; filter by brand | "Both brands, both prices, and you see which one actually makes you money." |
| 5 | Solar kit | Brightline: Solar kit 1 kW as one item; each part leaves stock | "Sell the system as one line, and still know what is left of each part." |
| 6 | Box of bulbs | Greenview Apartments: 2 boxes of Tronic 9W bulbs and 10 × MCB 16A, credit, LPO number in the notes | "Institutions buy on LPO and pay later. You see what each one owes, and since when." |
| 7 | A faulty item | A Fridge Guard returned, refunded by credit note | Then be honest: "Serial numbers and warranty dates are on our roadmap" (§6, S1) |
| 8 | Receiving | Receive a cables PO from Industrial Area Cables Distributors with their invoice number | "Every metre you sell can be traced back to the invoice you bought it on. That is your answer when someone asks if your cable is genuine." |
| 9 | Owner's view | Dashboard, margins by product (cable 25% vs accessories 40–50%), stock status, ageing, customer insights (top electricians, who stopped coming) | "Which electricians bring the business, and which ones have gone quiet." |
| 10 | Closing | Close the session | |

### 4.3 Moments that tend to close the sale

- The contractor price appearing on its own, at the till.
- Selling 23 m from a 100 m roll, then a whole roll, with stock right for both.
- The margins report: most owners have never seen their margin per item.
- Customer ageing: "these three owe you KES 410,000, and this one is 45 days late."
- The dashboard on the owner's phone.

---

## 5. Questions they will ask, and honest answers

| Question | Answer today | Code change |
|---|---|---|
| "Can I give a quotation?" | Not yet. Today the price check is the till itself. | Q1 (high) |
| "Can it print an invoice with my customer's PIN and LPO number?" | The A4 tax invoice prints the customer's name; the PIN and LPO number are not printed yet. | F1 (Tier 0) |
| "Does it work with eTIMS?" | Yes: sales can be signed by KRA through eTIMS (OSCU), with the KRA block and QR on the receipt. Set up at onboarding with their own PIN and device. | None. Do not show it live on the demo PIN |
| "Can they pay half cash, half M-Pesa?" | Not on one sale yet; sell on credit and record two payments, or split into two sales. | G2 |
| "Can I charge for delivery or for cutting/threading?" | Not as a service item yet. | N1 |
| "Can I track serial numbers and warranty?" | Not yet. | S1 |
| "Can I give a price for 50+ bags?" | Contractor (wholesale) price per customer, and a standing discount per customer. Quantity-break prices are not available. | P1 |
| "Can I send a statement to my contractor?" | The customer's ledger is on screen; there is no printable statement yet. | F3 |
| "What happens when the internet goes?" | The till needs the internet. Use a phone hotspot as backup; there is no offline mode. | M13 (existing gap) |
| "Can I run two shops?" | Yes: branches with their own stock, transfers between them, reports per branch. | None |
| "Can I see how much cement vs paint I sold?" | Per product, yes; per category, not yet. | M11 (existing gap) |
| "Can the fundi earn points/commission?" | A standing discount per customer and a monthly-spend reward exist. Commissions paid to a fundi for bringing a client do not. | Optional |

---

## 6. Code changes

Tiers: **Tier 0** before the first demo (each is small); **Tier 1** questions the demo will raise, build before onboarding a real
shop of this kind; **Tier 2** onboarding a real shop. Size: S = under a day, M = 1–3 days, L = a week or more.

### Tier 0: before the demo

| # | Change | Backend | Frontend | Size |
|---|---|---|---|---|
| **B1** | **`Electrical` sector.** The electrical demo is otherwise filed as a hardware. **Built 2026-11-03.** | `BusinessSector.Electrical` (append, value 7) in `Domain/Entities/Business.cs`. `BusinessDefaults.Sectors`: Trader, negative stock allowed, units Metre (`m`), Roll (`roll`), Coil (`coil`), Set (`set`). `BusinessModules.BySector`: `Deliveries`, `Bundles`. Stored as text, so **no migration**. Update the sector tests. | `BusinessSector` enum and label in `src/api/enums.ts`; `sectorProfiles` in `src/config/sectors.ts` | S |
| **B2** | **Deliveries and bundles for Hardware.** Hardware delivers to site and sells sets. **Built 2026-11-03.** | `BusinessModules.BySector[Hardware] = { Deliveries, Bundles }`. Add Foot (`ft`) and Tonne (`t`) to the Hardware sector's units (new businesses only). | None: menus come from the API's modules | S |
| **F1** | **Customer KRA PIN and LPO number on the invoice.** Contractors and institutions need the buyer's PIN to claim VAT, and the LPO number to pay. | Add `CustomerKraPin` to `SaleDto` (from the customer). Optional, cleaner: a `CustomerReference` field on `Sale` (LPO no.) instead of using `Notes`; needs a migration. | Capture "LPO / reference" in the payment dialog for credit sales and send it as `notes` (or the new field); print PIN and reference on the A4 receipt and the delivery note (`Receipt.tsx`, `DeliveryNote.tsx`) | S |
| **F2** | **Driver, not rider.** The delivery screens say "rider", which suits water, not a lorry of cement. | None | Sector-aware wording on the delivery board and dialogs: "Driver / vehicle" for Hardware and Electrical | S |
| **D2** | **Keep demo businesses out of platform figures.** | `IsDemo` on `BusinessProfile` (migration), set at registration by the super user; the platform dashboard excludes demo businesses. Skip it if demos run on a separate environment | A "Demo business" switch on the register form and a badge in the platform list | S |

### Tier 1: what the demo will raise

| # | Change | Backend | Frontend | Size |
|---|---|---|---|---|
| **Q1** | **Quotations.** The first thing both owners will ask for. | New `Quotation` + `QuotationLine` (number `QT-…`, customer optional, valid-until date, notes, status Draft/Sent/Accepted/Expired/Converted). Priced with the same rules as a sale (customer price type, standing discount, VAT), **without reserving stock**. Endpoints: create, update, list, get, `POST /quotations/{id}/convert` to a sale or a delivery order (prices re-checked at conversion, with a warning if they changed). Permissions `Quotations.*`. | "Save as quotation" from the till cart; quotation list; printable A4 quotation with validity and terms; convert button. **Stopgap (frontend only, M):** "Print as quotation" from the cart using `POST /sales/preview`, not saved | M–L |
| **D1** | **Demo history generator.** 90 days of believable trading. | A console tool in `tools/UltimatePos.DemoSeeder` (like `UltimatePos.EtimsSandbox`), refused unless the business is a demo (D2) or the environment is not production. It runs the **real services** in date order (purchase orders and receipts, sales from a weighted product mix, credit sales and later payments, returns, adjustments, expenses, cash sessions), so stock, cost and ledgers are right; then moves each document's timestamps to its simulated day in one transaction: `Sales.CreatedAt`, `SaleLines.CreatedAt`, `StockMovements.CreatedAt`, `CustomerLedgerEntries.CreatedAt`, `SupplierLedger.CreatedAt`, `PurchaseOrders.OrderDate`/`CreatedAt`, `SaleReturns.CreatedAt`, `CashSessions.OpenedAt`/`ClosedAt`, `DeliveryOrders.*At`. The cleaner alternative, `TimeProvider` injected where repositories use `DateTime.UtcNow`, touches many files; do it only if it is wanted for tests anyway. Patterns: Saturday the busiest day, mid-month dip, more cement in dry months, 3–4 contractors carrying most credit, one customer gone quiet | None | M |
| **G2** | Split payment (existing gap): deposit in cash, the rest by M-Pesa, or part credit | Sale payments as lines; cash-up and M-Pesa matching per line | Payment dialog with several tenders | M |
| **N1** | **Service (non-stock) items**: delivery charge, cutting and threading pipe, installation labour, transport | `Product.IsStockTracked` (default true, migration). Sales, returns and voids make no stock movement for a non-stock product; stock reports leave it out; margin is the full price | Switch on the product form; services hidden from stock screens | M |
| **S1** | **Serial numbers and warranty** (electrical: inverters, batteries, panels, appliances, power tools) | `Product.TracksSerials`, `Product.WarrantyMonths`; `ProductSerial` (serial, product, status In stock/Sold/Returned/Returned to supplier, sale line, purchase receipt line). Serials captured at receiving and chosen at sale; `GET /serials/{serial}` returns the sale, customer and warranty end date | Serial entry at receiving and at the till; warranty end printed on the receipt; a "Warranty check" lookup | L |
| **V1** | **Input VAT on purchases.** A VAT-registered shop's stock cost must be without VAT, but the supplier's bill is the invoice total with VAT. Today one unit cost serves both, so either margins or supplier balances are 16% off | `PurchaseOrderLine` gets the line's VAT rate (default from the product's tax class); receiving posts the bill with VAT and puts stock in at the cost without it; purchases report shows input VAT for the VAT return | VAT column on the PO and receiving screens; costs entered as on the supplier's invoice | M |
| **F3** | **Customer statement** for contractors and institutions | `GET /customers/{id}/statement?from&to`: opening balance, invoices, payments, returns, closing balance (the ledger with a running balance) | Printable A4 statement on the customer page | S–M |

### Tier 2: onboarding a real shop

| # | Change | Why | Size |
|---|---|---|---|
| M2 | Bulk upload with barcode, cost, opening quantity and packs (existing gap) | 1,000–5,000 SKUs at a real hardware. The demo files' build columns are already in this shape | M |
| M4 | Return to supplier (existing gap) | Faulty electricals under warranty, damaged sheets, short or wrong deliveries | M |
| M11 | Sales and margin by category (existing gap) | "How much cement vs paint did I sell?" | S–M |
| P1 | Quantity-break prices (50+ bags of cement) | Common in hardware price lists; per-customer prices cover most of it meanwhile | M |
| G8 | Receive without an order (existing gap) | Owner's cash trips to Industrial Area | S |
| M9 | Shelf and bin labels with barcodes | Most hardware items have no barcode | M |
| — | Variant matrix on product creation (size × gauge × length, colour × size) | Iron sheets and pipes come in dozens of sizes; one form instead of thirty | M |
| — | Fundi commission (a referrer on the sale, commission report) | How many hardwares keep fundis loyal. Ask the owner first: practices vary and are not well documented | M |

**Recommended order:** F1, F2 (and D2 if demos share production) before the first demo. Then Q1, D1 and V1. Then N1, F3, G2, S1
as the first real hardware or electrical client needs them.

---

## 7. After the demo

- Put the trading name and logo back to the demo's.
- Leave the business in place for the next demo. To start again, close the business (Platform → Businesses → Close) and register a
  new one; data is kept but no longer reachable.
- Note what the prospect asked for in §5 that is not built: it decides the order of §6.

---

## Sources (checked November 2026)

- How a Kenyan hardware works, products, margins, supplier credit, licences: [Huduma Global: how to start a hardware and building
  materials store in Kenya](https://hudumaglobal.com/blog/how-to-start-hardware-building-materials-store-kenya),
  [Construction Kenya: starting a hardware business](https://www.constructionkenya.com/11229/start-hardware-business-kenya/),
  [How We Made It In Africa: hardware chains in Kenya](https://www.howwemadeitinafrica.com/profit-making-idea-efficient-affordable-and-well-stocked-hardware-store-chain-in-kenya/63793/)
- Prices: [Construction Kenya: cement prices](https://constructionkenya.com/3007/cement-price-kenya),
  [Business Radar: mabati prices](https://www.businessradar.co.ke/blog/2024/10/11/how-much-royal-mabati-costs-a-comprehensive-price-breakdown/),
  [Latest Cost: iron sheet prices by gauge](https://latestcost.com/iron-sheet-price-kenya-gauge-profile-roof-size/),
  [Beyond Forest: building materials price list](https://www.beyondforest.org/post/building-materials-list-and-price-in-kenya),
  [Jiji: 2.5 mm cable listings](https://jiji.co.ke/nairobi/297-electrical-wires/high)
- Counterfeit electrical goods: [The Star: cables the most counterfeited goods](https://www.the-star.co.ke/news/2019-04-05-electric-cables-the-most-counterfeited-goods),
  [Citizen Digital: KEBS seizes substandard extension sockets](https://www.citizen.digital/news/kebs-seizes-substandard-extension-sockets-worth-ksh25-million-in-mombasa-n366148),
  [KNA: KEBS intercepts 24,000 extension cables](https://www.kenyanews.go.ke/kebs-intercepts-24000-substandard-power-extension-cables/),
  [Nairobi News: fake socket outlets impounded](https://nairobinews.nation.africa/hundreds-of-fake-portable-socket-outlets-impounded/),
  [PC Tech: Schneider survey of counterfeit electrical products in Africa](https://pctechmag.com/2015/03/schneider-electric-releases-the-first-survey-on-counterfeit-electrical-products-in-africa/)
- Electrical shops' stock and customers (owners' own accounts): [Zidisha: electrical shop loan profiles](https://zidisha.org/index.php/loan/electrical-goods-for-my-shop)
- Solar VAT (unsettled, **confirm**): [Capital FM: 16% VAT on solar equipment](https://www.capitalfm.co.ke/business/2025/06/16pc-vat-on-solar-equipment-to-raise-prices-by-sh2000-gogla-warns/),
  [KPMG: Finance Act 2025 analysis](https://assets.kpmg.com/content/dam/kpmg/ke/pdf/tax/2025/Finance_Act%202025_KPMG_Analysis.pdf),
  [Launch Base Africa, June 2026](https://launchbaseafrica.com/2026/06/24/a-reprieve-for-kenyas-electric-vehicles-but-for-how-long/)
- Hardware POS features elsewhere (quotations, contractor accounts, units): [Touch Dynamic](https://www.touchdynamic.com/blogs/news/construct-higher-profits-with-six-must-have-hardware-store-pos-features),
  [MicroBiz](https://microbiz.com/hardware-paint-store-pos/), [Celerant](https://www.celerant.com/industries/hardware/)
- eTIMS, VAT and licences: as in [MINIMART_ONBOARDING.md §12](MINIMART_ONBOARDING.md#12-kenyan-compliance-a-minimart-must-meet)

Not found in public sources: customary fundi commissions, and usual credit terms hardwares give contractors. Ask the prospect.
