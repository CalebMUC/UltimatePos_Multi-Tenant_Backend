# Frontend Task: Backdated Sales (entering the paper book after a power failure)

> **For:** the coding agent working on the Ultimate POS frontend (`Ultimate_Multi_Tenant_Frontend`).
> **Depends on:** the API release with migration `BackdatedSales`.
> Before you start, read `CreateSaleRequestDto`, `SalePreviewDto` and `SaleDto` in `src/UltimatePos.Application/Sales/Dtos/SalesDtos.cs`
> and `ResolveSaleTime` in `src/UltimatePos.Application/Sales/SalesService.cs`. If the code and this prompt disagree, the code wins.

## Why

When the power goes off, the till cannot record sales, so the shop writes them in a paper book. When the power comes back, a manager
enters those sales **with the time they were really made**. That way they land on the right day in reports, on the stock card and in that
day's cash-up. This does not happen often, so it is a manager's tool: a separate mode of the POS screen, not a field on every sale.

Rules the API enforces (show them in the UI so nobody is surprised):

- **Up to 3 calendar days back** (East Africa Time), never in the future.
- Needs the new permission **`Sales.Backdate`**. Admin roles get it automatically. Give it to managers, not cashiers.
- A **reason** is required (≤ 200 characters), e.g. "Power failure, entered from the paper book".
- **Stock is checked as it stood at the sale time.** The check uses what was on hand then, and no later balance may go below zero. It
  does not use what is on the shelf now:
  - Stock delivered *after* the sale time cannot be used.
  - Stock that has been sold or adjusted out since then is not there for it either. This stops two paper slips together selling more
    than the shelf held.
- **Payment:** cash, M-Pesa code (`MpesaReference`), till payment (`MpesaTill`) or credit. **Not STK push**, because the customer paid at
  the time.
- **Cash and the drawer:** a backdated **cash** sale goes into the drawer (cash session) that was open at its time. If that drawer has
  already been counted and closed, the sale is refused. Managers should enter backdated cash sales **before** closing the drawer.
- **Liquor:** the selling hours are checked at the sale time, not now.
- **Prices** are today's prices, and the sale number is the next number in sequence.

## API

### 1. Record or preview: `POST /api/v1/sales` and `POST /api/v1/sales/preview`

Two new optional fields on the usual body:

```jsonc
{
  // ...customerId, paymentMethod, lines, bundles, amountTendered, mpesaReference... as today...
  "soldAt": "2026-11-05T14:30:00+03:00",   // when it really happened, WITH the offset; leave out for a normal sale
  "backdateReason": "Power failure, entered from the paper book"
}
```

- Send `soldAt` **only** in backdate mode. A `soldAt` within a minute of now is treated as an ordinary sale.
- Call **preview** with the same `soldAt`. `lines[].availableInBaseUnits` and `stockSufficient` then describe the stock **at that time**,
  and `soldAt` is echoed on the preview (UTC). Show "Stock at 14:30 on Wed 5 Nov" instead of "In stock".
- Errors (the message is written for the user; show it as it comes):

| HTTP | `code` | When |
|------|--------|------|
| 400 | `VALIDATION_FAILED`, field `soldAt` | in the future, more than 3 days back, or on a delivery |
| 400 | `VALIDATION_FAILED`, field `backdateReason` | missing or longer than 200 characters |
| 400 | `VALIDATION_FAILED`, field `paymentMethod` | STK push |
| 403 | `FORBIDDEN` | the user lacks `Sales.Backdate` |
| 409 | `INSUFFICIENT_STOCK` | not enough stock at the sale time (or since) |
| 400 | `INVALID_ASSIGNMENT` | cash sale in a drawer that has already been counted and closed (the message names the session) |
| 422 | `OUTSIDE_SELLING_HOURS` | alcohol sold outside the licence hours at the sale time |

### 2. The sale: `SaleDto`

- `createdAt` is **when it happened** (the backdated time).
- `recordedAt` (new, nullable) is when it was entered. It is null for normal sales.
- `backdateReason` (new, nullable).

### 3. Sales list: `GET /api/v1/sales?backdated=true`

`backdated=true` returns only late entries, `false` returns none of them, and leaving it out returns all of them. The date filters
(`from`/`to`) use `createdAt`, so a backdated sale appears on the day it happened.

### 4. Notifications

A backdated sale raises the event `Sale.Backdated` (warning). It is on the audit log, and other holders of `Sales.Backdate` get an in-app
notification linking to `/sales/{id}`.

## Screens

### 1. POS (`/pos`): "Enter past sales" mode

- Show a button **"Enter past sales"** only to users with `Sales.Backdate`. It opens a small dialog with:
  - **Date and time** picker (default: today at the current hour). Limit it to the last 3 days and nothing in the future. Send it with
    `+03:00`.
  - **Reason** (required, prefilled "Power failure, entered from the paper book" and editable).
- While the mode is on, show a **clear amber banner** across the POS: "Entering past sales: Wed 5 Nov 14:30 — Power failure…
  [Change] [Done]". Every sale rung up sends that `soldAt` and `backdateReason`. Let the manager change the time between slips without
  leaving the mode, because each slip has its own time.
- Hide **STK push** among the payment methods.
- Stock badges come from the preview, so they show the stock at that time.
- After each sale, keep the mode on and clear the cart, ready for the next slip. **Done** turns the mode off.
- Print the receipt as usual, but under the date add "Entered {recordedAt} — backdated" so a reprint is never mistaken for a new sale.
  Printing is optional in this mode: default the "Print receipt" toggle to **off**.

### 2. Sales list and sale detail

- Show a small "Backdated" tag on rows with `recordedAt`. On hover or in the detail view, show "Entered {recordedAt} by {createdByName}:
  {backdateReason}".
- Add a "Backdated only" filter (`backdated=true`).

### 3. Roles screen

`Sales.Backdate` appears under **Sales** with the description "Enter sales made up to 3 days ago (e.g. after a power failure)".

## Tests to add

- The "Enter past sales" button is hidden without `Sales.Backdate`.
- The date picker rejects dates more than 3 days back and future times. The reason is required.
- In backdate mode the request carries `soldAt` with `+03:00` and `backdateReason`, and STK push is hidden.
- A 409 `INSUFFICIENT_STOCK` and a 400 for a closed drawer show the API's message, and the cart is kept.
- The sales list shows the Backdated tag and the filter sends `backdated=true`.
