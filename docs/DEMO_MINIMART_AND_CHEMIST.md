# Demo accounts: an estate minimart and a chemist (Kenya)

How to build two demo businesses in Ultimate POS that look and behave like shops already trading, and how to walk a prospect
through them:

- **Jirani Minimart (Demo)**: an estate minimart in Kasarani selling staples, bread and milk, fresh produce, drinks, household and
  personal care to families, a school staffroom and a kiosk that restocks from it. It keeps a *deni* (credit) book and delivers to
  the apartments by boda.
- **Tiba Bora Chemist (Demo)**: a retail pharmacy in Juja selling over-the-counter medicines, dispensing prescriptions from the
  clinic next door, refilling chronic patients every month, billing an insurer, and supplying a clinic and a mission dispensary.

Both are built by scripts in about a minute each (§3), and a second script gives each a full day of trading (§3.6). The
catalogues are in [docs/demo-data/](demo-data/). §1 explains how these shops work, so the demo follows their day. What the code
does not do yet, and what was changed for these demos, is in §6.

This guide follows [DEMO_HARDWARE_AND_ELECTRICAL.md](DEMO_HARDWARE_AND_ELECTRICAL.md); its sections on personalising a demo (§3.10)
and after the demo (§7) apply unchanged. For onboarding a real minimart, see [MINIMART_ONBOARDING.md](MINIMART_ONBOARDING.md).

> **Prices are demo figures.** They are set from public 2026 Kenyan price trackers (see Sources), rounded to shelf prices, with
> margins typical of each kind of item. Medicine prices in Kenya are not regulated and vary widely between shops. Before each demo,
> ask the prospect for 10 to 15 of their own prices and change those products.

---

## 1. How these shops run

### 1.1 An estate minimart

| Topic | How it works | What the demo must show |
|---|---|---|
| **Customers** | Families from the estate, several times a day and in small amounts; a few regulars who buy on credit and pay at month end (the *deni* book); a kiosk owner who restocks from the minimart; an institution (a school staffroom, an office) on a monthly account. | Fast cash and M-Pesa sales, credit with a limit and a promised date, a wholesale price for the kiosk |
| **The day** | A breakfast rush (bread, milk, eggs, mandazi), a quiet midday, an evening supper rush (unga, sukuma, tomatoes, onions, cooking fat, charcoal, paraffin). | The till keeps up with a queue |
| **How goods are sold** | Packets by the piece; sugar, rice, beans and produce **loose by weight** (half a kilo of sugar, a quarter of onions); eggs singly or by the tray; flour and sugar by the bale to the kiosk; soda and water by the carton. | Decimal quantities; one product sold by the piece and the pack at different prices |
| **Margins** | Unga, sugar and cooking oil sell close to cost (5–10%) to bring people in. Bread and milk earn 8–12%; the distributor sets the shelf price. Drinks and toiletries 15–20%, snacks and sweets 20–30%, produce about 40% to cover what spoils. | The margin report: staples bring customers, snacks and toiletries pay the rent |
| **Suppliers** | Bread and milk **vans every morning**, paid in cash or M-Pesa on the spot; a wholesaler for dry goods on 14 days; produce from the market, cash; a gas dealer for LPG refills. | Receiving the vans with their delivery notes, paying from the till, supplier balances |
| **Losses** | Milk that goes sour, squashed bread, tomatoes that go soft, short deliveries, "borrowing" by staff. | Returns, stock adjustments with a reason, by the manager only |
| **Deliveries** | Estate customers order by phone or WhatsApp; a boda rider takes it to the door and comes back with the M-Pesa code. | A delivery order, dispatched to a rider, paid on delivery |
| **Compliance** | County single business permit; KRA PIN; Turnover Tax, or VAT above KES 5 million of taxable sales; **no medicines**: the Pharmacy and Poisons Board has been prosecuting supermarkets and general shops that sell them since January 2026. | The demo stocks no medicines. Mention eTIMS is supported; do not switch it on |

### 1.2 A chemist

| Topic | How it works | What the demo must show |
|---|---|---|
| **Customers** | Walk-ins for painkillers, cold and flu, stomach upsets and family planning; patients with a prescription from a nearby clinic; **chronic patients** (diabetes, blood pressure, asthma) refilling every month; insured patients whose bill goes to the insurer; small clinics and dispensaries buying stock. | OTC sales, prescriptions, refills on account, an insurer as a credit customer, a wholesale price for clinics |
| **How medicines are sold** | Tablets and capsules **by the tablet or by the strip** (a strip is cheaper per tablet), boxes of 100 for chronic patients; syrups, creams and inhalers by the piece; ORS and Eno by the sachet; courses of antibiotics in exact counts (15 capsules, 21 tablets). | One product sold by the tablet and the strip, stock in tablets for both |
| **Brands and generics** | The same molecule in a brand (Augmentin, Amoxil, Flagyl, Brufen) and in generics at a fraction of the price. The pharmacist offers both; generics carry the margin (60–100% mark-up against 25–35% on brands). | Search by generic name finds every brand; the margin report shows where the money is |
| **Prescription-only medicines** | Antibiotics, chronic medicines, steroids, inhalers and oral antifungals may only be sold against a prescription, and the sale recorded. Only a pharmacist or an enrolled pharmaceutical technologist may dispense. | The till asks for the prescription; a counter attendant is refused; the prescription register |
| **Expiry** | Every medicine has a batch number and an expiry date. Expired stock cannot be sold; it is written off and collected by a licensed waste handler. Near-expiry stock is sold first or returned to the supplier. | Receiving with batch and expiry, first-expiry-first-out at the till, the expiry report, a write-off from a batch |
| **Suppliers** | A pharmaceutical distributor for brands, a generics wholesaler, a surgical supplier, a cosmetics distributor; 14–30 days' credit. | Purchase orders received with batch numbers against an invoice |
| **Insurance** | Insured patients pay nothing or a co-pay; the chemist claims from the insurer monthly and is paid late. | The insurer as a customer on account, with the member number on the sale |
| **Compliance** | **PPB premises licence**, renewed every year, with a registered pharmacist or enrolled technologist in charge (the PPB closed dozens of unlicensed outlets in Nairobi and Kajiado in 2026, and acts against "licence renting"); county business permit; KRA PIN. Medicines are VAT-exempt, so a chemist whose other sales are small is often not VAT-registered. | Who dispensed each prescription, from which batch; the dispensing permission |

### 1.3 What wins these owners over

1. **"I don't know who owes me."** The *deni* book, the insurer and the clinic on account, with limits, promised dates and ageing.
2. **"Stock goes and I don't know where."** The vans received against their delivery notes; every write-off with a reason and a
   name; a cashier who cannot change a price or adjust stock.
3. **"Expired stock is money thrown away."** (chemist) Batches, the near-expiry card, first-expiry-first-out, the expiry report
   with its value.
4. **"The inspector will ask."** (chemist) The prescription register: who dispensed what, to whom, from which batch.
5. **"I don't know what makes money."** Margins by product: unga against snacks, brand against generic.
6. **"I can't leave the shop."** The dashboard on the phone, the cash count with its variance.

---

## 2. The two demo businesses

| | Minimart demo | Chemist demo |
|---|---|---|
| Business name | Jirani Minimart (Demo) | Tiba Bora Chemist (Demo) |
| Trading name | Jirani Minimart | Tiba Bora Chemist |
| Sector | `Minimart` (4) | `Pharmacy` (1) |
| Business type / kind | Retailer (1) / Trader (1) | Retailer (1) / Trader (1) |
| County / address | Nairobi, Mwiki Road, Kasarani | Kiambu, Kenyatta Road, Juja |
| KRA PIN | `P000000003M` (dummy) | `P000000004C` (dummy) |
| VAT registered | No (a small estate minimart) | No (sales are mostly exempt medicines) |
| M-Pesa till | `000333` | `000444` |
| Max cashier discount | 5% | 10% |
| Monthly-spend reward | 2% for the rest of the month after KES 10,000 | Off |
| Allow negative stock | Yes (sector default). Batch-tracked items never go below zero | Yes; every medicine is batch-tracked, so medicines never go below zero |
| eTIMS | **Off** | **Off** |
| Licence | Complimentary, 12 months | Complimentary, 12 months |

Because neither is VAT-registered, unit costs are loaded **as paid**, VAT included: a shop that is not registered cannot claim it
back. Each product still carries its usual tax class (zero-rated flour, bread and milk; exempt eggs, produce and medicines) so
nothing needs changing if the prospect is registered; then load costs without VAT as the hardware demo does.

---

## 3. Building each demo

### 3.1 The scripts

| Script | What it does | Time |
|---|---|---|
| [setup_minimart_demo.py](demo-data/setup_minimart_demo.py) | Registers Jirani Minimart and its complimentary licence; units, expense categories, categories, 116 products with packs and prices, batch tracking on 8 of them; the monthly reward; 7 suppliers; 6 customers; 3 bundles; the opening stock (one purchase order per supplier, received against an invoice, most suppliers paid); roles and three staff | ~1 min |
| [setup_chemist_demo.py](demo-data/setup_chemist_demo.py) | Registers Tiba Bora Chemist and its licence; categories, 98 products with strips and boxes; generic name, strength and dosage form on every medicine, 77 batch-tracked, 29 prescription-only; 4 suppliers; 6 customers; opening stock received **in batches with expiry dates**; roles and three staff | ~1 min |
| [practice_sales_minimart.py](demo-data/practice_sales_minimart.py) | One day of trading (§3.6) | ~1 min |
| [practice_sales_chemist.py](demo-data/practice_sales_chemist.py) | One day of trading (§3.6) | ~1 min |

```bash
pip install requests openpyxl
python docs/demo-data/setup_minimart_demo.py
python docs/demo-data/setup_chemist_demo.py
python docs/demo-data/practice_sales_minimart.py     # once a day, on the days before the demo
python docs/demo-data/practice_sales_chemist.py
```

They work through the API like the hardware scripts and share [demo_setup.py](demo-data/demo_setup.py) and
[demo_practice.py](demo-data/demo_practice.py). They refuse any API that is not on localhost unless given `--allow-remote` (for a
demo or staging environment, never production), and skip whatever already exists, so they can be run again. Once the owner has
changed the password, pass it with `--owner-password`.

| Who | Sign-in (first time) | Can |
|---|---|---|
| Minimart owner | `demo.minimart@ultimatepos.local` / `JiraniDemo@2026` | Everything |
| Minimart manager | `manager.mm@ultimatepos.local` / `JiraniStaff2026` | Prices, receiving, voids, returns, write-offs, the day's cash, deliveries, reports |
| Minimart cashier | `cashier.mm@ultimatepos.local` / `JiraniStaff2026` | The till, deni payments, delivery orders. **Not** prices, voids or stock |
| Minimart storekeeper | `store.mm@ultimatepos.local` / `JiraniStaff2026` | Receiving and stock levels only |
| Chemist owner | `demo.chemist@ultimatepos.local` / `TibaDemo@2026` | Everything, including dispensing |
| Pharmacist | `pharmacist.ch@ultimatepos.local` / `TibaStaff2026` | As a manager, and dispenses |
| Pharmaceutical technologist | `pharmtech.ch@ultimatepos.local` / `TibaStaff2026` | The till, returns, dispensing |
| Counter attendant | `counter.ch@ultimatepos.local` / `TibaStaff2026` | The till for OTC and cosmetics. **Cannot dispense** |

Staff must change their password at first sign-in. Sign in as each one the day before (§3.7) so it is done before the prospect
watches.

### 3.2 What the catalogues contain

The files are generated by [build_demo_catalogues.py](demo-data/build_demo_catalogues.py):
`python docs/demo-data/build_demo_catalogues.py docs/demo-data minimart chemist` rebuilds these two only. Columns A–J are the
API's bulk-upload layout, so the files can also be uploaded by hand (Catalog → Bulk Import). The yellow columns are a build sheet the
upload ignores: cost, opening quantity, supplier and pack, as in the hardware files, and for these two also **barcode, batch tracked,
shelf life, first batch expiring in N days, prescription-only, generic name, strength and dosage form**. The upload cannot take those
yet (gap M2); the setup script sets them on each product after the upload.

| | Minimart | Chemist |
|---|---|---|
| Departments → categories | 14 → 41, with the codes of [MINIMART_ONBOARDING.md §3](MINIMART_ONBOARDING.md#3-categories-in-a-kenyan-minimart) | 9 → 19 |
| Products | 116 | 98 |
| Packs (with their own price) | 19: bales of flour and sugar, cartons of soda, water, oil and noodles, trays of eggs, packets of tissue and matches, a dozen exercise books | 39: strips of 10 or 14, boxes of 100 for chronic medicines, boxes of plasters and masks |
| Batch-tracked | 8: yoghurt, mala, UHT milk, sausages, smokies, baby food. Fresh milk and bread are not: they sell out daily and the van takes stale stock back | 77: every medicine, and glucometer strips. Cosmetics, condoms and devices are not |
| Prescription-only | — | 29: antibiotics, oral antifungals, diabetes, blood pressure, asthma, steroids, a few creams and eye drops |
| Opening stock at cost, as paid | ≈ KES 459,000 | ≈ KES 383,000 |
| Wholesale price | Cost + 60% of the retail margin (the kiosk) | Same (the clinics) |

Prescription-only follows usual Kenyan practice and is a demo setting. For a real chemist, the superintendent confirms each product
against the PPB's classification before go-live.

**Barcodes.** The catalogues have no barcodes: inventing manufacturers' codes would only make real packs scan wrong. Before the
demo, buy or borrow 10 common items (a 2 kg unga, a Kabras sugar, a Panadol Extra pack, a Coca-Cola 500 ml), scan each into its
product's Barcode field (Catalog → Products → edit), and scan them at the till in front of the prospect. Or fill the Barcode column
of the build sheet and run the setup script again.

### 3.3 Opening stock and batches

One purchase order per supplier, received in full against an invoice number, as in the hardware demo. Batch-tracked lines are
received in named batches with expiry dates:

- a product with a lot of stock comes in two batches, an older one expiring sooner, so first-expiry-first-out has a choice;
- three chemist lines come with a batch expiring within two months (Doxycycline 100 mg, Expectorant Cough Syrup, Calamine
  Lotion), so the **expiry report and the near-expiry card** have something to show on day one;
- minimart yoghurt and mala expire in 14–21 days, so after two weeks without practice days some batches will have expired. That
  is realistic (it shows in the expiry report) but the till will not sell an expired batch: run a practice day, which receives the
  dairy van, before the demo.

### 3.4 Suppliers and customers

Names are fictitious on purpose; brands are real, as they are on the shelves.

| Minimart supplier | Terms | Chemist supplier | Terms |
|---|---|---|---|
| Mwiki Wholesalers & Distributors (dry goods, household, personal care) | 14 days, half paid | Kilimani Pharmaceutical Distributors (brands) | 30 days, half paid |
| Mkate Fresh Bread Distributors (bread van) | Cash | Afya Generics Wholesalers (generics) | 30 days, unpaid |
| Maziwa Bora Dairy & Cold Chain (milk van, sausages) | 7 days | Mediplus Surgical Supplies Ltd | 30 days, paid |
| Kasarani Beverage Distributors | 14 days, unpaid | Uzuri Cosmetics Distributors | 14 days, paid |
| Githurai Market Produce (Mama Njeri) | Cash | | |
| Ruiru Poultry Farm (eggs) | Cash | | |
| Mwiki Gas Point (LPG refills, paraffin) | 7 days | | |

| Minimart customer | Price | Credit limit | Why it is in the demo |
|---|---|---|---|
| Mama Wanjiku – House 14, Mwiki Gardens | Retail | 5,000 | The *deni* book: pays at month end |
| Otieno – Block C, Kasarani Estate | Retail | 8,000 | Deni, pays part by M-Pesa |
| Baraka Kiosk (Chief's Road) | **Wholesale** | 20,000 | Buys bales and cartons; pays each restock on the next |
| Mwiki Primary School Staffroom | Retail | 15,000 | An institution on a term account |
| Jane Muthoni – regular | Retail | — | Standing 2% "Buys every day" |
| Sunrise Court Apartments (deliveries) | Retail | 10,000 | WhatsApp orders delivered by boda |

| Chemist customer | Price | Credit limit | Why it is in the demo |
|---|---|---|---|
| Juja Road Medical Clinic | **Wholesale** | 80,000 | A clinic buying stock on an LPO |
| Kalimoni Mission Dispensary | **Wholesale** | 50,000 | Monthly order |
| Afya Bora Medical Cover (insurer) | Retail | 150,000 | Insured patients billed to the insurer, member number on the sale |
| Mzee Kamau – diabetes & BP | Retail | 10,000 | Monthly refill on account, standing 5% |
| Grace Wambui – asthma | Retail | — | Repeat inhaler, M-Pesa |
| Mama Akinyi – young family | Retail | — | ORS, zinc and a child's antibiotic |

### 3.5 Bundles (minimart)

| Bundle | Parts | Price |
|---|---|---|
| Breakfast Combo | Supaloaf 400 g + 2 × Brookside 500 ml + Blue Band 250 g | KES 320 |
| Ugali & Sukuma Supper Pack | Soko 2 kg + 3 sukuma + ½ kg tomatoes + ¼ kg onions | KES 320 |
| Back-to-School Pack | 6 exercise books + 3 pens | KES 290 |

The Pharmacy sector has no bundles module; a chemist's "packs" (a first-aid kit) are products of their own.

### 3.6 A day of trading

An owner judges the system by the dashboard and the reports. Run each practice script **once a day for three days before the
demo**; each run is one day (the API dates sales today, so days cannot be run in advance; see D1 in the hardware guide).

| | Minimart day | Chemist day |
|---|---|---|
| Opening | KES 3,000 float | KES 2,000 float |
| Deliveries | Bread van received and paid in cash from the till; dairy van received (yoghurt, mala and smokies in dated batches) and paid by M-Pesa | — |
| Sales | ~75: breakfast rush, midday shelves, evening supper rush, the three bundles, cash with change or an M-Pesa code (about 40%) | ~40: OTC by the tablet and the strip, 7 prescriptions recorded with prescriber and patient, cosmetics and family planning |
| On account | Mama Wanjiku, the staffroom, Otieno (who pays what he owes first); the kiosk's bales at the wholesale price (paying the last restock first) | Mzee Kamau's monthly refill with his 5%; an insured patient billed to Afya Bora; the clinic and the dispensary at wholesale |
| Payments in | Otieno pays up to 1,500 of what he owes by M-Pesa | Kamau's son pays 1,000 by M-Pesa; the insurer pays half its balance by EFT once it owes over 5,000 |
| Exceptions | Sour milk returned and refunded (not restocked); a sale voided; 1.5 kg tomatoes and a loaf written off; KES 8,000 banked at midday | A sealed cream returned and restocked; a sale voided; a broken Benylin bottle written off from its batch |
| Open for the demo | A WhatsApp delivery for Sunrise Court, waiting for a rider | — |
| Close | KES 100 short | KES 50 over |
| Expenses | Rent, power for the fridges, water, salaries, M-Pesa charges, the estate guard, wrapping paper; the county permit and a fridge service in the first month | Rent, power, internet, salaries, M-Pesa charges, the guard; the PPB premises licence, county permit and expired-stock collection in the first month |

Expenses are dated over the last two months and this one, and recorded **after** the day is closed: the API takes a cash expense
recorded while the drawer is open as paid out of it. A later day adds only what has fallen due since.

### 3.7 Checklist the day before

- [ ] A practice day run today (the minimart's dairy must be in date; the near-expiry card must show).
- [ ] Sign in as owner, manager/pharmacist, cashier/counter attendant: each changes the password and sees the right menus.
- [ ] 10 real items with their barcodes scanned in (§3.2), and the scanner works at the till.
- [ ] A cash session open with a float.
- [ ] At least one product at or below its reorder level, so the alerts show: in the minimart, sukuma and bread usually are after a
  practice day; in the chemist, Ventolin starts at its reorder level.
- [ ] The 80 mm printer prints a receipt.
- [ ] The dashboard open on a phone, signed in as the owner.
- [ ] The prospect's prices, name and logo in (hardware guide §3.10).

---

## 4. The demo scripts

Each takes 30–40 minutes. Follow the shop's day (§1). Let the owner ring up at least one sale.

### 4.1 Minimart

| # | Scene | What you do | What to say |
|---|---|---|---|
| 1 | Opening | Open the cash session with a KES 3,000 float | "Every day starts with the float and ends with a count. You see the difference the same night." |
| 2 | The bread van | As manager, raise the bread order; as storekeeper, receive it against the driver's delivery note; as manager, pay the driver in cash | "What came, against what you ordered, and the cash that left the drawer for it." |
| 3 | Breakfast rush | Scan the real items you brought; a Breakfast Combo as one line; cash with change; an M-Pesa code | "Fast enough for a queue. The combo is one tap; each part leaves its own stock." |
| 4 | Loose goods | ½ kg Sugar (loose), ¼ kg Red Onions, 3 sukuma | "Sell by weight without a calculator." |
| 5 | Egg or tray | 4 single eggs, then one tray at the tray price | "One stock of eggs, two prices." |
| 6 | The deni book | Mama Wanjiku: unga, oil, sugar on credit to month end. Then try to go over her limit: refused; manager override | "The deni book, without the book. It stops at the limit you set." |
| 7 | The kiosk | Baraka Kiosk: 2 bales of Soko and a carton of soda; the wholesale price appears by itself | "Your kiosk price is applied for you. The cashier cannot give it to the wrong person." |
| 8 | The regular | Jane Muthoni: her 2% is applied; explain the monthly reward (2% after KES 10,000 in a month) | "Reward the families who buy here every day, automatically." |
| 9 | WhatsApp order | Open the Sunrise Court delivery; dispatch it to a boda rider; mark delivered with the M-Pesa code | "Phone orders go out with a delivery note, and the money comes back recorded." |
| 10 | Cashier's screen | Sign in as the cashier: no price field, no stock adjustment, no void | "Your counter staff cannot change a price or make stock disappear." |
| 11 | Losses | As manager: write off 1 kg of spoilt tomatoes, reason "Spoilt"; refund a sour milk | "Every loss has a reason and a name." |
| 12 | Expiry | The near-expiry card: yoghurt that must go this week | "Sell it or return it before it is money thrown away." |
| 13 | Owner's view | Dashboard; margins (unga about 6%, snacks 25–30%); low stock and what to reorder; customer ageing (the deni); supplier balances; profit and loss with expenses | "This is what you would know every evening, from your phone." |
| 14 | Closing | Close the session; count the drawer; the variance | "Short or over, you know tonight." |

### 4.2 Chemist

| # | Scene | What you do | What to say |
|---|---|---|---|
| 1 | Opening | Float and session | As the minimart |
| 2 | By the tablet or the strip | 6 Panadol Extra tablets, then a strip at the strip price; stock goes down in tablets for both | "Sell what the customer needs. The strip is cheaper per tablet and the system knows it." |
| 3 | Brand or generic | Search "amoxicillin": Amoxil, Augmentin, Co-Amoxiclav and the generic side by side | "Search by the molecule and offer both. And see which one makes you money." |
| 4 | A prescription | Amoxicillin 500 mg × 15: the till asks for the prescription; enter prescriber, facility, patient, age, date. The sale records the batch it came from, the oldest first | "No antibiotic leaves without its prescription recorded." |
| 5 | The counter attendant | Sign in as the counter attendant; try the same sale: refused | "Only your pharmacist or your technologist can dispense. The system enforces it, even when you are away." |
| 6 | The register | Sales → Prescription register for today: patient, prescriber, who dispensed, which batch | "When the inspector asks, this is your answer." |
| 7 | Expiry | Expiry report: three batches within 90 days, with their value; first-expiry-first-out at the till; write off a damaged bottle from its batch | "You see the expiry coming, and you sell the oldest first without thinking about it." |
| 8 | Receiving | Receive a generics order: batch number and expiry for each line, from the invoice | "Every tablet you sell traces back to the batch and the invoice it came on." |
| 9 | The insured patient | Augmentin for an Afya Bora member, on the insurer's account, member number in the notes; the insurer's balance | "What each insurer owes you, and since when." Then be honest about claims (§5) |
| 10 | The chronic patient | Mzee Kamau's monthly refill: his 5%, on account. Customer insights: who has not come back for a refill | "Your chronic patients are your steadiest income. See who has stopped coming." |
| 11 | The clinic | Juja Road Medical Clinic: plasters, gauze, ORS at the wholesale price, on account | "Your clinic price list, applied by itself." |
| 12 | Owner's view | Margins: brands about 25%, generics about 45% of the price; stock value; ageing; profit and loss | "Your margin is in the generics." |
| 13 | Closing | Close the session | |

### 4.3 Moments that tend to close the sale

- (minimart) The deni book on screen: who owes what, since when, and the kiosk paying each restock on the next.
- (minimart) The cashier's screen without a price field.
- (chemist) The counter attendant refused, and the prescription register with batch numbers.
- (chemist) The expiry report with its value in shillings.
- The dashboard on the owner's phone.

---

## 5. Questions they will ask, and honest answers

| Question | Answer today | Code change |
|---|---|---|
| "Can a customer pay part cash, part M-Pesa?" | Not on one sale yet; record one tender, or sell on credit and take two payments. | G2 |
| "Does it read my scale's barcode labels?" | Not yet; the cashier types the weight shown on the scale. | M5 |
| "Can the carton's barcode scan as a carton?" | Not yet; scan a piece and pick the carton unit. | M3 |
| "Can I run a weekend offer?" | A new price, then the old one back after; no automatic start and end. | M7 |
| "Can I return stale bread or expired stock to the supplier?" | Not as one document yet; adjust it out with a reason and agree the credit with the supplier. | M4 |
| "What if the internet goes?" | The till needs the internet; keep a phone hotspot as backup. | M13 |
| "Can I claim from the insurer and SHA?" | The insurer is a customer on account, with the member number in the notes; its balance and ageing are there. There are no claim forms, no scheme rules, no co-pay split and no SHA link. | PH1, G2 |
| "Can it keep the DDA register for controlled drugs?" | Not yet. The prescription register covers prescription-only medicines. Keep the controlled-drugs register on paper. | PH2 |
| "Can it print the label for the bottle (dose, times a day)?" | Not yet; the receipt shows the medicine and batch, not the directions. | PH3 |
| "Can I supply antibiotics to a clinic?" | A sale of a prescription-only medicine needs a patient's prescription; a clinic's stock order of them cannot be recorded properly yet. Clinics buy OTC and surgical items at wholesale today. | PH4 |
| "Can it remind patients to refill?" | Customer insights shows who has stopped coming; there are no SMS reminders. | PH5 |
| "Can I see sales by department (dairy vs drinks)?" | Per product, yes; per category, not yet. | M11 |
| "Does it work with eTIMS?" | Yes, set up at onboarding with the shop's own PIN and device. | None; do not show it on a demo PIN |
| "Can I run two shops?" | Yes: branches with their own stock, transfers between them, reports per branch. | None |

---

## 6. Code changes

### Built for these demos (3 November 2026)

| # | Change | Where |
|---|---|---|
| **MS1** | **Minimart sector defaults**: new minimarts start with the Carton (`ctn`), Tray (`tray`) and Bale (`bale`) units and the `PKG` (packaging and carrier bags) and `REF` (refrigeration) expense categories, which [MINIMART_ONBOARDING.md §4](MINIMART_ONBOARDING.md#4-business-configuration) asked people to add by hand | `BusinessDefaults.Sectors[Minimart]` |
| **MS2** | **Deliveries for minimarts**: the delivery board, rider dispatch and rider summary, for WhatsApp and phone orders delivered by boda | `BusinessModules.BySector[Minimart]` |
| **PS1** | **Sachet unit for pharmacies**: ORS, Eno and Mara Moja are sold by the sachet | `BusinessDefaults.Sectors[Pharmacy]` |

Sectors are stored as text and the defaults apply to businesses registered from now on, so **no migration**. Existing minimarts
see the delivery menu as soon as the API is released (the menu follows the sector); they get the new units and expense categories
only if added by hand. The setup scripts add any that are missing, so the demos work on an API released before these changes.
Tests: `BusinessSectorDefaultsTests`. The frontend reads modules from the API; only the table in
[Frontend-Sector-Modules-Prompt.md](Frontend-Sector-Modules-Prompt.md) changed.

### Tier 1: what these demos will raise

Size: S = under a day, M = 1–3 days, L = a week or more.

| # | Change | Backend | Frontend | Size |
|---|---|---|---|---|
| **PH1** | **Insurance claims.** Pharmacies live on insured patients | `InsuranceScheme` (insurer, scheme, co-pay % or amount, claim limit); on the sale: scheme, member number, the patient's share and the insurer's share (needs G2's payment lines); a claims list per insurer and month with status (Submitted, Paid, Rejected), and the insurer's payment matched to claims | Scheme and member number at the till; claims screen; printable claim summary | L |
| **PH2** | **Controlled-drugs (DDA) register** | `Product.IsControlled`; a register of every movement of a controlled product (received, dispensed, written off) with running balance, prescriber and patient; stock adjustments of a controlled product need a second person | Register screen and print | M |
| **PH3** | **Dispensing label**: patient, medicine, dose, how often, how long, warnings | `SaleLine.Directions` (text, optional), required by nothing | Directions field on prescription-only lines; a small label print (thermal) per line | S–M |
| **PH4** | **Supplying a facility**: a clinic's stock order of prescription-only medicines | On a sale to a customer marked as a licensed facility (`Customer.FacilityLicenceNumber`), record the facility's licence and order number instead of a patient prescription; shown in the register | Facility licence on the customer form | S |
| **PH5** | **Refill reminders** for chronic patients | A refill interval per customer and product (or from the last quantity and the daily dose); a daily job that lists or texts patients due, through the existing notifications | "Due for refill" list | M |
| **G2** | Split payment (existing gap): part cash, part M-Pesa; an insurer and a co-pay | Sale payments as lines; cash-up and M-Pesa matching per line | Several tenders in the payment dialog | M |
| **M4** | Return to supplier (existing gap): stale bread and sour milk to the van, expired medicines to the distributor | One document: stock out (from a batch where tracked), the supplier's balance down | From the supplier or the expiry report | M |
| **G8** | Receive without an order (existing gap): the vans | `POST /purchase-orders/receive-direct`, an order made and received in one step | "Receive delivery" from the supplier | S |

### Tier 2: onboarding a real shop

| # | Change | Why | Size |
|---|---|---|---|
| M2 | Bulk upload with barcode, cost, opening quantity, packs, batch and expiry, and the medicine fields | 1,000–3,000 items at a real minimart or chemist. The demo build sheets are already in this shape, and `demo_setup.py` shows the calls it would replace | M |
| M3, M5 | Pack barcodes; scale barcodes | Cartons and bales; weighed produce and meat | M each |
| M7 | Time-limited promotions | Weekend offers, near-expiry discounts | M |
| M11 | Sales and margin by category | "How much did dairy make this month?" | S–M |
| M13 | Offline till | Estates with unreliable power and data | L |
| D1 | Demo history generator (hardware guide §6) | 90 days of history instead of three practice days | M |

**Recommended order:** for the first chemist client, PH3 and PH4 (small, and the pharmacist will ask), then G2 with PH1. For the
first minimart client, M2 and G8, then G2 and M4.

---

## 7. After the demo

As in the hardware guide §7: put the name and logo back, leave the business for the next demo, and note what the prospect asked
for in §5 that is not built.

---

## Sources (checked November 2026)

- Minimart prices: [Money254 weekly shopping trackers, Nairobi](https://www.money254.co.ke/post/weekly-shopping-tracker-see-latest-prices-for-basic-goods-in-nairobi-3-news),
  [Money254: food items set for price increases](https://www.money254.co.ke/post/5-food-items-set-for-price-increases-this-month---cbk-survey-news),
  [The Star: average retail prices of consumer goods, May 2026](https://www.the-star.co.ke/news/infographics/2026-05-08-annual-average-retail-prices-of-selected-consumer-goods-1),
  [Kenya Times: sugar and maize flour prices, August 2026](https://thekenyatimes.com/latest-kenya-times-news/sugar-maize-flour-prices-hike-in-latest-report/)
- Medicines in general shops and pharmacy enforcement: [The Standard: crackdown on illegal pharmacies from 2026](https://www.standardmedia.co.ke/health-science/article/2001536979/kenya-to-crack-down-on-illegal-pharmacies-from-2026),
  [The Star: crackdown on unlicensed sellers, December 2025](https://www.the-star.co.ke/news/2025-12-17-crackdown-looms-as-ppb-targets-unlicensed-sellers-of-drugs),
  [The Star: 95 arrested, 48 pharmacies closed, June 2026](https://www.the-star.co.ke/news/2026-06-22-95-arrested-48-pharmacies-closed-in-major-crackdown),
  [Eastleigh Voice: supermarkets ordered to stop selling unregistered medicines](https://eastleighvoice.co.ke/health%20ministry%20kenya/255908/government-orders-supermarkets-retailers-to-cease-selling-unregistered-and-counterfeit-medicines)
- Pharmacy licensing: [Pharmacy and Poisons Act, Cap 244](https://www.cehurd.org/wp-content/uploads/2012/04/right%20to%20health%20data%20base/LAWSOF%20COUNTRIES/kenya/drugs%20and%20poisoning/Pharmacy%20and%20Poisons%20Act%20Cap244.pdf),
  [Huduma Global: starting a pharmacy in Kenya](https://hudumaglobal.com/blog/start-pharmacy-chemist-business-kenya-licensing-regulations-requirements),
  [Pharmacy and Poisons Board](https://en.wikipedia.org/wiki/Pharmacy_and_Poisons_Board). Fees differ between sources: **confirm** with the PPB
- Medicine prices unregulated since 1994: [The Standard: steep price of drugs in Kenya](https://www.standardmedia.co.ke/business/wednesday-life/article/2000214985/concern-over-steep-price-of-pharmaceutical-drugs-in-kenya)
- VAT on medicines (exempt; inputs changed by the Finance Act 2026, **confirm**): [KPMG: Finance Act 2025 analysis](https://assets.kpmg.com/content/dam/kpmg/ke/pdf/tax/2025/Finance_Act%202025_KPMG_Analysis.pdf),
  [Kenyans.co.ke: KAM on the proposed VAT change](https://www.kenyans.co.ke/news/112670-kam-warns-5-rise-medicine-prices-over-proposed-vat-change-finance-bill-2025),
  [Capital FM: manufacturers on delayed VAT exemptions, September 2026](https://capitalfm.africa/pharmaceutical-manufacturers-warn-of-disruptions-over-delayed-vat-exemptions/)
- Minimart tax, licences and weights and measures: as in [MINIMART_ONBOARDING.md](MINIMART_ONBOARDING.md#sources-checked-october-2026)

Not found in public sources: current shelf prices for named medicines at Kenyan chemists, and the usual mark-ups chemists apply to
brands and generics. The figures here are demo settings; ask the prospect.
