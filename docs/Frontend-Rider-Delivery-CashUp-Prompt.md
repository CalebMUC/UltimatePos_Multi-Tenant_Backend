# Frontend Task: Delivery by Rider at the Till, Returns, Trips and Fuel, and the Day's Float

> **For:** the coding agent working on the Ultimate POS frontend (`Ultimate_Multi_Tenant_Frontend`).
> **Depends on:** the API release with migration `DeliveryModeTripsReturnsCashSessions`.
> Before you start, read `src/UltimatePos.Application/Deliveries/DeliveryOrders.cs`, `src/UltimatePos.Application/CashSessions/CashSessions.cs`,
> `src/UltimatePos.Api/Controllers/DeliveryOrdersController.cs`, `src/UltimatePos.Api/Controllers/CashSessionsController.cs` and §6.1, §6.2
> and §8.1 of `docs/WATER_REFILL_STATION_ONBOARDING.md`. If the code and this prompt disagree, the code wins.

## Why

The water refill station (Aqua) asked for these:

1. Choose the **mode of delivery when charging**: at the counter, or by rider.
2. A sale sent with a rider is **not completed** until the rider comes back. Cash: complete it when the cash is handed in. M-Pesa: the rider
   must bring back the **M-Pesa code** before it can be completed.
3. A sale sent by rider must **appear on the Deliveries page**. Today the POS never creates a delivery order, so the board stays empty.
   That is most likely why the station says the Deliveries screen "does not work". Check the board for a runtime error too.
4. A **Returned** status, for a delivery the rider brings back.
5. **Trips per rider against fuel paid** (expenses), to spot unnecessary trips.
6. A **float** given at the start of the day for change, and an **end-of-day cash-up** that accounts for it.

## How the API models it (read this first)

- A rider delivery is a **delivery order**, not a sale. Until the rider is back, the order holds its stock back (reserved) and no money
  is recorded. "Complete the sale" = `POST /delivery-orders/{id}/deliver` with the payment. That call creates the sale. Show an open rider
  order as **"Not completed"** wherever the cashier looks for sales.
- A sale that came from a delivery has `deliveryMode: 1` (Rider). Counter sales have `0`.
- Riders are **names**, not users. They are matched without regard to case or surrounding spaces, so offer the names already used (from
  `GET /delivery-orders/summary` → `byRider[].riderName`) in a combo box. That way "Peter" is not typed five different ways.

## Rules that do not change

- Enums are sent as **numbers**. New and relevant ones:
  - `DeliveryOrderStatus`: `0` Pending, `1` OutForDelivery, `2` Delivered, `3` Cancelled, **`4` Returned** (new)
  - `PaymentMethod`: `0` Cash, `1` Credit, `2` MpesaTill, `3` MpesaStkPush, `4` MpesaReference
  - `DeliveryMode`: `0` Counter, `1` Rider (new)
  - `CashSessionStatus`: `0` Open, `1` Closed (new) · `CashMovementType`: `0` PayIn, `1` PayOut (new)
- Every response is `{ success, data, error: { code, message, details }, meta }`.
- Never compute totals, stock or expected cash in the browser. Show what the API returns.
- Hiding a button is not security. The API checks permissions on every call.

## Modules and permissions

| Need | Check |
|---|---|
| Delivery mode at the till, Deliveries menu | module **`deliveries`** |
| Cash-up menu | module **`cashUp`** (new, every business) |
| Send an order out with a rider at the till, or dispatch | `DeliveryOrders.Create` **and** `DeliveryOrders.Dispatch` |
| Complete (record) a delivery | `DeliveryOrders.Deliver` (+ `DeliveryOrders.AcceptCredit` for pay later) |
| Mark returned | **`DeliveryOrders.Return`** (new) |
| Fuel report | `DeliveryOrders.View` **and** `Expenses.View` |
| See the cash session | **`CashSessions.View`** (new) |
| Open the day with a float | **`CashSessions.Open`** (new, managers) |
| Pay in / pay out | **`CashSessions.RecordMovement`** (new) |
| Count out the day | **`CashSessions.Close`** (new) |

Add the five new permissions to `src/api/permissions.ts`. The Admin role gets them on API start-up; other roles need them assigned.

## 1. POS checkout: mode of delivery

On the payment step, add a **Delivery** toggle: **Counter** (default) / **Rider**. Show it only when the `deliveries` module is on.

**Counter** is unchanged (`POST /sales`).

**Rider**:
- Requires a registered customer. Reuse the customer picker with *Register new customer*.
- Fields: delivery address (prefilled from the customer's address), contact phone (optional, defaults to the customer's), rider
  name (combo box, required to send it now), optional notes.
- **Expected payment**: Cash, M-Pesa code (`4`), Till (`2`), or Pay later (`1`, only with `DeliveryOrders.AcceptCredit`). This is a
  guide for the rider, not a payment. No amount tendered and no code are asked for here.
- Charge button reads **"Send with rider"** and calls:

```http
POST /api/v1/delivery-orders
{
  "customerId": "…",
  "deliveryAddress": "Ruiru, Kimbo stage, green gate",
  "contactPhone": null,
  "notes": null,
  "lines": [ { "productId": "…", "unitOfMeasureId": "…", "quantity": 2 } ],
  "bundles": [ { "productBundleId": "…", "quantity": 1 } ],   // bundles now work on delivery orders
  "expectedPaymentMethod": 0,
  "riderName": "Peter",            // omit to leave it Pending on the board
  "deliveryTripId": null           // or a trip still out with Peter, to add this order to it
}
```

- The response is a `DeliveryOrderDto` with `status: 1` (OutForDelivery), `tripNumber` and `estimatedTotal`. Print a **delivery
  note** for the rider from it: order number, customer, address, phone, lines, amount to collect (`estimatedTotal`) and how
  (`expectedPaymentMethod`). Do not print a receipt: there is no sale yet.
- Clear the cart and show a toast linking to the order.
- Errors: `409 INSUFFICIENT_STOCK` (keep the cart), `403` without `DeliveryOrders.Dispatch` (suggest leaving out the rider so someone
  else dispatches), `400` on `deliveryTripId` if the trip is back or is another rider's.

## 2. Deliveries board

Columns **Pending**, **Out for delivery** (with a "Not completed" badge and the amount to collect), **Delivered today**, **Returned
today**.

Card actions:
- **Pending**: *Dispatch* (rider), *Cancel* (reason). Multi-select pending cards → **Send together** → `POST /delivery-orders/trips
  { riderName, deliveryOrderIds: [...] }` (one trip, returns `DeliveryTripDto`).
- **Out for delivery**:
  - **Complete sale** (`DeliveryOrders.Deliver`). The existing *Record delivery* dialog: Cash (amount handed in, change shown), M-Pesa code
    (**required**: the API refuses with 400 `mpesaReference` if empty; say *"Enter the M-Pesa code the rider brought back"*), Till
    (pick from unmatched), Pay later. Preselect the method from `expectedPaymentMethod`. Lines and bundles stay editable for a partial
    hand-over (send `lines` and/or `bundles`; leaving both out keeps the order as it was).
  - **Returned** (`DeliveryOrders.Return`) → `POST /delivery-orders/{id}/return { reason }`. Reason picker: *Customer not home*, *Refused*,
    *Wrong address*, *Other…*. Then invalidate `delivery-orders` and `stock`.
  - *Cancel* stays for orders called off before they left. A returned order cannot be cancelled.
- Show `tripNumber` on cards. Clicking it filters the board: `GET /delivery-orders?deliveryTripId=`.

`DeliveryOrderLineDto` now carries `productBundleId`, `bundleName`, `bundleQuantity`. Group lines sharing a `productBundleId` into one
row ("2 × 20L bottle + water"), as on the sale receipt.

## 3. Rider cash-up (existing page)

`RiderSummaryDto` gained `returnedCount`, `tripCount` and `cashExpectedNow` (cash the orders they have out should bring back).
`DeliverySummaryDto` gained `returnedCount` and `tripCount`. Add the columns.

## 4. Trips and fuel (new page under Deliveries)

- `GET /delivery-orders/trips?from=&to=&riderName=`: trips, newest first, each with `orderCount`, `deliveredCount`, `returnedCount`,
  `outCount`, `deliveredTotal`, `isBack`, `isWasted`, `orders[]`. Mark `isWasted` trips red ("came back with nothing delivered").
- `GET /delivery-orders/fuel-report?from=&to=&expenseCategoryId=&expectedFuelPerTrip=`: per rider `tripCount`, `ordersCarried`,
  `deliveredCount`, `returnedCount`, `wastedTrips`, `singleOrderTrips`, `fuelSpent`, `fuelPerTrip`, `expectedFuel`, `fuelVariance`,
  `fuelAccountedFor`, `aboveAverage`; totals at the top.
  - Filters: date range, expense category (default the **Transport & Fuel** category, code `TRN`, from `GET /expense-categories`),
    and *Expected fuel per trip (KES)*. Remember the last value per browser (`localStorage`, in try/catch). The API does not store it.
  - Highlight `fuelAccountedFor: false` ("fuel not accounted for by trips") and `aboveAverage: true`. Show `singleOrderTrips` as
    "could have been combined".

## 5. Expenses: tag fuel to a rider

On the expense form, add an optional **Rider** combo box (same names as above), shown when the `deliveries` module is on. Send it as
`riderName` on `POST /expenses`. On `PUT /expenses/{id}`, `riderName: null` keeps the tag and `""` removes it. List filter:
`GET /expenses?riderName=`. Show `riderName` in the list and detail. **Only expenses tagged with a rider count as that rider's fuel.**

## 6. Sales list

- Filter `GET /sales?deliveryMode=0|1` (*Counter* / *Rider*). Show a small *Rider* badge on rider sales (`deliveryMode: 1`).
- Add a **Not completed** tab that lists open rider orders (`GET /delivery-orders?status=1`) with the amount to collect, linking to the
  board. These are not sales yet, so they are not in `GET /sales`.

## 7. Cash-up: the day's float (new page, module `cashUp`)

| Call | Body | Returns |
|---|---|---|
| `GET /api/v1/cash-sessions/current` | | `CashSessionDto` or `data: null` (day not opened) |
| `POST /api/v1/cash-sessions/open` | `{ openingFloat, notes }` | `201`, `CashSessionDto` |
| `POST /api/v1/cash-sessions/{id}/movements` | `{ movementType: 0 PayIn \| 1 PayOut, amount, reason }` | `CashSessionDto` |
| `POST /api/v1/cash-sessions/{id}/close` | `{ countedCash, notes }` | `CashSessionDto` (closed, with `variance`) |
| `GET /api/v1/cash-sessions?from=&to=&status=&page=` | | paged `CashSessionDto[]` |
| `GET /api/v1/cash-sessions/{id}` | | `CashSessionDto` |

Screens:
- **No open session**: a card *"Open the day"* with the float amount (`CashSessions.Open`). Also show a dismissable banner on the POS
  for users with `CashSessions.Open` when `current` is null. The API does **not** block sales without a session.
- **Open session**: the running `breakdown` as a statement: opening float, + cash sales (count), + customer payments, + pay-ins,
  − cash refunds, − cash expenses, − supplier payments, − pay-outs, **= expected in drawer**. Then a movements table with *Pay in* and
  *Pay out* buttons (reason required; suggest "Banked", "Change for rider …", "Rider returned change", "Float top-up").
- If `breakdown.cashStillWithRiders > 0`, warn before closing: *"KES X is still with riders (N orders). Complete or return those
  deliveries before counting."* Allow closing anyway.
- **Close**: a counted-cash input (optionally a denomination counter that adds up into it). On submit, show expected, counted and
  **variance** (red if short, amber if over). A closed session is read-only and keeps its figures.
- **History**: list with date, branch, float, expected, counted, variance, who opened and closed.
- Errors: 400 *"…is still open at this branch"* on open (reload `current`), 400 *"…is closed"* on movement or close.
- Notifications: new type `CashSession.Variance` (links to `/cash-sessions/{id}`) and `DeliveryOrder.Returned` (links to
  `/deliveries?orderId={id}`).

## Tests to add

- POS: Rider mode sends `POST /delivery-orders` with `riderName` and `expectedPaymentMethod`, never `POST /sales`; Counter is unchanged.
- Record delivery: M-Pesa code empty → inline error, nothing sent.
- Board: a Returned card appears in *Returned today*; *Send together* posts the selected ids.
- Cash-up: expected figure is shown from `breakdown.expectedCash`, never recomputed; the close dialog shows the variance from the response.
