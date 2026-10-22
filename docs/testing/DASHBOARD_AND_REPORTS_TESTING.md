# Testing the Dashboard and Reports endpoints

Everything here was run against a copy of the `ultimatepos_dev` database, and the expected values below are what the API returned.
The test data is built **on what is already in your database**, so it uses real names and real prices:

| Already in the database | Used for |
|---|---|
| Alvet Agrochemicals Limited (a **Manufacturer**) | the business everything belongs to |
| Kamau Enterprises (supplier, 31-day terms) | purchase orders |
| Famakeya Center Limited (customer, wholesale prices) | credit sales, payment, ageing |
| Super Dairy Cattle Basemix `DAIRY-FG-000002` (kg: wholesale 150, retail 160) | made in a production run, sold |
| Supreme Dog Meal `PET-FG-000002` (kg: wholesale 600, retail 750) | bought-in stock, sold |
| Yeast Dairy Nutripak `DAIRY-FG-000007` (g, reorder level 20), Multimix Dairy Premix `DAIRY-FG-000003` (g, reorder level 20) | low-stock and out-of-stock alerts |
| 13 expense categories (RENT, UTIL, TRN, ...) | expenses |

What the database does **not** have, and the seed script adds: raw materials, stock, purchases, a formula, sales, payments and expenses.
Your catalogue is all finished goods, and a Manufacturer may only buy raw materials and packaging, so the script creates three raw
materials (Limestone Powder, Dicalcium Phosphate, Premix Salt) and puts the bought-in finished goods into stock with stock adjustments.

## 1. Set up

1. Start the API (`dotnet run --project src/UltimatePos.Api --launch-profile http`, which listens on `http://localhost:5062`).
2. **Use a database you can throw away.** The seed script writes real records, and the API gives you no way to delete a sale or a
   stock movement. To make a copy of dev (the seed script also refuses to run if the database already has sales, so it cannot double-seed):

   ```powershell
   docker exec ultimatepos-db psql -U ultimatepos -d postgres -c "create database ultimatepos_test"
   docker exec ultimatepos-db pg_dump -U ultimatepos -d ultimatepos_dev -Fc | docker exec -i ultimatepos-db pg_restore -U ultimatepos -d ultimatepos_test --no-owner
   $env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=ultimatepos_test;Username=ultimatepos;Password=<your password>"
   dotnet run --project src/UltimatePos.Api --launch-profile http
   ```
3. Seed the data, then create the restricted users:

   ```powershell
   python docs/testing/seed_test_data.py            # add the base URL as an argument if it is not localhost:5062
   python docs/testing/create_test_users.py
   ```

Everything the script creates is stamped with **today's date** (the API sets timestamps itself), so every request below uses
`from=<today>&to=<today>`. The examples use `2026-10-17`; replace it with your date. Step 5 shows how to backdate invoices for the ageing report.

Accounts: `admin@ultimatepos.local` / `Admin@12345` and, from `create_test_users.py`, `nopermissions@test.local`,
`dashboard@test.local` and `salesstaff@test.local`, all with password `Test@12345`.

> **Log in again after any permission change.** Permissions travel inside the login token, so a token issued earlier does not have them.

## 2. Calling the API

In Swagger (`http://localhost:5062/swagger`): `POST /api/v1/auth/Login` with `{"email": "...", "password": "..."}`, copy `data.token`, click
**Authorize**, paste it (no `Bearer ` prefix). Or with curl:

```bash
TOKEN=$(curl -s localhost:5062/api/v1/auth/Login -H 'Content-Type: application/json' \
  -d '{"email":"admin@ultimatepos.local","password":"Admin@12345"}' | python -c "import sys,json;print(json.load(sys.stdin)['data']['token'])")
curl -s "localhost:5062/api/v1/dashboard/summary?from=2026-10-17&to=2026-10-17" -H "Authorization: Bearer $TOKEN"
```

Two things that catch people out:
- **Enums in request bodies are numbers**, not names (the API has no string-enum converter): `itemType` 0 RawMaterial / 1 Packaging / 2 FinishedGood;
  `paymentMethod` 0 Cash / 1 Credit / 2 MpesaTill / 3 MpesaStkPush; `costType` 0 Labour / 1 Packaging / 2 Utilities / 3 Transport / 4 Overhead / 5 Other.
  In **responses** most enums are names (`"status": "LowStock"`), but a few DTOs return numbers (a business's `kind`: 0 Manufacturer, 1 Trader).
- Responses use the envelope `{ "success", "data", "error", "meta" }`. List endpoints put paging in `meta`.

## 3. What the seed script creates

| Step | What | Result to remember |
|---|---|---|
| Purchase `PO-000001` | 500 kg limestone @20, 300 kg DCP @60, 100 kg salt @15 from Kamau, received in full | stock in; supplier spend 29,500 |
| Purchase `PO-000002` | 200 kg limestone @22, due 3 days ago, **only 80 kg arrives** | an overdue open order, 120 kg / 2,640 outstanding |
| Production `PR-000001` | formula: 100 kg basemix = 60 kg limestone + 30 kg DCP + 10 kg salt. Run 200 kg, extra costs labour 400 + utilities 150 | material 6,333.10 (limestone is costed at its **weighted average**, 20.28/kg, not 20), total 6,883.10, **34.42 per kg** |
| Stock adjustments | Dog Meal +100 kg @420 then -5 kg (spillage); Nutripak +15 g @1.20; Multimix +10 g then -10 g (expired) | Nutripak **low** (15 <= 20), Multimix **out of stock** (0) |
| `INV-000001` | walk-in, cash: 50 kg basemix @160 + 10 kg dog meal @750 | 15,500 |
| `INV-000002` | Famakeya, credit: 100 kg basemix @150 (**wholesale** price, from the customer's price type) | 15,000 |
| `INV-000003` | Famakeya, credit: 20 kg dog meal @600 | 12,000 |
| `INV-000004` | walk-in, cash: 2 kg dog meal, then **voided** | 1,500, counted as voided, not revenue |
| Payment | Famakeya pays 10,000 | settles the oldest invoice first: INV-000002 has 5,000 left |
| Expenses | rent 25,000, utilities 4,200, transport 1,800 | 31,000 |

## 4. Expected results (admin, `from=to=today`)

Check these by hand: if one is off, that is a bug worth reporting.

### Dashboard: `GET /api/v1/dashboard/summary?from=...&to=...`

| Field | Expected | Why |
|---|---|---|
| `sales.saleCount` / `revenue` | 3 / **42,500** | 15,500 + 15,000 + 12,000. The voided sale is not counted. |
| `sales.averageSaleValue` | 14,166.67 | 42,500 / 3 |
| `sales.creditSalesAmount` | 27,000 | INV-2 + INV-3 |
| `sales.voidedCount` / `pendingCount` | 1 / 0 | |
| `profit.costOfGoodsSold` | **17,762.33** | 150 kg basemix x 34.4155 + 30 kg dog meal x 420 |
| `profit.grossProfit` / `grossMarginPercent` | 24,737.67 / 58.21 | |
| `profit.operatingExpenses` / `netProfit` | 31,000 / **-6,262.33** | the month's rent makes today a loss |
| `profit.isComplete` | true | every sold line has a cost; `uncostedRevenue` is 0 |
| `receivables.totalOutstanding` | **17,000** | 5,000 + 12,000; `customersOwing` 1 |
| `stock.lowStockCount` / `outOfStockCount` | **1 / 1** | Nutripak / Multimix |
| `stock.totalStockValue` | 53,965.68 | `productsMissingCost` 0 |
| `production.runCount` / `totalCost` | 1 / 6,883.10 | |
| `hiddenSections` | `[]` | admin sees everything |

### Reports (admin)

| Request | Expected |
|---|---|
| `reports/sales?from&to` | revenue 42,500; `byPaymentMethod`: Credit 27,000 (63.53%), Cash 15,500 (36.47%); `topCustomers`: Famakeya 27,000, **Walk-in** 15,500; `topProducts`: Basemix 150 kg / 23,000, Dog Meal 30 kg / 19,500 (the 2 kg voided is excluded); `voidedCount` 1, `voidedAmount` 1,500 |
| `reports/production-costs?from&to` | 1 run; material 6,333.10, additional 550, total 6,883.10; basemix row: yield 100%, 34.42 per kg; `additionalCostsByType`: Labour 400 (72.73%), Utilities 150; `topIngredients`: DCP 3,600 (56.84%), Limestone 120 kg / 2,433.10, Salt 300 |
| `reports/customer-aging?includeInvoices=true` | Famakeya 17,000 outstanding, all in `days0To30`; open invoices INV-000002 (5,000 left of 15,000) and INV-000003 (12,000) |
| `reports/inventory/stock-status` | 7 products: 5 in stock, 1 low (Nutripak, shortfall 5), 1 out (Multimix, shortfall 20); value 53,965.68; **out of stock is listed first**. `?status=LowStock` lists only Nutripak |
| `reports/inventory/movements?from&to` | Basemix: produced 200, sold 150, closing 50. Dog Meal: adjusted in 100, out 5, sold 30 (net of the void), closing 65. Limestone: received 580, consumed 120, closing 460. Totals: received value 31,260, sold cost 17,762.33, consumed cost 6,333.10 |
| `reports/inventory/adjustments?from&to` | 5 adjustments (3 increases, 2 decreases); increase value 42,038, decrease value 2,120, net 39,918; each row shows who made it and the reason |
| `reports/purchases/summary?from&to` | 2 orders; ordered 33,900, received 31,260, outstanding 2,640; by status: Received 29,500, PartiallyReceived 4,400 |
| `reports/purchases/open-orders` | 1 order: `PO-000002`, overdue by 3 days, 120 kg / 2,640 outstanding |
| `reports/purchases/price-trends?from&to` | Limestone first 20, latest 22, **+10%**, quantity-weighted average 20.5714; DCP and Salt have a single purchase, so `changePercent` is `null`. Add `&productId=<limestone id>` to see each purchase as `points` |

## 5. Optional: backdate invoices to test the ageing buckets

All the invoices are dated today, so every balance sits in the 0-30 bucket. The API cannot backdate, so do it in SQL on the **test** database:

```powershell
docker exec -i ultimatepos-db psql -U ultimatepos -d ultimatepos_test < docs/testing/backdate_invoices.sql
```

Now `reports/customer-aging?includeInvoices=true` gives `days31To60` **12,000** (INV-000003, 40 days) and `days61To90` **5,000** (INV-000002, 75 days:
the 10,000 payment went to this oldest invoice first). `totalOutstanding` is still 17,000. Today's dashboard revenue drops to 15,500, because the two
credit sales no longer fall on today.

## 6. Who sees what (the permission tests)

Log in as each user and call the same requests.

| User | `dashboard/summary` | `dashboard/profit` | `reports/sales` | `reports/customer-aging` |
|---|---|---|---|---|
| no token | 401 | 401 | 401 | 401 |
| `nopermissions@test.local` | 403 | 403 | 403 | 403 |
| `dashboard@test.local` (Dashboard.View only) | **403** | 403 | 403 | 403 |
| `salesstaff@test.local` (no Costing.View) | **200**, partial | 403 | 200 | 403 |
| admin | 200, everything | 200 | 200 | 200 |

`dashboard@test.local` gets 403 and not an empty page: `Dashboard.View` only opens the door, and every section needs its own permission.

What `salesstaff` gets back from `dashboard/summary` (note the nulls, not zeros):

```json
{ "sections": ["Sales", "Stock"], "hiddenSections": ["Profit", "Receivables", "Production"],
  "sales": { "saleCount": 3, "revenue": 42500, "...": "..." },
  "profit": null, "receivables": null, "production": null,
  "stock": { "lowStockCount": 1, "outOfStockCount": 1, "totalStockValue": null, "productsMissingCost": null } }
```

Also check as `salesstaff`: in `inventory/stock-status`, `movements` and `adjustments`, every cost and value field (`averageUnitCost`, `stockValue`,
`totalStockValue`, `soldCost`, `netValue`, ...) is `null` while the **quantities are identical to admin's**. The adjustment audit trail (who, when, why) is still shown.

Every report endpoint's permission:

| Endpoint | Needs |
|---|---|
| `dashboard/summary` | `Dashboard.View` plus at least one section permission |
| `dashboard/sales` / `profit` / `receivables` / `stock` / `production` | `Dashboard.View` plus `Sales.View` / `Costing.View` / `Customers.ViewLedger` / `Stock.View` / `Production.View` |
| `reports/sales` | `SalesReports.View` |
| `reports/production-costs` | `ProductionReports.View` |
| `reports/customer-aging` | `CustomerReports.View` |
| `reports/inventory/*` | `InventoryReports.View` (cost fields also need `Costing.View`) |
| `reports/purchases/*` | `PurchaseReports.View` |

## 7. Edge cases worth trying

| Request | Expected |
|---|---|
| `reports/sales?from=2026-10-31&to=2026-10-01` | 400 `INVALID_ASSIGNMENT` (from after to) |
| `reports/sales?from=2025-01-01&to=2026-12-31` | 400 (range over 366 days) |
| `dashboard/sales?utcOffsetMinutes=5000` | 400 (offset must be -840..840) |
| `reports/inventory/stock-status?usageDays=0` | 400 (1..366) |
| `dashboard/summary` with no dates | today (Nairobi time); one date alone means that single day |
| `reports/sales?from=<a quiet day>&to=<same>` | zeros, not an error; every day in a range gets a row in `byDay` |
| `reports/sales?...&top=100` | capped at 50 |
| `reports/inventory/adjustments?...&rows=1` | one row, `truncated: true`, totals still cover all five |
| Late-night sale | sales are placed on the **Nairobi** day (UTC+3): a sale at 22:00 UTC on the 16th belongs to the 17th. Try `utcOffsetMinutes=0` to move it back |

## 8. Things to know

- **The business kind cannot be changed through the API.** Your existing business was migrated with `Kind = Manufacturer`
  (Alvet mixes premixes from formulas, so that fits). The update endpoint has no `kind` field, and registration requires it explicitly. If Alvet
  should be a Trader, fix it in the database: `update "Businesses" set "Kind" = 'Trader';`. A Trader may buy only finished goods and cannot
  buy raw materials, so the purchase steps above would fail.
- **No delete endpoints for transactions.** Sales can be voided, stock can be adjusted back, expenses can be voided, but nothing is removed.
  To start again, drop the test database and recreate it from dev.
- **M-Pesa sales are not seeded.** Till and STK-push sales need Safaricom's sandbox. A `Pending` sale (pending count, pending amount) only appears that way.
- **Dashboard stock value includes deactivated products**, while the low and out-of-stock counts do not. Deactivated products are rare in the seed, so you
  will not see a difference unless you deactivate one that has stock.
