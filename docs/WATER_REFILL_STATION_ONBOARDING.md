# Onboarding a water refilling station (Kiambu, Kenya)

How to set up a water refilling station in Ultimate POS. The station buys water from vendors and refills containers in 1, 10 and 20
litre quantities. Almost everything is **configuration of existing features**; the backend changes this needed are listed in
[§13](#13-backend-changes-made-for-this-onboarding). Missing features are in [BACKEND_API_GAPS.md](BACKEND_API_GAPS.md). Changes the
frontend needs are in [§12](#12-frontend-changes-required).

Nothing here is specific to water in code. A refill station is a **Trader** that sells **one product held in litres** in three pack
sizes, with **overselling switched off**. Any business that sells one stock in several sizes (fuel, cooking oil, flour, cereals) can
be set up the same way.

All routes are under `/api/v1`. Request bodies are JSON, and **enums are sent as numbers** (the API has no string-enum converter).
The numbers used below are given with their names.

---

## 1. Owner checklist — what we must get from the business

Nothing below is invented. Where a value is not known yet, leave it out; do not enter a placeholder.

| # | Item | Needed for | Status |
|---|------|-----------|--------|
| 1 | Legal business name and trading name | Registration, receipts | ☐ Owner |
| 2 | KRA PIN (required by the API, unique) | Registration | ☐ Owner |
| 3 | Physical address (county: Kiambu) and phone | Registration, receipts | ☐ Owner |
| 4 | **VAT registered?** | `isVatRegistered`. Decides whether VAT appears on sales | ☐ Owner / accountant |
| 5 | **Tax class of refilled water** (Standard 16%, Zero-rated or Exempt) | Product `taxClassification` | ☐ **Accountant must confirm.** Not assumed. |
| 6 | M-Pesa till or paybill number | `mpesaShortCode` (till payments are matched to sales) | ☐ Owner |
| 7 | Selling price for 1L, 10L and 20L (KES, VAT-inclusive if VAT-registered) | Price tiers | ☐ Owner |
| 8 | Any cheaper price for regular or bulk customers | Wholesale price tier, plus customers set to Wholesale | ☐ Owner (optional) |
| 9 | Maximum discount a cashier may give (%) | `maxDiscountPercent` | ☐ Owner (optional) |
| 10 | Opening stock in litres on go-live day, and what it cost per litre | Opening stock adjustment | ☐ Owner, physical count |
| 11 | Reorder level in litres (when to order more) | Product `reorderLevel` | ☐ Owner |
| 12 | Water vendors: name, phone, KRA PIN, payment terms (days), usual price per litre | Suppliers, purchase orders | ☐ Owner |
| 13 | Staff who will use the system, and what each may do | Users and roles (§9) | ☐ Owner |
| 14 | Container sizes sold, their price (e.g. 20L container 500), and how many are in stock | Container products (§3.4) | ☐ Owner |
| 15 | Customer credit: who may buy on account, and up to what limit | Customer `creditLimit` | ☐ Owner |
| 16 | ~~Containers: deposits?~~ | **Answered 2026-10-19:** customers bring their own (pay for water only), or buy a container with the water. Never lent on deposit. | ✔ |
| 17 | ~~Deliveries?~~ | **Answered 2026-10-19:** yes, free. Customer orders → rider delivers → customer pays cash, at the till, or promises a date. The cashier records it when the rider is back; only a manager may accept a promise to pay. | ✔ (§6.1) |
| 18 | Riders' names | Recorded on each delivery (riders do not sign in) | ☐ Owner |
| 19 | Water-quality testing, filter and tank-cleaning schedule to record? | Maintenance log (gap) | ☐ Owner (§11) |

---

## 2. Business configuration

| Setting | Value | Notes |
|---|---|---|
| **Sector** | **`WaterRefill`** | What line of business it is in. Sets the starting defaults below (Trader, overselling off, `10L`/`20L` units, filter, sanitation and water-testing expense categories). Set and changed by super users only |
| Business type | `Retailer` (1) | Sells to the public at retail prices |
| Kind | `Trader` (1) | Buys finished goods (water) to resell; a Trader may only purchase `FinishedGood` items |
| County | Kiambu | |
| Currency | KES | Implicit: the platform is KES-only |
| Timezone | Africa/Nairobi (UTC+3) | Built in (`ReportPeriod.DefaultUtcOffsetMinutes = 180`); Kenya has no daylight saving |
| **Allow negative stock** | **`false`** | **The station never sells more water than it holds** (§13) |
| VAT, M-Pesa short code, discount cap | From the checklist | |

A super user registers the business (`Businesses.Register`), and names the owner, who becomes its Admin:

```http
POST /businesses
{
  "businessName": "<legal name>", "tradingName": "<trading name>",
  "sector": 3 /*WaterRefill*/, "businessType": 1,   // kind and allowNegativeStock may be left out: the sector supplies Trader and false
  "kraPin": "<KRA PIN>", "physicalAddress": "<address>", "county": "Kiambu", "phoneNumber": "<phone>",
  "isVatRegistered": <owner/accountant>, "mpesaShortCode": "<till>", "maxDiscountPercent": <owner or null>,
  "owner": { "fullName": "<owner's name>", "email": "<owner email>", "phoneNumber": "<phone>" }
}
```

To change it later, use `PUT /businesses/{id}` (`Businesses.Update`). `allowNegativeStock` may be left out or set to `null`, which keeps
the current setting.

---

## 3. Products and units of measure

### 3.1 Why one product, not three

The station should have **one** product, *Water (refill)*, held in **litres**, and sold in three units:

| Selling unit | Symbol | Conversion to litres | Price |
|---|---|---|---|
| Litre (the base unit) | `l` | 1 | Owner's 1L price |
| 10 Litre Refill | `10L` | 10 | Owner's 10L price |
| 20 Litre Refill | `20L` | 20 | Owner's 20L price |

Every sale converts what is sold into litres and takes it off **one** balance. Two 20L, one 10L and three 1L refills take
(2 × 20) + (1 × 10) + (3 × 1) = **53 L** in one sale, priced per size. Three separate products would mean three separate stock balances,
none of them the real amount of water in the tank, and each could sell water the others had already sold. The sales report still
breaks sales down by size ([§10](#10-dashboard-and-reports)).

### 3.2 Setup calls (in order, as the owner or an Admin)

1. **Units.** `Litre` / `l` exists by default in every new business. Create the two pack units:
   `POST /units-of-measure/create-unit` `{ "name": "10 Litre Refill", "symbol": "10L" }`, then the same for `20L`.
2. **Category.** `POST /categories/create-category`, for example `{ "name": "Water", "code": "WTR" }`.
3. **Product.** `POST /products/create-product`:
   `{ "name": "Water (refill)", "categoryId": "…", "itemType": 2 /*FinishedGood*/, "taxClassification": <0 Standard | 1 ZeroRated | 2 Exempt — accountant>, "baseUnitOfMeasureId": "<l>", "reorderLevel": <owner, litres>, "isBatchTracked": false }`
4. **Conversions.** `POST /products/{id}/unit-conversions` `{ "packUnitOfMeasureId": "<10L>", "conversionFactor": 10 }`, then `<20L>`
   with `20`.
5. **Prices.** `POST /products/{id}/price-tiers` once per unit:
   `{ "unitOfMeasureId": "<l | 10L | 20L>", "priceType": 1 /*Retail*/, "price": <owner>, "effectiveFrom": "<now>" }`.
   Add `priceType: 0` (Wholesale) tiers only if the owner gives regular customers a different price, and set those customers'
   `priceType` to Wholesale.

To change a price, add a new tier with a later `effectiveFrom`. Past sales keep the price they were sold at. To stop selling, use
`POST /products/{id}/deactivate`.

### 3.3 Opening stock

On go-live day, after a physical reading of the tank:
`POST /stock/adjustments` `{ "productId": "…", "unitOfMeasureId": "<l>", "quantityChange": <litres>, "reason": "Opening stock", "unitCost": <cost per litre> }`.
The cost is required: stock with no cost would make every margin wrong.

### 3.4 Containers (and other items)

The owner's rule: **a customer who brings a container pays for the refill only; a customer without one buys a container as well.**
There are no deposits.

- Create one product per container size the station sells, for example *"20L Container (empty)"*: `itemType: 2`, base unit `pcs`,
  a Retail price (e.g. 500), opening stock by count, and a reorder level. Containers are stock of their own, never mixed with water.
- **Pair each refill size with its container:** `PUT /products/{waterId}/unit-conversions/{20L conversionId}/container`
  `{ "containerProductId": "<20L container>" }` (`Products.ManageUnitConversions`; `null` clears it). It can also be given when the
  conversion is created (`containerProductId` on `POST /products/{id}/unit-conversions`). The till reads it from `GET /products/{id}`
  (`unitConversions[].containerProductId`, `…Name`, `…Sku`, `…UnitOfMeasureId`, `…UnitSymbol`, `…IsActive`), so cashiers need only
  `Products.View`. The container must be an active finished good of the same business, not batch-tracked or prescription-only.
  Pack units only: the base unit (1L) has no conversion row, so it cannot carry a container.
- **Own container:** one sale line, the refill (20L refill = 200).
- **No container:** two lines, the refill and the container (200 + 500 = 700). The container leaves container stock and the water
  leaves water stock, each at its own cost.
- **A container the customer brings is not a product**, and is never entered.

Caps, dispensers or cleaning products are set up the same way, only if the owner sells them.

---

## 4. Water supply (suppliers and purchase orders)

| Step | Call | Permission |
|---|---|---|
| Register a vendor | `POST /suppliers` | `Suppliers.Register` |
| Order water (in litres, at the price per litre) | `POST /purchase-orders` with lines `{ productId, unitOfMeasureId: <l>, quantityOrdered, unitCost }` | `PurchaseOrders.Create` |
| Record what actually arrived | `POST /purchase-orders/{id}/receive` with `{ lines: [{ purchaseOrderLineId, quantityReceived }], supplierInvoiceNumber, notes }` | `PurchaseOrders.Receive` |
| Pay the vendor | `POST /suppliers/{id}/payments` `{ amount, paymentMethod, paymentReference }` | `Suppliers.RecordPayment` |
| What is owed | `GET /suppliers/balances`, `GET /suppliers/{id}/ledger` | `Suppliers.ViewLedger` |

- **Only water received increases stock**, in litres, at the order's price. Each receipt posts a bill to the vendor's account for what
  arrived.
- **Partial deliveries:** receive what came; the order shows `PartiallyReceived`, and ordered and received quantities are both on
  the order.
- **No double counting:** receiving more than was ordered is refused. A completed order cannot be received again. Since this
  onboarding, the same receipt submitted twice at once (double-click, retry) is also received only once (§13).
- **The receiving user** is recorded on the stock movement (`createdBy`). The delivery note number goes in `supplierInvoiceNumber`.
- **Urgent water with no order:** create the order and receive it at once (two calls). It stays a normal, auditable purchase that
  owes the vendor. **Do not** use a stock adjustment for a delivery: that skips the vendor's bill.
- **Transport and delivery charges:** record them as an expense in **Transport & Fuel** (`TRN`). They then count once, as an
  operating expense. Do not also add them to the price per litre on the order, or they would be counted twice.
- **Rule for staff: pay vendors with "Pay supplier", never with "Record expense".** The water's cost already reaches profit as cost of
  goods sold when it is sold. Paying the bill as an expense counts it a second time, and the vendor's balance never clears.

Reorder levels: when stock falls to the product's `reorderLevel`, a *below reorder level* alert is raised; at zero, an *out of stock*
alert. The stock-status report shows average daily usage and days of cover. **No order is ever placed automatically.**

---

## 5. Inventory and reconciliation

Closing stock = Opening + Received − Sold − Recorded losses ± Approved adjustments.
This is exactly `GET /reports/inventory/movements?from=&to=&productId=` (columns `opening`, `received`, `sold`, `adjustedIn`,
`adjustedOut`, `closing`; sold is net of voids). Example, tested: opening 500 + received 300 − sold 420 − spillage 5 = **375 L**.

| Need | How |
|---|---|
| Spillage, wastage, leaks | `POST /stock/adjustments` with a negative `quantityChange` and a reason (e.g. "Spillage at the tap"). A reason is always required. |
| Physical count | Read the tank, compare with `GET /stock/{productId}`, adjust the difference with reason "Stock count" |
| Who adjusted, and why | `GET /reports/inventory/adjustments` |
| Approval of adjustments | No approval step exists. Give `Stock.Adjust` only to the Manager/Owner: holding the permission *is* the approval (gap G11) |
| Stock value | Weighted average cost per litre (`GET /reports/inventory/stock-status`, needs `Costing.View`) |

---

## 6. Customers and sales

| Path | How |
|---|---|
| **Walk-in** | `POST /sales` with `customerId: null`. Choose sizes, then pay by cash (`amountTendered`; change is calculated), M-Pesa till or STK push. No registration. |
| **Existing customer** | `GET /customers?search=` by name or phone. Phone numbers match as typed: `0722…`, `+254 722 …` or `722…`. Their price list (Retail/Wholesale) applies automatically. History: `GET /sales?customerId=`. Balance: `GET /customers/{id}/balance`. |
| **New customer at the counter** | `POST /customers` with name and phone only (`Customers.Register`), then `POST /sales` with the new `customerId`. One sale, linked to them. |
| **Credit** | `paymentMethod: 1` (Credit) needs a registered customer. A sale past their `creditLimit` is refused unless the user holds `Sales.OverrideCreditLimit` and sends `overrideCreditLimit: true` (logged). Payments: `POST /customers/{id}/payments`. |
| **Preview before charging** | `POST /sales/preview` returns the total, and for each line `stockSufficient`. |
| **Returns, voids** | `POST /sales/{id}/returns` (reason required); `POST /sales/{id}/void` (`Sales.Void`). Stock goes back once. |

**No overselling.** With `allowNegativeStock: false`, a sale needing more water than is held is refused with **409
`INSUFFICIENT_STOCK`**, for example *"Not enough stock for: 'Water (refill)' (50 available, 53 needed)."* The check is repeated inside
the database, so two counters selling at the same moment cannot oversell between them. **M-Pesa STK push:** stock is checked
when the prompt is sent. If the customer then pays after the tank has run dry, the sale still completes (they have paid) and the
out-of-stock alert tells the manager to count and correct.

**Payment methods at the counter** today are Cash, Credit, M-Pesa till (matched to an incoming till payment) and M-Pesa STK push.
Bank transfer at the counter and split payments are **not** available; they are deferred by the owner (gaps G1, G2). Customers
paying their account may use Cash, M-Pesa, Bank transfer or Cheque.

M-Pesa: a sale paid by STK push stays **Pending** and takes no stock until Safaricom confirms. The frontend's request is never taken
as proof of payment. A missed callback can be recovered with `POST /sales/{id}/mpesa/reconcile`. The station's till number must be
the business's (or branch's) `mpesaShortCode`, and the C2B URLs registered once by an Admin (`POST /mpesa/c2b/register-urls`).
Credentials and callback URLs are deployment configuration (`Mpesa:*` settings), not entered here.

Every sale keeps its number, date, cashier, lines (unit, quantity, litres, price, discount, VAT), total, payment method and status.
A counter sale is fulfilled when it is rung up. Deliveries go through a delivery order (§6.1) and become a sale when delivered.

**Promise to pay (credit) with a date:** any credit sale may carry `paymentDueDate`, the day the customer promised to pay by. If the
invoice is still open after that day, an *overdue promise* alert goes to users with `Customers.RecordPayment`. The alert clears once
it is paid. The customer-ageing report shows `paymentDueDate` and `isPromiseOverdue` on each open invoice.

### 6.1 Delivery orders (free delivery)

```
Customer orders ─► POST /delivery-orders            Pending: the water (and any container) is set aside for this order
                   POST /delivery-orders/{id}/dispatch { riderName }        OutForDelivery
   rider back ─►   POST /delivery-orders/{id}/deliver  { payment }          Delivered = an ordinary sale; stock leaves now, once
   or ─────────►   POST /delivery-orders/{id}/cancel   { reason }           Cancelled: the reserved stock is free again
```

1. **Order** (`DeliveryOrders.Create`): `{ customerId, deliveryAddress, contactPhone?, notes?, lines }`. The customer must be
   registered: register a new one at the phone first (`POST /customers`, name and phone).
   - Lines are the refill, and the container line only if the customer has none, e.g. `[{ water, 20L, 1 }, { 20L container, pcs, 1 }]`.
   - The order is priced at the customer's prices (`estimatedTotal`). It is refused with 409 if the water not already set aside for
     other orders is not enough.
2. **Reserved:** an open order holds its quantities back. `GET /stock` shows `quantityOnHand` (still in the tank),
   `reservedQuantity` and `sellableQuantity` (what the counter can sell). A counter sale cannot take water promised to a delivery.
3. **Dispatch** (`DeliveryOrders.Dispatch`): record the rider's name. Riders do not need an account.
4. **Deliver** (`DeliveryOrders.Deliver`), recorded by the cashier when the rider is back, at the order's branch.
   - `paymentMethod` is one of:
     - `0` Cash, with `amountTendered`; change is worked out
     - `2` MpesaTill, with `mpesaTransactionId` picked from `GET /mpesa/transactions/unmatched`
     - `1` Credit: charged to the customer's account like any credit sale. `paymentDueDate` is optional. **Needs
       `DeliveryOrders.AcceptCredit`, give it to managers only**. The credit limit
       still applies (`overrideCreditLimit` with `Sales.OverrideCreditLimit`).
   - STK push is not used for deliveries.
   - If what was handed over differed from the order (*"I have my own container after all"*), send `lines` with what was actually
     delivered; the sale is priced from those.
   - The order is linked to its sale (`saleId`). Recording the same delivery twice, even at the same moment, gives one sale.
5. **Cancel** (`DeliveryOrders.Cancel`) a called-off or failed delivery, with a reason. Nothing had left stock, so the reservation is
   simply released.
6. **Rider cash-up:** `GET /delivery-orders/summary?from=&to=`. Per rider, it shows deliveries, value, **cash to hand in**, till
   payments and credit, plus how many orders are out now. Lists: `GET /delivery-orders?status=&riderName=&customerId=&from=&to=`.

---

## 7. Expenses

The defaults every business gets already cover rent (`RENT`), salaries and wages (`SAL`), utilities including electricity and water
bills (`UTIL`), transport (`TRN`), repairs and maintenance (`REP`), licences (`LIC`), M-Pesa and bank charges (`FIN`) and
miscellaneous (`MISC`). Add the station's own:

```http
POST /expense-categories  { "code": "FILT", "name": "Filter Replacements", "description": "Filter cartridges and membranes" }
POST /expense-categories  { "code": "SAN",  "name": "Cleaning & Sanitation", "description": "Tank cleaning, sanitiser, cleaning supplies" }
POST /expense-categories  { "code": "WQT",  "name": "Water Testing & Inspection", "description": "Lab tests, inspection fees" }  -- only if the owner wants it apart from LIC
```

An expense (`POST /expenses`) records the date, category, amount (KES), description, payment method (`0` Cash, `1` M-Pesa, `2` Bank,
`3` Cheque), reference (required unless cash), a receipt link, who recorded it, and an optional payee. It is recorded as **paid**
(cash basis), and is **corrected by voiding** with a reason (`POST /expenses/{id}/void`), never by editing the amount. Expenses
never touch water stock (tested). There is no approval workflow (gap G11).

**Profit never counts a cost twice:** the water's cost reaches profit only as cost of goods sold, when it is sold. Expenses are
operating costs. See the vendor rule in §4.

---

## 8. Payments and daily reconciliation

- **Cash:** `amountTendered` must cover the total; `changeDue` is returned and stored.
- **M-Pesa till:** the customer pays the till; the cashier picks the payment from `GET /mpesa/transactions/unmatched` and records the
  sale with its `mpesaTransactionId`. The amount must match. **One payment can pay for only one sale**, even when two cashiers pick it
  at the same moment (§13).
- **Repeated Safaricom callbacks** are accepted quietly and settle the sale once.
- **Daily cash-up:** `GET /reports/sales?from=&to=` (`byPaymentMethod`, `byCashier`) gives the day's takings per method and per
  cashier. A full cash-up report (cash sales + customer payments in cash − cash expenses, M-Pesa received vs matched) does not exist
  yet (gap G10).

---

## 9. Users, roles and permissions

Permissions are enforced by the backend on every endpoint. Hiding a button is not the security. Only **Admin** is created with a
business. Create the others with `POST /roles`, then `PUT /roles/{id}/permissions` (permission ids from `GET /permissions`). Add users
with `POST /auth/Register` (`roleName`), and change roles with `PUT /users/{id}/roles`. Suggested roles, for the owner to confirm:

| Role | Permissions |
|---|---|
| **Owner** | `Admin` (everything) |
| **Manager** | Sales.*, Customers.*, Suppliers.*, PurchaseOrders.*, Stock.View, **Stock.Adjust**, Expenses.*, ExpenseCategories.View, Dashboard.View, Costing.View, all *Reports.View, Mpesa.View, Sales.ApplyDiscount, Sales.OverrideCreditLimit, Sales.Void, DeliveryOrders.*, including **DeliveryOrders.AcceptCredit** |
| **Cashier** | Sales.Create, Sales.View, Customers.View, Customers.Register, Customers.RecordPayment, Products.View, Stock.View, Mpesa.View, Dashboard.View, DeliveryOrders.View, .Create, .Dispatch, .Deliver, .Cancel (**not** AcceptCredit) |
| **Storekeeper** | Suppliers.View, PurchaseOrders.View, PurchaseOrders.Receive, Stock.View, Products.View |
| **Accountant** | Expenses.*, ExpenseCategories.*, Suppliers.ViewLedger, Suppliers.RecordPayment, Customers.ViewLedger, Costing.View, SalesReports.View, CustomerReports.View, InventoryReports.View, PurchaseReports.View, Dashboard.View |

Business data is isolated per tenant: every query is filtered to the signed-in business, and stock to the branches the user may see
(tested: another business cannot see or sell the station's water). The station starts with one branch, its head office (00).

---

## 10. Dashboard and reports

| Requested | Source today |
|---|---|
| Today's sales | `GET /dashboard/sales?from=today&to=today` |
| Today's litres sold | `GET /reports/sales` → `topProducts[].quantitySoldInBaseUnits` for the water product (litres), or `GET /reports/inventory/movements` → `sold` |
| Available water (litres) | `GET /stock/{waterProductId}` → `quantityOnHand` |
| Today's supply cost | `GET /reports/inventory/movements` → `receivedValue` (needs `Costing.View`), or `GET /reports/purchases/summary` → `receivedValue` |
| Today's expenses | `GET /expenses/summary?from=&to=` |
| Outstanding customer balances | `GET /dashboard/receivables` |
| Outstanding supplier balances | `GET /suppliers/balances` (sum it) |
| Low stock alerts | `GET /dashboard/stock`, `GET /stock?belowReorderOnly=true`, notifications |
| Daily sales summary, by payment method | `GET /reports/sales` → `byDay`, `byPaymentMethod` |
| **Sales by refill size** | `GET /reports/sales` → **`topProductUnits`** (new): per size, quantity sold, litres, revenue |
| **Sales by cashier** | `GET /reports/sales` → **`byCashier`** (new) |
| Supply and purchase history | `GET /reports/purchases/summary`, `GET /purchase-orders`, `GET /reports/purchases/price-trends` |
| Supplier balances | `GET /suppliers/balances` |
| Inventory reconciliation | `GET /reports/inventory/movements` |
| Losses and adjustments | `GET /reports/inventory/adjustments` |
| Expense breakdown | `GET /expenses/summary` |
| Customer balances | `GET /reports/customer-aging` |
| Gross profit and operating expenses | `GET /dashboard/profit`: revenue (net of VAT), cost of goods sold (cost per litre when sold), gross profit, operating expenses, net operating profit |
| Cash and M-Pesa reconciliation | Partly (`byPaymentMethod`, unmatched till payments). Full cash-up: gap G10 |
| Deliveries per rider, cash to hand in | `GET /delivery-orders/summary` |
| Promises to pay that are overdue | Alerts, and `GET /reports/customer-aging?includeInvoices=true` → `isPromiseOverdue` |

Profit is **never** "sales minus what was paid to vendors today". It uses the cost recorded when each litre left stock, plus expenses.
`isComplete: false` on the profit figures means some sales had no recorded cost (`uncostedRevenue` says how much), so the
profit figures are not complete. Recording opening stock and every delivery with its cost prevents this.

---

## 11. Optional modules

- **Containers: settled, no module needed.** Containers are sold as products (§3.4) and never lent on deposit.
- **Deliveries: built** (§6.1). Delivery is free today. A delivery fee, scheduled time slots and proof of delivery are not built;
  add them when the owner needs them (BACKEND_API_GAPS.md G4).
- **Water quality and maintenance (G6), ask the owner first:** a log of tests, filter changes, tank cleaning, service dates and licence expiry, kept apart
  from stock. The system would record results; it sets no quality thresholds and claims no regulatory compliance. Testing stays with
  accredited labs and the applicable Kenyan requirements. *Ask:* what do you test, how often, and what must you show an inspector?

---

## 12. Frontend changes required

The backend changes are additive. Nothing an existing screen sends or reads has changed shape. To support the station:

1. **Business settings:** add an *"Allow selling below zero stock"* switch. It is read from `GET /businesses/current` →
   `allowNegativeStock`, and sent on `PUT /businesses/{id}` as `allowNegativeStock`. Leaving it out or sending `null` keeps the current
   value, so older screens are safe. For registration (platform screen), send it on `POST /businesses` (default `true`).
2. **POS checkout:**
   - Handle **409 `INSUFFICIENT_STOCK`** on `POST /sales`. Show the message and keep the cart; it names what is short.
   - When `allowNegativeStock` is false, use `POST /sales/preview` → `lines[].stockSufficient` to warn, or disable *Pay*, before charging.
   - Quick buttons per size (1L, 10L, 20L) make a counter sale two taps. Each button is the water product with that unit; no new API.
3. **Till payment picker:** a sale against an M-Pesa till payment can now be refused with **400 `INVALID_ASSIGNMENT`**: *"This M-Pesa
   transaction has just been matched to another sale."* Refresh `GET /mpesa/transactions/unmatched` and let the cashier pick again.
4. **Receive purchase order:** can now be refused with **400 `INVALID_ASSIGNMENT`**: *"…has just been received against (or cancelled)
   by someone else."* Reload the order and show what is still outstanding. Disable the button while a receipt is in flight.
5. **Void:** a second void of the same sale returns 400 (already voided). Reload the sale.
6. **Sales report:** two new lists on `GET /reports/sales`:
   - `topProductUnits[]` (`productId, productName, sku, unitOfMeasureId, unitSymbol, quantitySold, quantitySoldInBaseUnits, revenue,
     percentOfRevenue`). Revenue is net of VAT and returns, like `topProducts`. Title it *Sales by size*.
   - `byCashier[]` (`userId, cashierName, saleCount, total, percentOfRevenue`). Totals include VAT, like `byPaymentMethod`.
7. **Customer search at checkout:** phone search now works in the formats cashiers type (`0722…`, `+254 722…`). Remove any
   client-side workaround. Add a *Register new customer* shortcut in the customer picker: `POST /customers`, then use the returned
   id on the sale.
8. **Dashboard (refill station layout):** the cards in §10 can be built from existing endpoints; no new endpoint is required. Water
   in litres shows the product's `baseUnitSymbol`.
9. **Counter: "Customer has a container?"** A toggle on the cart. Off adds the container named on the refill's unit conversion
   (`unitConversions[].containerProductId`, §3.4) as a line next to the refill; on leaves the refill only. Selling is unchanged: it only
   decides the lines sent.
10. **Delivery orders (new screens):**
    - *New delivery order:* customer picker with *Register new customer*, address, contact phone, the same container toggle, lines.
      Show `estimatedTotal`, and handle 409 `INSUFFICIENT_STOCK` (*"…available, …needed"*).
    - *Delivery board:* `GET /delivery-orders?status=`, with columns Pending / Out for delivery / Delivered today. Actions: **Dispatch**
      (rider name), **Cancel** (reason).
    - *Record delivery*, used by the cashier when the rider is back:
      - Choose Cash (amount given, show change), Till (pick from `GET /mpesa/transactions/unmatched`), or *Pay later* (date picker).
      - Show *Pay later* only to users holding `DeliveryOrders.AcceptCredit`. The API refuses others with 403 anyway.
      - Allow editing the lines (remove the container) before saving.
      - Handle 400 `INVALID_ASSIGNMENT` *"…has just been recorded…"* by reloading the order.
    - *Rider cash-up:* `GET /delivery-orders/summary?from=&to=`, a table per rider of delivered, cash to hand in, till and credit.
11. **Stock screens:** show `reservedQuantity` ("on delivery orders") and use `sellableQuantity` as "available". `quantityOnHand` still
    includes water set aside for deliveries until they are delivered.
12. **Credit sale at the counter:** optional *Promised by* date (`paymentDueDate`) on credit sales.
13. **Customer account:** show `paymentDueDate` and an *overdue* badge (`isPromiseOverdue`) on open invoices in the ageing report.
    The new notification type `Customer.PaymentPromiseOverdue` links to `/sales/{id}`.

14. **Sector (all businesses):**
    - Platform registration screen: a *Sector* picker (`sector`: 0 General, 1 Pharmacy, 2 Agrovet, 3 WaterRefill, 4 Minimart,
      5 Supermarket, 6 Hardware). Kind can be left empty where the sector suggests one; an Agrovet must still choose.
    - Platform businesses list: a sector filter (`GET /platform/businesses?sector=`).
    - Platform dashboard: a by-sector table (`bySector`).
    - Business detail: a *Change sector* action (`PUT /platform/businesses/{id}/sector`).
    - In the business app, read `sector` from sign-in (`businesses[].sector`) or `GET /businesses/current`. Use it to choose menus:
      prescriptions for Pharmacy, the delivery board and refill quick buttons for WaterRefill. Hiding a menu is a convenience only;
      the API still checks permissions.

Not needed now: bank transfer or split payment at the counter (deferred), container deposits (not used), maintenance (§11).

---

## 13. Backend changes made for this onboarding

| Change | Why | Where |
|---|---|---|
| `BusinessProfile.AllowNegativeStock` (default **true**) | A business can refuse to sell more than it holds. Existing businesses keep their behaviour. | Domain `Business.cs`, `SalesService`, `SalesRepository`, business DTOs, migration `BusinessAllowNegativeStock` |
| Purchase order receipt: row lock and re-check | The same delivery submitted twice at once was put into stock twice, with two vendor bills | `PurchasingRepository.ReceivePurchaseOrderAsync` |
| M-Pesa STK confirm/fail: row lock, act only while Pending | A callback plus a reconcile at the same moment took stock twice | `SalesRepository.CompletePendingSaleAsync`, `FailPendingSaleAsync` |
| Void: row lock and re-check | A double void put stock back twice | `SalesRepository.VoidSaleAsync` |
| Till payment claimed inside the sale's transaction | One till payment could be matched to two sales | `SalesRepository.RecordCompletedSaleAsync` (removed `IMpesaRepository.MatchToSaleAsync`) |
| Sales report: `topProductUnits`, `byCashier` | Sales by refill size, sales by cashier | `ReportsService.BuildSalesReport`, report DTOs |
| Customer phone search in local formats | Searching `0722…` did not find `254722…` | `BusinessRepository.GetCustomersAsync` |
| **Delivery orders** (Pending → OutForDelivery → Delivered / Cancelled), with reservations, a rider, payment on delivery, rider summary | The station delivers | `Domain/Entities/DeliveryOrder.cs`, `Application/Deliveries/DeliveryOrders.cs`, `DeliveryOrderRepository`, `DeliveryOrdersController`, migration `DeliveryOrders` |
| Delivery becomes a sale in the sale's own transaction (claim only while out for delivery) | A delivery recorded twice must not be two sales | `SalesRepository.RecordCompletedSaleAsync` |
| Reserved stock is not sellable | A counter sale must not sell water promised to a delivery | `SalesService.BuildSalePlanAsync`, `InventoryRepository.GetReservedQuantitiesAsync`, `StockLevelDto.ReservedQuantity` |
| `Sale.PaymentDueDate`, overdue-promise alert, promise on ageing invoices | "Promise to pay on a future date" | `SalesService`, `AlertSweepService`, `ReportsService.GetCustomerAgingAsync` |
| Permissions `DeliveryOrders.View/Create/Dispatch/Deliver/Cancel/AcceptCredit` | Delivering on credit is manager-only | `PermissionCatalog` (Admin roles get them on startup) |
| Credit delivery needs no promised date | Owner: pay later is an ordinary credit sale on the customer's account | `DeliveryOrderService.DeliverAsync` |
| `ProductUnitConversion.ContainerProductId`, `PUT …/unit-conversions/{id}/container` | Which container goes with which refill size, kept on the server rather than per till | Domain, `CatalogService`, `CatalogRepository`, `ProductsController`, migration `UnitConversionContainer` |

**Migrations:**
- `BusinessAllowNegativeStock`: `ALTER TABLE "Businesses" ADD "AllowNegativeStock" boolean NOT NULL DEFAULT TRUE`. Every existing
  business keeps allowing negative stock.
- `DeliveryOrders`: new tables `DeliveryOrders` and `DeliveryOrderLines`, plus a nullable `Sales.PaymentDueDate`.
- `UnitConversionContainer`: a nullable `ProductUnitConversions.ContainerProductId` (foreign key to `Products`, restrict on delete).

All are additive. The API applies migrations on startup (`DbSeeder.SeedAsync`). It can also be applied with
`dotnet ef database update --project src/UltimatePos.Infrastructure --startup-project src/UltimatePos.Api`.

**Tests:** `tests/UltimatePos.Api.Tests/WaterRefillStationPostgresTests.cs` (integration, real PostgreSQL), plus two report tests in
`ReportsTests.cs`. They cover:
- 1L, 10L and 20L deductions, and the 53 L mixed sale
- oversell refusal, including five concurrent sales
- the default of allowing negative stock, and the setting surviving an older client
- a delivery received once (sequentially and concurrently), and the vendor owed once
- the 500 + 300 − 420 − 5 = 375 reconciliation
- adjustments requiring a reason, and expenses leaving stock alone
- walk-in sales, registering a customer at checkout, phone search, and credit limit and override permission
- duplicate and concurrent M-Pesa callbacks
- one till payment for one sale
- concurrent voids
- the new report lists
- tenant isolation
- deliveries:
  - own container (water only) vs no container (water + container), and removing the container at the door
  - cash with change, a till payment, and STK refused
  - pay-later refused for a cashier and accepted for a manager with a date
  - the overdue-promise alert raised and then cleared on payment
  - reservations blocking the counter and other orders, released on cancel
  - a delivery still recordable when other orders hold the rest, but never below zero
  - a delivery recorded four times at once giving one sale
  - the rider summary

Each concurrency test was checked to **fail** with its guard removed. Run them with:

```bash
ULTIMATEPOS_TEST_POSTGRES="Host=localhost;Port=5432;Username=ultimatepos;Password=localdevonly" dotnet test ultimatePos.sln
```

(The connection string is the local docker-compose database's. Without the variable, the database tests are skipped.)
