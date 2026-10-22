# Frontend Task: Customer Buying Insights and Standing Customer Discounts

> **For:** the coding agent working on the Ultimate POS frontend (`Ultimate_Multi_Tenant_Frontend`).
> **Depends on:** the API release with migration `CustomerDiscountAndInsights`.
> Before you start, read `src/UltimatePos.Application/Customers/CustomerInsights.cs`, `src/UltimatePos.Api/Controllers/CustomerController.cs`,
> `src/UltimatePos.Api/Controllers/DashboardController.cs` and the sale DTOs in `src/UltimatePos.Application/Sales/Dtos/SalesDtos.cs`.
> If the code and this prompt disagree, the code wins.

## Why

The water refill station's owner wants to see **how each customer has been buying** (how often, how much, how recently, whether they
are drifting away), on the dashboard and in the Customers module. On that basis the owner can **give a customer a discount**. Once
given, the discount applies by itself on every later sale to that customer, at the counter and on deliveries. The cashier does not
type it and does not need the discount permission.

The API suggests, the owner decides. The API never picks a percentage.

## Rules that do not change

- Enums are sent and received as **numbers**. New ones:
  - `CustomerSegment`: `0` Occasional, `1` New, `2` Regular, `3` Top, `4` Slipping
  - `CustomerSuggestedAction`: `0` None, `1` ConsiderDiscount, `2` WinBack, `3` ReviewDiscount
  - `CustomerInsightsSort`: `0` Spend, `1` Purchases, `2` Recent
- Every response is `{ success, data, error: { code, message, details }, meta }`.
- **Never compute discounts, totals, segments or ranks in the browser.** Show what the API returns. The sale preview already includes
  the customer's discount.
- Hiding a button is not security. The API checks permissions on every call.

## Permissions (new)

| Need | Permission |
|---|---|
| See insights (list, customer tab, dashboard panel) | **`Customers.ViewInsights`** |
| Give, change or remove a customer's discount | **`Customers.ManageDiscount`** |

Add both to `src/api/permissions.ts`. Admin gets them on API start-up; assign them to Owner/Manager roles, not Cashier. A cashier still
**sees** a customer's discount at the till (it comes on `CustomerDto` and the sale preview, which need only `Customers.View` and
`Sales.Create`).

## What the segments mean (show this as help text)

Measured against the business's own customers in the same period:

| Segment | Meaning | Label / colour |
|---|---|---|
| **Top** | In the top fifth by spend, and bought more than once | "Top customer", gold |
| **Regular** | Comes back at least as often as the typical repeat customer (`typicalDaysBetweenPurchases`) | "Regular", green |
| **Slipping** | Has stayed away more than twice as long as usual for them (and at least 7 days) | "Slipping away", red |
| **New** | First ever purchase in the last 30 days | "New", blue |
| **Occasional** | Everyone else | "Occasional", grey |

| Suggestion | Show as |
|---|---|
| `ConsiderDiscount` | "Consider a loyalty discount" + **Give discount** button |
| `WinBack` | "Hasn't been back: call them / make an offer" + phone link |
| `ReviewDiscount` | "Has a discount but isn't buying regularly: review it" + **Change discount** |
| `None` | nothing |

## API

### Insights list

`GET /api/v1/customers/insights?from=&to=&segment=&suggestedAction=&search=&sort=&page=&pageSize=` → paged `CustomerBuyingSummaryDto[]`.
`from`/`to` are local days (`yyyy-MM-dd`). Leave them out for **the last 90 days**. Only customers who bought in the period are listed.

```jsonc
// CustomerBuyingSummaryDto
{
  "customerId": "…", "customerName": "Mama Wanjiru", "phoneNumber": "254722…",
  "purchaseCount": 24, "totalSpent": 9600.00, "averageSpend": 400.00,
  "firstPurchaseAt": "2026-03-02T…Z",       // ever, not just in the period
  "lastPurchaseAt": "2026-10-21T…Z", "daysSinceLastPurchase": 1,
  "averageDaysBetweenPurchases": 3.5,       // null with one purchase
  "discountPercent": null,                  // standing discount, null = none
  "discountGiven": 0.00,                    // standing-discount money given in the period
  "segment": 3, "suggestedAction": 1, "spendRank": 1
}
```

`totalSpent` is completed sales, VAT included, less returns. Walk-in sales (no customer) are not in the list. The dashboard shows their
total separately.

### One customer

`GET /api/v1/customers/{id}/insights?from=&to=` → `CustomerInsightsDto`. Default period: **the last 365 days**.

```jsonc
{
  "customerId": "…", "customerName": "…", "phoneNumber": "…", "from": "2025-10-23", "to": "2026-10-22",
  "summary": { /* CustomerBuyingSummaryDto, or null if nothing bought in the period */ },
  "byMonth": [ { "month": "2025-10", "purchaseCount": 3, "total": 1200.00 }, … ],   // every month in the period, zeros included
  "topProducts": [ { "productId": "…", "productName": "Water refill", "baseUnitSymbol": "l", "quantityInBaseUnits": 960, "total": 9600.00 } ],
  "byPaymentMethod": [ { "paymentMethod": 0, "purchaseCount": 20, "total": 8000.00 } ],
  "deliveredByRider": 4, "boughtAtCounter": 20, "returnedAmount": 0.00,
  "balance": 0.00, "creditLimit": null,
  "discountPercent": null, "discountNote": null, "discountSetAt": null,
  "maxDiscountPercent": 15,                  // the business's cap; null = none
  "typicalDaysBetweenPurchases": 5.0,        // what "Regular" is measured against
  "reasons": [ "24 purchase(s), 9600.00 in total: number 1 of 37 customers by spend.", "Buys about every 3.5 day(s); …", "Last bought 1 day(s) ago.", "A loyal, valuable customer with no standing discount: a discount could keep them." ]
}
```

`quantityInBaseUnits` is net of returns, in the product's base unit. For water that is litres: show "960 L".

### Set the discount

`PUT /api/v1/customers/{id}/discount` → `CustomerDto`

```jsonc
{ "discountPercent": 5, "note": "Buys 20L every 3 days since March" }   // give or change
{ "discountPercent": null, "note": null }                               // remove (0 also removes)
```

- `note` is **required** when giving a discount (400 `note`).
- `discountPercent` must be below 100, with at most 2 decimals, and at most `maxDiscountPercent` (400 `discountPercent` with the cap in
  the message).
- Sales already made keep their prices. Only later sales get the new rate.
- The change is audited and notifies other holders of `Customers.ManageDiscount` (notification type `Customer.DiscountChanged`, link
  `/customers/{id}`).

`CustomerDto` (all customer endpoints) now also carries `discountPercent`, `discountNote` and `discountSetAt`.

### Dashboard panel

- `GET /api/v1/dashboard/summary` now has a `customers` section (name `"Customers"` in `sections` / `hiddenSections`). It is null
  without `Customers.ViewInsights`.
- Or call `GET /api/v1/dashboard/customers?to=` on its own. The panel **always covers the 90 days up to `to`** (default today),
  whatever dates the rest of the dashboard uses, because buying habits don't show in a single day. Label it "Last 90 days".

```jsonc
// DashboardCustomersDto
{
  "from": "2026-07-25", "to": "2026-10-22",
  "activeCustomers": 37, "newCustomers": 4, "regularCustomers": 12, "slippingCustomers": 5,
  "discountCandidates": 6, "customersWithDiscount": 3,
  "registeredRevenue": 84500.00, "walkInRevenue": 120300.00,
  "topCustomers": [ /* up to 5 CustomerBuyingSummaryDto, by spend */ ],
  "candidates":   [ /* up to 5 with suggestedAction = ConsiderDiscount */ ],
  "slipping":     [ /* up to 5 Slipping, by spend */ ]
}
```

### Sales and the till

- `SalePreviewDto` gained `customerDiscountPercent` and `customerDiscountTotal`. `SaleDto` gained `customerDiscountPercent` and
  `customerDiscountAmount`. These are **already inside** `discountTotal` and the line `discountAmount`s. Do not subtract them again.
- A cashier **without** `Sales.ApplyDiscount` can sell to a discounted customer. Only a discount they type on top still needs the
  permission (403 as before).
- Delivery orders are priced the same way, so `estimatedTotal` already includes the discount.

## Screens

### 1. Dashboard: "Customers" panel (needs `Customers.ViewInsights`)

Add it to `src/app/(app)/dashboard/page.tsx` when `summary.customers` is present. Layout:
- Stat tiles: Active customers · Regulars · New · **Slipping away** (red when > 0) · **Discount candidates** (amber when > 0) · With a discount.
- A bar or split showing **registered vs walk-in revenue**. Caption: "Walk-in buyers can't be rewarded or followed up. Register regulars at the till."
- Three short lists side by side (stack on phones): **Top customers** (name, spend, purchases, segment chip), **Consider a discount**
  (name, purchases, every N days, *Give discount* button), **Slipping away** (name, last bought N days ago, phone link).
- Each row links to the customer's **Buying** tab. Each list has a "See all" link to the insights list, filtered.

### 2. Customers → "Buying insights" list (new page or tab, `Customers.ViewInsights`)

- Route suggestion: `/customers/insights`. Add a menu item under Customers.
- Toolbar: date range (default last 90 days), segment chips, suggestion filter, search (name or phone), sort (Spend / Purchases / Recent).
- Columns: rank, customer, phone, purchases, total spent, average, every N days, last bought (N days ago), discount %, segment chip,
  suggestion with its action button.
- Paged (`meta`). Row click → customer detail, Buying tab.

### 3. Customer detail → "Buying" tab (`src/app/(app)/customers/[id]/page.tsx`)

- Header: segment chip, suggestion banner (from `summary.suggestedAction`), and the `reasons` as a short bullet list.
- Stat row: purchases, total spent, average spend, buys every N days (vs typical), last bought, first ever purchase, discount given.
- **Monthly chart** from `byMonth` (bars = total, line or label = purchase count). Show zero months.
- **What they buy**: `topProducts` with quantity in base units ("960 L") and total.
- Payment mix (`byPaymentMethod`), counter vs rider (`boughtAtCounter` / `deliveredByRider`), returns, balance and credit limit.
- Period picker (default last 12 months).
- If `summary` is null: "No purchases in this period."

### 4. Discount dialog (`Customers.ManageDiscount`)

Open it from the Buying tab, the list and the dashboard.
- Shows the current discount (and note, date) and the cap (`maxDiscountPercent`).
- Input: percentage (0–cap, step 0.5), **note required**.
- Live preview text: "A 20L refill at 200 would be 190." Get the number from `POST /sales/preview` with the customer and one line of their
  top product, never from browser maths. Or leave the example out.
- Buttons: **Save**, and **Remove discount** when one exists (sends `null`).
- After saving, invalidate `customers`, `customer-insights` and the dashboard queries.

### 5. Till / POS

- When the selected customer has `discountPercent`, show a chip next to their name: "**5% loyalty discount**".
- In the totals, show the customer discount as its own line ("Customer discount (5%) −40.00") from `preview.customerDiscountTotal`. Show
  any typed discount separately as `discountTotal − customerDiscountTotal`.
- Receipt and sale detail: show the same line from `customerDiscountAmount`.

### 6. Customer list and form

- Customer list: a small "% off" badge on customers with a discount.
- The customer edit form does **not** set the discount. That only happens in the discount dialog, with its permission and note.

## Tests to add

- Dashboard: the panel renders from `summary.customers`; hidden (not empty) when `customers` is null.
- Insights list: filters and sort go to the query string; no client-side ranking.
- Discount dialog: note required before Save; the cap is shown and enforced from the API error; Remove sends `discountPercent: null`.
- POS: the customer-discount line uses `customerDiscountTotal`; a cashier without `Sales.ApplyDiscount` can still charge a discounted customer.
