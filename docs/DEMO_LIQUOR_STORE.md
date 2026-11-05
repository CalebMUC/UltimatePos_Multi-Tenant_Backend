# Demo account: a wines and spirits shop (Kenya)

How to build a demo business in Ultimate POS that looks and behaves like a liquor store already trading, and how to walk a prospect
through it:

- **Baridi Wines & Spirits (Demo)**: an off-licence on Kamiti Road, Zimmerman (Nairobi). It sells beer by the bottle, the can and the
  crate; spirits by the quarter (250 ml) and the bottle; wine; mixers, ice, snacks and cigarettes. It tops up two bars and a members'
  club when their distributor is late, supplies an events caterer, and delivers to the apartments at night by boda.

It is built by a script in about a minute (§3), and a second script gives it a full day of trading (§3.6). The catalogue is in
[docs/demo-data/](demo-data/). §1 explains how these shops work and **what goes wrong in them**; §1.2 sets each problem against what the
system does about it. What was built for this demo is in §6.

This guide follows [DEMO_MINIMART_AND_CHEMIST.md](DEMO_MINIMART_AND_CHEMIST.md) and
[DEMO_HARDWARE_AND_ELECTRICAL.md](DEMO_HARDWARE_AND_ELECTRICAL.md); the hardware guide's sections on personalising a demo (§3.10) and
after the demo (§7) apply unchanged.

> **Prices are demo figures.** Beer and the mainstream spirits are set from online prices seen in 2026 and the brewer's last published
> spirits prices (see Sources), rounded to shelf prices; the rest are estimates with margins typical of each kind of drink. Brewer and
> distributor prices change with every excise adjustment. Before each demo, ask the prospect for 10 to 15 of their own prices.

> **The law here is summarised, not quoted.** The Alcoholic Drinks Control Act, 2010 and each county's own act set the rules, and the
> 2025 national policy proposes changes not yet law (§1.3). Confirm with the prospect's county before saying what their licence allows.

---

## 1. How a wines and spirits shop runs

### 1.1 The trade

| Topic | How it works | What the demo must show |
|---|---|---|
| **Customers** | Walk-ins after work, mostly men 25–45, buying one or two beers, a quarter of cane spirit or gin with a mixer; weekend buyers taking a crate home; bars and clubs topping up when their distributor is late; events caterers buying by the case for weddings and parties; companies buying for staff parties on an LPO; apartments ordering by WhatsApp at night. | Fast single sales, crates at the wholesale price, an events order on account, night deliveries |
| **The day** | Quiet until mid-afternoon; a rush from about 5 pm to 10 pm; Friday and month-end are the big nights. | The till keeps up with the evening queue |
| **How drinks are sold** | Beer by the bottle (returnable glass), the can, the crate of 25 or the carton of 24 cans. Spirits by the **quarter (250 ml)**, the 750 ml bottle and, for events, the case of 12. Wine by the bottle or the case. Ice by the bag. Cigarettes by the packet (single sticks may not be sold). | One product sold by the bottle and the crate at different prices |
| **Empties and deposits** | Beer comes in returnable bottles and crates. A customer who brings empties pays for the drink only; one who does not pays a deposit for the bottle or the crate and gets it back when they return them. The distributor exchanges full crates for empty ones. | The crate's deposit added when the customer has no empties; empties bought back |
| **Margins** | Mark-ups on cost: thin on beer (8–12%), because the brewer's distributor sets the price and every shop on the road sells the same crate; mainstream spirits 15–20%, premium spirits and liqueurs about 22%, wine around 30%, mixers 25%, ice 60%, cigarettes under 10%. | The margin report: beer brings people in, spirits and wine pay the rent |
| **Suppliers** | The brewer's appointed distributor (beer, the brewer's spirits and ready-to-drinks), paid before the lorry leaves; a wines and spirits wholesaler for imports and wine, on 14–30 days; a soft-drinks depot; an ice supplier, cash; a tobacco distributor. | Receiving the lorry against its invoice, supplier balances |
| **Losses** | Broken bottles; spirits that walk (small, valuable, easy to pocket); a cashier who sells a quarter and keeps the cash; counterfeit stock bought cheap "from a friend". | Every write-off with a reason and a name; a cashier who cannot adjust stock |
| **Deliveries** | WhatsApp orders to the estates at night, by boda; the rider collects M-Pesa at the door and should see an ID. | A delivery order, the rider's ID check, the hours |

### 1.2 The challenges, and what the system does about each

| # | Challenge | What happens today | What Ultimate POS does | Status |
|---|---|---|---|---|
| 1 | **Selling to minors.** Selling alcohol to anyone under 18 carries a fine of up to KES 150,000, up to a year in prison, or both. NACADA says the seller must demand proof of age, and it raids outlets that serve minors. | The owner tells staff to check IDs and hopes they do. Nothing proves they did. | Every alcohol and tobacco product is **age-restricted**. The till will not complete the sale until the cashier taps how the age was checked: national ID, passport, driving licence, other ID, or a regular whose ID was seen before. The receipt prints it. The **age-check register** lists every such sale, the method and the cashier. | **Built** (LQ2, LQ4) |
| 2 | **Selling outside the licence hours.** The licence and the county act set the hours; selling outside them risks the licence. Hours differ by county and between weekdays and weekends. | A rule on the wall. | The owner enters the licence hours (weekday and weekend, past midnight if allowed). Outside them the till **refuses** alcohol and tobacco, and a delivery cannot leave with a rider; soda and snacks still sell. | **Built** (LQ3). Public holidays use the weekday hours (LQ10) |
| 3 | **Counterfeit and illicit alcohol.** An estimated 60% of alcohol drunk in Kenya by volume in 2024 was illicit. KRA and NACADA seize bottles with fake or swapped excise stamps, and attendants of shops holding them have been arrested. | Stock bought from "a guy" at a good price. | Stock comes in only on a purchase order, from a named supplier, against the supplier's invoice number. Overselling is off, so a bottle sold that was never received shows up at once. The owner can show where every bottle came from. | **Built** (existing). Recording the excise-stamp check at receiving is LQ6 |
| 4 | **Theft and shrinkage.** Quarters and premium bottles are small and valuable. | The owner counts the shelf and argues with staff. | Overselling is **off** by default in this sector: the system knows how many bottles there should be. Only a manager can adjust stock, always with a reason and a name. Voids need a manager. The cash count shows the variance the same night. | **Built** (existing, sector default LQ1). A count sheet for a nightly spirits count is LQ8 |
| 5 | **Empties and deposits.** Crates and bottles are money: deposits taken, deposits refunded, crates owed by bars. | A notebook, or nothing. | A crate of beer carries its crate-and-empties deposit as a **container**, added at the till when the customer brings no empties (the refill station's mechanism). The single-bottle deposit is a line of its own. Deposits refunded come out of the drawer as a recorded pay-out, and the empties go back into stock. | **Built** (existing containers, LQ1). A deposit-refund document and crates owed per bar are LQ7 |
| 6 | **Cash at night.** The till fills up in the evening, and the shop closes late. | The cash goes home in a pocket. | Cash drops to the safe recorded during the session; M-Pesa for most sales; the drawer counted at close with its variance. | **Built** (existing) |
| 7 | **Drinkers on credit (deni).** Credit to individuals for alcohol is rarely repaid. | "Nitakulipa Friday." | The demo gives credit only to businesses (bars, the caterer, a Sacco), each with a limit; going over the limit needs a manager. No monthly-spend reward is set on alcohol. | **Built** (existing) |
| 8 | **Prices that move.** Excise duty is adjusted every year for inflation and the brewer passes it on; distributors change prices often. | Price stickers redone by hand; cashiers charging old prices. | Prices have an effective date, so a new price list can be loaded ahead and takes effect at the right time; the cashier cannot change a price. | **Built** (existing). A price-list upload is M2 |
| 9 | **Licences and inspections.** The county liquor licence (renewed every year), the business permit and, in Nairobi, the county's new LiquorPay licensing system, which it says is mandatory by December 2026. Traders also complain of inspectors demanding bribes. | Papers in a drawer. | The licence fee is an expense under *Licences & Permits*. When an inspector asks, the owner shows the age-check register and the purchase invoices for the stock on the shelf. | Records **built**; a licence-renewal reminder is LQ9 |
| 10 | **Expired ready-to-drinks.** Ciders and ready-to-drinks carry a best-before date and move slowly; NACADA found expired drinks in its 2026 raids. | Found when a customer complains. | Ciders and ready-to-drinks are **batch-tracked**: received with their best-before date, sold oldest first, on the expiry report before they expire. | **Built** (existing batches) |
| 11 | **Rules that may change.** The 2025 national policy proposes raising the drinking age to 21, banning sales within 300 m of schools, and banning alcohol in supermarkets. These need new laws, which had not passed when this guide was written. | Uncertainty. | The **minimum age is a setting** (18 today), so a change to 21 is one field, and the till asks for the new age at once. | **Built** (LQ3) |

### 1.3 What wins this owner over

1. **"If a child buys beer here, I lose my licence."** The age check at the till, the age-check register, the hours enforced.
2. **"Bottles disappear."** Overselling off, every adjustment with a name, the cashier's screen without a stock button, the cash variance.
3. **"I don't know where my empties are."** The crate deposit at the till, refunds recorded.
4. **"The bars owe me."** Customer balances, limits, ageing.
5. **"Is my stock genuine?"** Every bottle traces to a supplier's invoice.
6. **"I can't be here every night."** The dashboard on the phone; the register shows who sold what to whom, and how they checked.

---

## 2. The demo business

| | Liquor store demo |
|---|---|
| Business name | Baridi Wines & Spirits (Demo) |
| Trading name | Baridi Wines & Spirits |
| Sector | `Liquor` (8) |
| Business type / kind | Retailer (1) / Trader (1) |
| County / address | Nairobi, Kamiti Road, Zimmerman |
| KRA PIN | `P000000005W` (dummy) |
| VAT registered | **Yes**: a busy wines and spirits shop sells well over KES 5 million a year. Costs are loaded without VAT |
| M-Pesa till | `000555` |
| Max cashier discount | 5% |
| Monthly-spend reward | **Off**, on purpose: a discount for drinking more is not something to demo |
| Allow negative stock | **No** (the sector default): every bottle is counted |
| Age-restricted sales | 18 and over; sold **10:00–23:00** every day so the demo works in daytime. Set the hours on the prospect's licence (Settings → Age-restricted sales) |
| eTIMS | **Off** |
| Licence | Complimentary, 12 months |

Alcohol is standard-rated for VAT; excise duty is already in the distributor's price, so the shop charges none of its own. Deposits are
entered as **exempt** (a refundable deposit is not a sale of the crate): the accountant confirms this.

---

## 3. Building the demo

### 3.1 The scripts

| Script | What it does | Time |
|---|---|---|
| [setup_liquor_demo.py](demo-data/setup_liquor_demo.py) | Registers Baridi Wines & Spirits and its complimentary licence; the minimum age and selling hours; units, expense categories, categories, 101 products with crates, cartons and cases and their prices; the crate deposit on each crate; 75 products age-restricted, 5 batch-tracked; 5 suppliers; 5 customers; 3 bundles; the opening stock (one purchase order per supplier, received against an invoice, ciders in dated batches); roles and three staff | ~1 min |
| [practice_sales_liquor.py](demo-data/practice_sales_liquor.py) | One day of trading (§3.6). Must run within the selling hours | ~1 min |

```bash
pip install requests openpyxl
python docs/demo-data/setup_liquor_demo.py
python docs/demo-data/practice_sales_liquor.py      # once a day, on the days before the demo, between 10:00 and 23:00
```

They share [demo_setup.py](demo-data/demo_setup.py) and [demo_practice.py](demo-data/demo_practice.py) with the other demos: they
refuse an API that is not on localhost unless given `--allow-remote` (a demo or staging environment, never production), skip what
already exists, and take `--owner-password` once the owner has changed it.

| Who | Sign-in (first time) | Can |
|---|---|---|
| Owner | `demo.liquor@ultimatepos.local` / `BaridiDemo@2026` | Everything |
| Manager | `manager.lq@ultimatepos.local` / `BaridiStaff2026` | Prices, receiving, voids, returns, write-offs, the day's cash, deliveries and credit to bars, reports and the age-check register |
| Cashier | `cashier.lq@ultimatepos.local` / `BaridiStaff2026` | The till with the age check, payments from bars, delivery orders. **Not** prices, voids or stock |
| Storekeeper | `store.lq@ultimatepos.local` / `BaridiStaff2026` | Receiving the lorry, stock levels |

Staff must change their password at first sign-in: sign in as each one the day before (§3.7).

### 3.2 What the catalogue contains

Generated by [build_demo_catalogues.py](demo-data/build_demo_catalogues.py):
`python docs/demo-data/build_demo_catalogues.py docs/demo-data liquor` rebuilds this one only. Columns A–J are the bulk-upload layout;
the yellow build columns add cost, opening quantity, supplier, the pack and its prices, **the container sold with the pack** (the crate's
deposit), batch tracking and **age-restricted**. The upload cannot take those yet (gap M2); the setup script sets them after it.

| | |
|---|---|
| Departments → categories | 8 → 21: Beer & Cider (lager, stout & malt, cider & ready-to-drink), Spirits (whisky, vodka, gin, brandy & cognac, rum & cane, liqueurs), Wine (red, white & rosé, sparkling), Mixers & Soft Drinks (sodas & tonics, water, energy, ice), Snacks, Tobacco, Accessories & Bags, Empties & Deposits |
| Products | 101, from Balozi to Moët; 250 ml quarters of the cane spirits, vodkas, gins, whiskies and brandies that sell most |
| Packs | 34: **6 crates of 25 returnable bottles, each with the crate deposit as its container**; cartons of 24 cans, quarters and ready-to-drinks; cases of 12 for the spirits and wines events buy; cartons of soda and water |
| Age-restricted | 75: every beer, cider, spirit, wine and packet of cigarettes. Not soda, water, ice, snacks or deposits |
| Batch-tracked | 5 ciders and ready-to-drinks (best-before about 9 months); Snapp's first batch expires in 45 days so the expiry report has something to show |
| Deposits | *Crate & 25 Empties (deposit)* KES 1,200 and *Empty Beer Bottle 500ml (deposit)* KES 30: demo figures, confirm with the distributor |
| Opening stock at cost, without VAT | ≈ KES 1,243,000: the brewer's distributor 667,000, the wines and spirits wholesaler 471,000, soft drinks 45,000, snacks and ice 27,000, tobacco 34,000 |
| Wholesale price | Cost + 60% of the retail margin (bars and the caterer) |

**Barcodes.** As in the other demos the catalogue has none: buy or borrow 10 common items (a Tusker can, a Kenya Cane quarter, a
Gilbey's 750 ml, a Coca-Cola 500 ml), scan each into its product's Barcode field and scan them at the till in front of the prospect.

### 3.3 Suppliers and customers

Names are fictitious on purpose; brands are real, as they are on the shelves.

| Supplier | Supplies | Terms |
|---|---|---|
| Thika Road Beverages Ltd | The brewer's distributor: beer, ciders, the brewer's spirits | Paid before the lorry leaves |
| Mlimani Wines & Spirits Wholesalers | Imported spirits, wine, other local spirits | 14 days, half paid |
| Roysambu Soft Drinks Depot | Sodas, tonics, water, energy drinks | 7 days, paid |
| Githurai Snacks & Ice Supplies | Ice, snacks, cups, bags | Cash |
| Kahawa Tobacco Distributors | Cigarettes | Cash |

| Customer | Price | Credit limit | Why it is in the demo |
|---|---|---|---|
| Kona Bar & Grill (Zimmerman) | **Wholesale** | 60,000 | A bar topping up crates when its distributor is late; pays each top-up on the next |
| Wanjiru Events & Catering | **Wholesale** | 150,000 | Weddings: crates, cases of wine and whisky, ice, delivered on account |
| Thika Road Sacco (staff events) | Retail | 40,000 | A company on an LPO |
| Sunset Gardens Apartments (deliveries) | Retail | — | Night WhatsApp orders, paid by M-Pesa at the door |
| Roysambu Members Club | **Wholesale** | 80,000 | A club buying by the case |

No individual has credit: a deni book for drinkers is a problem this shop should not have.

### 3.4 Bundles

| Bundle | Parts | Price |
|---|---|---|
| Sundowner Gin Pack | Gilbey's Gin 750 ml + 2 × Schweppes Tonic 500 ml + ice 2 kg | KES 1,550 |
| Whisky Night Pack | Johnnie Walker Red Label 750 ml + 2 × Coca-Cola 1.25 L + ice 2 kg | KES 2,650 |
| Match Day Pack | 6 Tusker cans + 6 Guinness Smooth cans + 2 groundnuts | KES 3,200 |

A bundle with alcohol in it asks for the age check like any other sale.

### 3.5 Empties at the till

- **A crate of beer**: pick the crate unit. If the customer brings a crate of 25 empties, the till sells the beer only. If not, it adds
  *Crate & 25 Empties (deposit)* at KES 1,200 (the *Customer brought a crate of empties?* toggle, the containers module).
- **A single bottle**: the customer swaps an empty, or pays *Empty Beer Bottle 500ml (deposit)* (KES 30) as a line of its own.
- **Empties brought back**: refund the deposit from the drawer (*Pay out*, reason "Deposit refunded: 6 empties") and add the empties
  back to stock with a stock adjustment. Two steps today; one document is LQ7.
- **The lorry**: full crates exchanged for empty ones, so no deposit is charged on the restock.

### 3.6 A day of trading

Run the practice script **once a day for three days before the demo**, between 10:00 and 23:00. It stops before it starts if the
hours are closed, and it cannot be run twice on the same day (the session closes and the M-Pesa codes repeat).

| | |
|---|---|
| Opening | KES 5,000 float |
| Deliveries | The brewer's lorry (crates, cans, quarters, ready-to-drinks in a dated batch) paid by bank transfer; the ice van paid cash from the till |
| The age check | A beer sale without it is refused (the script checks this); every other sale of alcohol records a method: national ID about half the time, a known regular about a third, the rest driving licence, passport, other ID |
| Sales | About 80: an afternoon of quarters, sodas and cigarettes; the evening rush of single beers (some with the bottle deposit), cans, quarters with mixers and ice, wine and spirits bottles; the three bundles; a walk-in crate with its deposit; M-Pesa about 60% |
| On account | Kona Bar pays its last top-up and takes 3 crates and a carton of quarters at wholesale; the Sacco takes gin and tonic on an LPO; Wanjiru Events' wedding order (3 crates with deposits, a case of wine, whisky, ice, soda) delivered on 14 days |
| Deliveries | A night order to Sunset Gardens delivered with the rider's ID check; another left waiting for a rider for the demo |
| Exceptions | 6 empties and a crate returned, deposits refunded from the drawer; a broken Kenya Cane 750 ml written off; a Chrome Vodka quarter short at the count written off; a Hennessy sale voided; KES 15,000 dropped to the safe |
| Close | KES 200 short |
| Expenses | Rent, power for the coolers, water, salaries, the night guard, M-Pesa and bank charges, boda riders, carrier bags; the county liquor licence, the business permit and a cooler service in the first month |

### 3.7 Checklist the day before

- [ ] A practice day run today (the register and the dashboard have today's sales).
- [ ] Sign in as owner, manager and cashier: each changes the password and sees the right menus.
- [ ] 10 real items with their barcodes scanned in, and the scanner works at the till.
- [ ] A cash session open with a float.
- [ ] The prospect's licence hours ready to type in (scene 3), and their county's minimum age (18).
- [ ] The 80 mm printer prints a receipt with *Age checked*.
- [ ] The dashboard open on a phone, signed in as the owner.
- [ ] The prospect's prices, name and logo in (hardware guide §3.10).

---

## 4. The demo script

About 35 minutes. Follow the evening (§1.1). Let the owner ring up a sale.

| # | Scene | What you do | What to say |
|---|---|---|---|
| 1 | Opening | Open the session with the float | "Every night starts with the float and ends with a count." |
| 2 | The lorry | As storekeeper, receive the distributor's order against its invoice; the ready-to-drinks ask for their best-before date | "Every bottle on your shelf came on an invoice from a named supplier. When KRA asks where it came from, this is the answer." |
| 3 | Licence hours | As owner, Settings → Age-restricted sales: type the prospect's licence hours. Set them to start in an hour; try to sell a beer: refused, the soda still sells. Put them back | "Outside your licence hours, the till will not sell alcohol. Not even if you are not there." |
| 4 | The age check | A Tusker and a quarter: *Pay* asks how the age was checked. Tap *National ID*; the receipt prints it | "Your cashier cannot finish the sale without checking. And you can see that they did." |
| 5 | A crate | A crate of Tusker for a walk-in with no empties: the deposit is added; with empties, it is not | "Your empties are money. The system keeps track of them." |
| 6 | Empties back | Refund 6 empties from the drawer; the empties go back into stock | "The deposit you give back is recorded, not just taken from the drawer." |
| 7 | The bar | Kona Bar: 2 crates at the wholesale price, on account; try to go over the limit: refused | "Your bar price is applied by itself, and the bar cannot run up more than you allow." |
| 8 | Party pack | The Sundowner Gin Pack as one line; it still asks for the age check | "One tap for the party pack, and each bottle leaves its own stock." |
| 9 | Night delivery | The Sunset Gardens order: send it with the rider; record it delivered, with the rider's ID check and the M-Pesa code | "Even at the door, the rider checks the ID, and you know he did." |
| 10 | Cashier's screen | Sign in as the cashier: no price field, no stock adjustment, no void | "Your staff cannot change a price or make a bottle disappear." |
| 11 | Losses | As manager, write off a broken bottle with its reason; the quarter short at the count | "Every bottle that goes missing has a reason and a name." |
| 12 | The register | Reports → Age-check register: today's sales, the method, the cashier; the share of *regulars* per cashier | "When the county inspector or NACADA asks, this is your book. And if one cashier marks everyone a regular, you will see it." |
| 13 | Owner's view | Dashboard; margins as a share of the price (beer about 9%, spirits 14–18%, wine about 23%); low stock; customer ageing (the bars, the caterer); profit and loss | "This is your shop every evening, from your phone." |
| 14 | Closing | Close the session, count the drawer | "Short or over, you know tonight." |

### 4.1 Moments that tend to close the sale

- The beer that cannot be sold outside the licence hours.
- The age-check register, with the cashier who marks everyone a regular.
- The crate deposit added by itself.
- The cashier's screen without a stock button.

---

## 5. Questions they will ask, and honest answers

| Question | Answer today | Code change |
|---|---|---|
| "Can it scan the ID?" | No, and it keeps no ID numbers: the cashier taps how the age was checked. That is the record the law asks for without holding customers' personal data. | None planned |
| "Can it check the excise stamp?" | Not inside the system. Check stamps with KRA's app at receiving; the system records the supplier and the invoice for every bottle. | LQ6 |
| "Who owes me crates?" | Deposits are paid and refunded at the till; there is no running count of crates per bar yet. | LQ7 |
| "Can I count the spirits every night?" | Count the shelf and record each difference as an adjustment with a reason. There is no count sheet yet. | LQ8 |
| "Will it remind me to renew the licence?" | Not yet; the fee is recorded as an expense. | LQ9 |
| "What about public holidays?" | The weekday hours apply on a holiday. | LQ10 |
| "Can a customer pay part cash, part M-Pesa?" | Not on one sale yet. | G2 |
| "Can I return empties to the distributor in the system?" | Not as one document: adjust them out with a reason and agree the credit with the distributor. | M4 |
| "The lorry comes without an order." | Raise the order and receive it at once (two taps); one step is G8. | G8 |
| "Can I upload my price list when excise changes?" | Prices change per product with an effective date; a price-list upload is not there yet. | M2 |
| "What if the internet goes?" | The till needs the internet; keep a phone hotspot as backup. | M13 |
| "Does it work with eTIMS?" | Yes, set up at onboarding with the shop's own PIN and device. | None; do not show it on a demo PIN |
| "What if the age goes to 21?" | One setting. | None |

---

## 6. Code changes

### Built for this demo (4 November 2026)

| # | Change | Where |
|---|---|---|
| **LQ1** | **Liquor sector** (`BusinessSector.Liquor` = 8): kind Trader, **overselling off**; Bottle (`btl`), Crate, Carton and Case units; Packaging and Refrigeration expense categories; modules batches, deliveries, containers, bundles and `ageRestricted` | `BusinessSector`, `BusinessDefaults.Sectors[Liquor]`, `BusinessModules.BySector[Liquor]` |
| **LQ2** | **Age-restricted products and the age check**: `Product.IsAgeRestricted`; a sale with such a product needs `ageCheck` (`AgeCheckMethod`: NationalId, Passport, DrivingLicence, OtherId, KnownCustomer) or is refused with **422 `AGE_CHECK_REQUIRED`**; the method is stored on the sale and returned on `SaleDto.ageCheck`; the preview says `requiresAgeCheck`, `minimumAge`. Also on recording a delivery (`DeliverDeliveryOrderRequestDto.ageCheck`). Bundles are covered through their parts. Updating a product without `isAgeRestricted` keeps it | `SalesService.CheckAge`, `CatalogService`, `DeliveryOrders.cs` |
| **LQ3** | **Selling hours and the minimum age**: `PUT /businesses/current/age-restricted-sales` (`Businesses.Update`): minimum age 18–25, weekday and weekend windows, past midnight allowed, East Africa Time. Outside them a sale of age-restricted products is refused with **422 `OUTSIDE_SELLING_HOURS`**, at the till and when a delivery goes out with a rider (not when the rider is back). Times are read as "HH:mm" (`ClockTimeJsonConverter`) | `SellingHours`, `SalesService.EnsureWithinSellingHours`, `BusinessService.UpdateAgeRestrictedSalesAsync` |
| **LQ4** | **Age-check register**: `GET /sales/age-checks?from&to` (`SalesReports.View`): each sale of alcohol or tobacco, the method, the cashier, the age-restricted lines | `SalesService.GetAgeCheckRegisterAsync` |
| **LQ5** | **`ageRestricted` module** for General, Minimart and Supermarket too (cigarettes, and beer where they are licensed) | `BusinessModules` |

**Migration** `LiquorAgeRestrictedSales`: `Products.IsAgeRestricted` (false), `Sales.AgeCheck` (null), `Businesses.MinimumPurchaseAge`
(**18** for every existing business) and four nullable selling-hours columns. Nothing changes for an existing business until a
product is marked age-restricted: no product is, so no sale asks for an age check. Rolling back the code leaves the columns unused.

**Tests**: `SellingHoursTests` (windows, past midnight, weekends), `BusinessSectorDefaultsTests` (the Liquor profile and modules),
`LiquorAgeRestrictedPostgresTests` (the age check at the till and on delivery, the register, the hours at the till and at dispatch,
validation, the flag kept on update). Frontend: [Frontend-Liquor-Age-Check-Prompt.md](Frontend-Liquor-Age-Check-Prompt.md), and the
module table in [Frontend-Sector-Modules-Prompt.md](Frontend-Sector-Modules-Prompt.md).

### Tier 1: what this demo will raise

Size: S = under a day, M = 1–3 days, L = a week or more.

| # | Change | Backend | Frontend | Size |
|---|---|---|---|---|
| **LQ6** | **Excise stamp check at receiving** | On a receipt line of an excisable product, `StampsVerified` (bool) and an optional note; a report of receipts not verified | A checkbox per line, *"Stamps checked with the KRA app"* | S |
| **LQ7** | **Empties: deposit refunds and crates owed** | A deposit-refund document (empties in, cash or M-Pesa out, in the session); a running count of crates and empties per customer (bars) and per supplier | *Empties back* at the till; crates owed on the customer page | M |
| **LQ8** | **Stock count sheet** | A count of chosen products (spirits nightly, everything monthly): expected, counted, difference, posted as one adjustment with a reason; who counted | Count screen, variance report | M |
| **LQ9** | **Licence renewal reminders** | Licences and permits with expiry dates on the business; a notification 30 and 7 days before | Settings → Licences | S |
| **LQ10** | **Public holidays** | A holiday calendar (national, editable) that the selling hours treat as weekends | Settings → Holidays | S |
| G2 | Split payment (existing gap) | | | M |
| M4 | Return to supplier (existing gap): empties to the distributor, damaged stock | | | M |
| G8 | Receive without an order (existing gap): the lorry | | | S |

### Tier 2: onboarding a real shop

| # | Change | Why | Size |
|---|---|---|---|
| M2 | Bulk upload with barcode, cost, opening quantity, packs, containers and age-restricted | 300–1,500 items at a real shop; the build sheet is already in this shape | M |
| M3 | Pack barcodes | A crate or carton scanned as a crate | M |
| M7 | Time-limited promotions | Weekend offers on wine, festive season | M |
| M11 | Sales and margin by category | "How much did spirits make this month?" | S–M |
| M13 | Offline till | Power cuts in the evening rush | L |

**Recommended order** for the first liquor client: LQ8 and LQ7 (the owner's biggest losses are bottles and empties), then LQ6 and G8.

---

## 7. After the demo

As in the hardware guide §7: put the name and logo back, put the selling hours back to 10:00–23:00, leave the business for the next
demo, and note what the prospect asked for in §5 that is not built.

---

## Sources (checked November 2026)

- Hours and licences: [Alcoholic Drinks Control (Licensing) Regulations, 2010](https://new.kenyalaw.org/akn/ke/act/ln/2010/206) (national
  hours by licence; counties set their own), [Nyandarua County Alcoholic Drinks Control Act, 2024](https://www.slo-countybills.go.ke/api/v1/bill/pdf/8M5ISP4N7W_Nyandarua-1-2024_THE%20%20NYANDARUA%20COUNTY%20ALCOHOLIC%20DRINKS%20CONTROL%20ACT,%202024.pdf),
  [Venas News: bar and wines and spirits licences, 2025](https://venasnews.co.ke/2025/03/19/bar-and-wines-and-spirits-licenses-in-kenya/).
  Sources disagree on the national hours for a retail wines and spirits licence: **confirm** with the county
- Minors and proof of age: [Eastleigh Voice: NACADA on minors buying alcohol](https://eastleighvoice.co.ke/alcohol%20consumption/98191/nacada-raises-alarm-over-minors-buying-alcohol-from-petrol-stations-in-nairobi),
  [People Daily: NACADA warns stores selling to children](https://peopledaily.digital/news/nacada-warns-stores-selling-alcohol-drugs-to-children).
  The section numbers differ between reports: **confirm** against the Act on Kenya Law
- The 2025 policy (age 21, supermarkets, 300 m from schools): [The Star, 30 July 2025](https://www.the-star.co.ke/news/2025-07-30-state-to-set-drinking-age-at-21-and-ban-alcohol-sale-in-supermarkets-in-new-policy),
  [The Star, 13 January 2026](https://www.the-star.co.ke/news/2026-01-13-murkomen-state-to-raise-drinking-age-from-18-to-21),
  [Nation: proposed rules rattle traders](https://nation.africa/kenya/news/proposed-alcohol-rules-brew-storm-rattle-traders-5137746)
- Nairobi: [The Star: City Hall's alcohol bill, February 2026](https://www.the-star.co.ke/counties/nairobi/2026-02-22-city-hall-cracks-down-on-alcohol-trade-in-sweeping-new-bill),
  [The Star: LiquorPay digital licensing, May 2026](https://www.the-star.co.ke/news/2026-05-25-nairobi-county-intensifies-crackdown-on-illicit-brew),
  [The Star: new liquor board, May 2026](https://www.the-star.co.ke/counties/nairobi/2026-05-05-nairobi-unveils-new-liquor-board-warns-crackdown-on-illegal-alcohol-trade)
- Counterfeit and illicit alcohol: [Kenyans.co.ke: KRA destroys fake goods, July 2026](https://www.kenyans.co.ke/news/124851-kra-destroys-fake-goods-worth-ksh-218m-nairobi-and-mombasa),
  [TNX Africa: NACADA arrests 11 in Nairobi, August 2026](https://tnx.africa/newsbeat/article/2001556771/nacada-arrests-11-seizes-counterfeit-alcohol-in-nairobi-crackdown),
  [The Kenya Times: NACADA crackdown on counterfeit networks](https://thekenyatimes.com/national/nacada-launches-crackdown-on-counterfeit-alcohol-networks-in-nairobi/),
  [Nation: how fake alcohol syndicates operate](https://nation.africa/kenya/news/how-fake-alcohol-syndicates-operate-underground-network-5575034),
  [Food Business MEA: Kenya steps up the fight against counterfeit drinks](https://www.foodbusinessmea.com/?p=2110829),
  [Standard: traders accuse officials of extortion](https://www.standardmedia.co.ke/business/amp/nairobi/article/2001493273/alcohol-traders-accuse-state-officials-of-extortion)
- Prices: [Soys: Tusker Lager 24 cans, October 2026](https://soys.co.ke/products/tusker-lager-500ml-24-cans-DEKQE.html),
  [Carrefour Kenya](https://www.carrefour.ke/mafken/en/c/54866), [Oaks & Corks: Tusker 500 ml](https://www.oaks.delivery/product/tusker-lager-500ml/),
  [Drinks Vine: Tusker](https://drinksvine.co.ke/product/tusker-lager), [Kenyans.co.ke: KBL cuts Chrome prices](https://www.kenyans.co.ke/news/106503-kbl-reduces-prices-chrome-gin-vodka-2-other-alcohols),
  [Capital FM: KBL spirits price cuts, November 2024](https://www.capitalfm.co.ke/business/2024/11/kbl-announces-price-cuts-on-mainstream-spirits-ahead-of-the-festive-season)
- Excise adjusted for inflation every year: [Business Daily: EABL on excise](https://www.businessdailyafrica.com/bd/markets/market-news/eabl-to-raise-beer-price-by-sh20-if-treasury-ups-tax-2247178)
- Returnable bottles: [Food Business MEA: KBL's returnable spirits bottles](https://www.foodbusinessmea.com/?p=2107915)

Not found in public sources: current deposits for a crate and a bottle, retail margins on beer and spirits, and the 2026 hours in
Nairobi's county law. The figures here are demo settings; ask the prospect.
