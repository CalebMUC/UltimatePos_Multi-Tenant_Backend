# Frontend Task: Click Through the Dashboard's On Credit and Cost of Goods Cards

> **For:** the coding agent working on the Ultimate POS frontend (`Ultimate_Multi_Tenant_Frontend`).
> **Depends on:** the API release with `GET /api/v1/dashboard/credit-customers` and `baseUnitSymbol` on the profit rows. No migration.
> Before you start, read `src/UltimatePos.Api/Controllers/DashboardController.cs`, `src/UltimatePos.Application/Dashboard/Dtos/DashboardDtos.cs`
> and `src/UltimatePos.Application/Costing/Dtos/CostingDtos.cs`.
> If the code and this prompt disagree, the code wins.

## Why

The dashboard shows two totals the owner cannot look behind. **On credit** says how much was sold on credit, but not to whom.
**Cost of goods sold** says what the goods cost, but not which goods. Clicking either card must show the list behind that number, for
the same dates and branch the dashboard is showing.

## Rules that do not change

- Every response is `{ success, data, error: { code, message, details }, meta }`.
- **Never compute totals in the browser.** Show what the API returns. Sorting rows is fine.
- The list must add up to the card. The API guarantees this only if you send the dashboard's own `range` and `branch.scope`.
- Hiding a button is not security. The API checks permissions on every call.

## Words that are easy to confuse

| On screen | Meaning | Part of this task? |
|---|---|---|
| **On credit** | Sold on credit in the chosen period | Yes |
| **Customers owe** | Everything owed right now, however old | No, only linked to from the new dialog |
| **Customer credit held** | Customers who have **prepaid**: the business owes them. Their balance is negative | No, leave the card as it is |

## 1. API

### On credit (new)

`GET /api/v1/dashboard/credit-customers?from=&to=&utcOffsetMinutes=&branchId=`

Needs `Sales.View`, the same permission that shows the On credit card, so everyone who sees the card can open it.

```ts
// add to src/api/dto/reports.ts
export interface DashboardCreditCustomerDto {
  customerId: Guid;
  customerName: string;
  phoneNumber: string | null;        // null without Customers.View
  saleCount: number;
  creditSalesAmount: number;         // sold on credit in the period, before payments and returns
  lastSaleAt: DateTimeString;        // the customer's latest credit sale in the period
  outstandingBalance: number | null; // owed NOW; null without Customers.ViewLedger
}
export interface DashboardCreditCustomersDto {
  from: DateOnly;
  to: DateOnly;
  customerCount: number;
  totalCreditSales: number;          // equals summary.sales.creditSalesAmount
  customers: DashboardCreditCustomerDto[]; // biggest first
}
```

```ts
// add to dashboardApi in src/api/endpoints/other.ts
creditCustomers: (from?: DateOnly, to?: DateOnly, scope?: BranchScope) =>
  api.get<DashboardCreditCustomersDto>('/dashboard/credit-customers', cleanParams({ from, to, ...tz(), ...branchParam(scope) })),
```

`outstandingBalance` needs care:

- It is what the customer owes **today**, across **every branch** and every period. It can be bigger or smaller than `creditSalesAmount`.
- `null` means the user may not see it. It does **not** mean zero, so never show `null` as 0.
- `0` means they have paid. A negative value means they have prepaid.

### Cost of goods (existing endpoint)

Use `costingApi.profit(start, end, undefined, branch.scope)`, with `start` and `end` from `dayBounds(range.from, range.to)` in
`@/features/reports/ReportLayout`. It needs `Costing.View`, the same permission that shows the card. `totalCost` equals
`summary.profit.costOfGoodsSold`.

Add one field to `ProfitRowDto` in `src/api/dto/inventory.ts`:

```ts
baseUnitSymbol: string | null; // "l", "kg", "pkt": the unit of quantitySoldInBaseUnits
```

## 2. Make `StatCard` clickable

In `src/components/shared/Display.tsx`, add an optional `onClick` to `StatCard`:

- With `onClick`, render a real `<button type="button">` with the same look, plus `w-full text-left`, a hover style and a visible focus ring.
- Its accessible name must say what it opens, for example `aria-label="On credit: view customers"`.
- Without `onClick`, the output stays exactly as it is today. Every other card on the site must look and behave the same.

## 3. The two dialogs

Add `src/features/dashboard/CreditCustomersDialog.tsx` and `src/features/dashboard/CostOfGoodsDialog.tsx`. Use `Dialog` and
`DialogContent size="xl"` from `@/components/ui/overlays`, and `DataTable` with `searchable` and `csv`, the same way
`src/app/(app)/reports/profit/page.tsx` does.

Both take `open`, `onOpenChange`, the dashboard's `range` and its `branch`. Both fetch **only while open** (`enabled: open`), with keys such as
`key('dashboard', { creditCustomers: range, branchId: branch.branchId })`. Show the period in the dialog description with `formatDateOnly`.

Wire them up in `src/app/(app)/dashboard/page.tsx`:

- On credit: `hint="View customers"` and an `onClick` that opens `CreditCustomersDialog`.
- Cost of goods sold: `hint="View goods"` and an `onClick` that opens `CostOfGoodsDialog`.

### On credit dialog

Title **Customers on credit**. Above the table show `totalCreditSales` and `customerCount`.

| Column | Shows |
|---|---|
| Customer | `customerName`. Links to `/customers/{customerId}` only if the user has `Customers.View` |
| Phone | `phoneNumber` as a `tel:` link. Leave the column out without `Customers.View` |
| Sales | `saleCount` |
| On credit | `creditSalesAmount` |
| Last sale | `lastSaleAt` with `formatDateTime` |
| Owes now | `outstandingBalance`. Leave the column out without `Customers.ViewLedger`. Above 0: warning colour. 0: "Paid". Below 0: "Prepaid" and the amount without the minus |

- With the Owes now column showing, add under the table: *"Owes now covers all branches and all dates, so it can differ from the amount on credit in this period."*
- This list shows only customers who bought on credit **in the period**. Someone with an old unpaid invoice and no new credit sale is not on it.
  So with `CustomerReports.View`, add a link in the footer: **See everyone who owes** → `/reports/customer-aging`.
- Empty state: *"No credit sales in this period."*

### Cost of goods dialog

Title **Cost of goods sold**. Above the table show `totalCost`. Sort rows by `cost`, highest first, before passing them to the table.

| Column | Shows |
|---|---|
| Product | `productName`. Links to `/products/{productId}` only if the user has `Products.View` |
| Qty sold | `quantitySoldInBaseUnits` with `baseUnitSymbol`, using `QuantityText` |
| Cost | `cost` |
| Revenue | `revenue`, excluding VAT |
| Gross profit | `grossProfit` |
| Margin | `marginPercent` with `formatPercent` |

- A row with `uncostedRevenue > 0` has sales with no recorded cost, which are left out of `cost`. Show a **No cost recorded** warning badge on it.
- Returns are netted off in the period they happen, so a row can show a negative quantity or cost. Show the API's value as it is.
- Footer link: **Profit by product** → `/reports/profit`.
- Empty state: *"No sales in this period."*

## 4. Tests

Add `src/features/dashboard/DashboardDrilldowns.test.tsx`, built like `CustomersPanel.test.tsx` (msw, `renderWithProviders`, `signIn`). Cover:

- [ ] Opening the dashboard calls neither list endpoint. The existing tests use `onUnhandledRequest: 'error'`, so they must still pass.
- [ ] Clicking On credit calls `/dashboard/credit-customers` with the dashboard's `from`, `to` and `branchId`, and shows the customers.
- [ ] Without `Customers.ViewLedger`, there is no Owes now column, and no "0" or "Paid" shown in its place.
- [ ] Without `Customers.View`, there is no Phone column and the customer name is not a link.
- [ ] `outstandingBalance` of 0 shows Paid; a negative one shows Prepaid.
- [ ] Clicking Cost of goods sold calls `/costing/profit` with the dashboard's day bounds and branch, and rows are sorted by cost with their unit.
- [ ] A row with `uncostedRevenue` shows the warning.
- [ ] A `StatCard` with no `onClick` does not render a button.

## Acceptance checks

1. Pick any period and branch. The dialog total equals the card, to the cent, for both cards.
2. Change the dates or branch, then reopen. The list follows the dashboard.
3. Void a credit sale. It leaves both the card and the list.
4. A customer who paid after buying on credit stays on the list, with Owes now at Paid.
5. Both cards open with the keyboard (Tab, then Enter), and Esc closes the dialog.
6. At phone width the table scrolls inside the dialog and the page does not scroll sideways.
