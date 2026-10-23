# Frontend Task: Monthly-Spend Discount (20% after 4,000 in a month)

> **For:** the coding agent working on the Ultimate POS frontend (`Ultimate_Multi_Tenant_Frontend`).
> **Depends on:** the API release with migration `MonthlySpendDiscount`, and builds on `docs/Frontend-Customer-Insights-Discount-Prompt.md`.
> Before you start, read `src/UltimatePos.Application/Sales/MonthlySpendReward.cs`, the sale DTOs in
> `src/UltimatePos.Application/Sales/Dtos/SalesDtos.cs`, and `UpdateMonthlyDiscountAsync` in
> `src/UltimatePos.Application/Business/BusinessService.cs`. If the code and this prompt disagree, the code wins.

## The rule (from the water refill station's owner)

- When a **registered customer's** purchases in a **calendar month** reach **4,000**, they get **20% off** their purchases for the rest
  of that month.
- The purchase that takes them to 4,000 or past it pays **full price**. The discount starts with the **next** purchase.
- On the 1st of the next month, prices go back to normal and the count starts again from zero.
- Walk-in sales (no customer) never count and never get it. Register regulars at the till.

The API does all of this. The browser never adds up a customer's month and never computes the discount.

Facts to show where useful:
- The month is the calendar month in East Africa Time. Purchases at **every branch** count.
- What counts is completed sales, after any discount, less returns. Pending M-Pesa prompts and voided sales do not count.
- If the customer also has a standing discount, they get **the larger of the two**, never both.
- The business's "max discount" cap limits what **cashiers** type. It does not limit this reward.

## Enums (numbers)

- `MonthlyDiscountScope`: `0` RestOfMonth (every later purchase that month; the station's rule), `1` NextPurchaseOnly (only the first
  purchase after reaching the threshold)
- `CustomerDiscountSource`: `0` Standing (the customer's own discount), `1` MonthlySpend (this reward)

## API

### Settings

`GET /api/v1/businesses/current` → `BusinessDto` now has `monthlyDiscountThreshold`, `monthlyDiscountPercent` (both null = off) and
`monthlyDiscountScope`.

`PUT /api/v1/businesses/current/monthly-discount` (`Businesses.Update`) → `BusinessDto`

```jsonc
{ "threshold": 4000, "percent": 20, "scope": 0 }   // on: the station's rule
{ "threshold": null, "percent": null, "scope": 0 } // off
```

400 if only one of `threshold` and `percent` is given, if `threshold <= 0`, or if `percent` is not between 0 and 100 (exclusive). Up to
2 decimals.

### Till

`POST /sales/preview` now returns:

```jsonc
{
  "totalAmount": 160.00,
  "customerDiscountPercent": 20, "customerDiscountTotal": 40.00,
  "customerDiscountSource": 1,                 // 1 = MonthlySpend, 0 = Standing, null = none
  "customerMonth": {                           // null for walk-ins, or when the business has no reward
    "month": "2026-10", "spent": 4200.00,      // this month so far, before this sale
    "threshold": 4000.00, "percent": 20, "scope": 0,
    "remainingToThreshold": 0.00,
    "qualifies": true                          // this sale gets the reward
  }
}
```

`SaleDto` (receipt, sale detail) has `customerDiscountSource` next to `customerDiscountPercent` and `customerDiscountAmount`.

### Customer

`GET /customers/{id}/insights` → `thisMonth`: the same `customerMonth` object, or null.

## Screens

### 1. Settings → Discounts (`Businesses.Update`)

A card **"Monthly spend reward"**:
- Toggle On/Off.
- When on: *Spend threshold (KES)* (number), *Discount (%)* (number), and *Applies to*: "Every purchase for the rest of the month"
  (`0`, default) or "Only the next purchase" (`1`).
- One sentence preview: *"Customers who spend KES 4,000 in a month get 20% off every purchase for the rest of that month."*
- Save → `PUT /businesses/current/monthly-discount`. Off sends both values as null. Then refresh the current business.
- Show the API's 400 messages next to the fields (`details.threshold`, `details.percent`).

### 2. POS / till (when a customer is selected and `preview.customerMonth` is not null)

- Under the customer's name, a progress bar: **"Spent KES 3,800 of 4,000 this month: KES 200 to unlock 20% off"**, from `spent`,
  `threshold` and `remainingToThreshold`.
- When `qualifies` is true: a green chip **"20% monthly reward applied"**. When the rule is "next purchase only" and it is not
  qualifying because it was already used, say "Monthly reward already used this month".
- Totals: the customer discount line is labelled by `customerDiscountSource`:
  - `1` → "Monthly reward (20%) −40.00"
  - `0` → "Customer discount (10%) −20.00"
  - Still show only `customerDiscountTotal`; never add a second line.
- Do not cache the preview across sales. After each completed sale the customer's month changes, so the next preview can differ.
- Delivery orders: `estimatedTotal` comes from the same pricing. The sale is priced again when the rider is back, so the reward can
  apply then even if it did not at order time. The *Complete sale* dialog must show the amount from the API, not the order's estimate.

### 3. Receipt and sale detail

When `customerDiscountAmount > 0`, print "Monthly reward (20%)" or "Customer discount (10%)" by `customerDiscountSource`, with the
amount.

### 4. Customer → Buying tab, dashboard

- Buying tab: a "This month" card from `thisMonth`: spent, threshold, remaining, and a *Reward active* badge when `qualifies`.
- Insights list (optional): a column "This month" is **not** in the list API. Do not compute it. Leave it to the Buying tab.

## Tests to add

- Settings: off sends nulls; on validates both fields; the preview sentence follows the values.
- POS: the progress bar renders from `customerMonth`; the reward chip shows only when `qualifies`; the discount line label follows
  `customerDiscountSource`; no client-side arithmetic on totals.
- Receipt: the label follows `customerDiscountSource`.
