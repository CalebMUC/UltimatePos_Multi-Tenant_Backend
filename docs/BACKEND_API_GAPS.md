# Backend API gaps

What the API does not support yet, found while onboarding a water refilling station
([WATER_REFILL_STATION_ONBOARDING.md](WATER_REFILL_STATION_ONBOARDING.md)). None of these is implemented. Each needs owner approval
(marked) or a product decision before work starts. Contracts are proposals that follow the existing conventions: `/api/v1`,
`ApiResponse<T>`, `PERMISSION:` policies, append-only ledgers, tenant and branch filters. Until a gap is built, the frontend shows no
screen for it. It does not fake success with mock data.

| # | Gap | Station needs it? | Schema change | Status |
|---|-----|---|---|---|
| G1 | Bank transfer at the counter | Maybe | Yes | **Deferred by owner** |
| G2 | Split and partial payment on one sale | Maybe | Yes | Needs decision |
| G3 | Sales orders (order before fulfilment) | If they take orders | Yes | Ask owner |
| G4 | Deliveries | If they deliver | Yes | Ask owner |
| G5 | Reusable containers and deposits | If they lend containers | Yes | Ask owner |
| G6 | Water-quality and maintenance log, expiry reminders | Likely | Yes | Ask owner |
| G7 | Close a purchase order short, with a discrepancy reason | Yes | Small | Recommended |
| G8 | Receive without an order, in one call | Nice to have | No | Optional |
| G9 | Landed cost (delivery charges in stock cost) | No, while transport is an expense | Yes | Optional |
| G10 | Daily cash-up / reconciliation report | Yes | No | Recommended |
| G11 | Approval workflow for stock adjustments and expenses | Owner's choice | Yes | Ask owner |
| G12 | One dashboard call for the station's cards | Nice to have | No | Optional |
| G13 | Supplier lead time and reorder recommendation | Nice to have | Small | Optional |
| G14 | Per-business currency and timezone | No (Kenya only) | Yes | Platform decision |
| G15 | Customer-specific prices | Maybe | Yes | Ask owner |
| G16 | Role templates (Cashier, Storekeeper, …) | Convenience | No | Optional |
| R1 | Cancel of a purchase order is not serialised against a receipt | — | No | **Risk, fix soon** |
| R2 | Unique M-Pesa receipt number is not enforced | — | Index | **Risk, fix soon** |

---

### G1 — Bank transfer at the counter *(deferred by the owner on 2026-10-19)*
**Today:** `PaymentMethod` is Cash, Credit, MpesaTill, MpesaStkPush. A `Sale` has no payment reference. Customer *account*
payments already accept BankTransfer.
**Proposed:** add `PaymentMethod.BankTransfer`, and `Sale.PaymentReference` (string, required for BankTransfer). Record it like a cash
sale (completed, stock out), without `amountTendered`.
**Schema:** one nullable column on `Sales`. The enum is stored as a string, so the new value needs no migration.
**Frontend:** a payment option and a reference field.

### G2 — Split and partial payment on one sale
**Today:** one payment method per sale. Part-paid is only possible as a full credit sale plus a customer payment.
**Proposed:** a `SalePayment` table (`saleId, method, amount, reference, mpesaTransactionId?`). `POST /sales` takes `payments[]`. The
unpaid remainder posts to the customer ledger (registered customers only). Each M-Pesa part keeps the verified-status rules
(an STK part stays pending until confirmed).
**Decision needed:** may a walk-in leave a balance? (Recommend no.)

### G3 — Sales orders
**Today:** a sale is a completed counter sale. `Pending` exists only for STK payments.
**Proposed:** a `SalesOrder` (`Draft → Confirmed → PartiallyFulfilled → Fulfilled | Cancelled`) with lines and an optional
`scheduledFor`. `POST /sales-orders`, `POST /sales-orders/{id}/fulfil` with `{ lines[] }`. Fulfilling creates a normal `Sale`, the
**only** place stock leaves, so an order can never deduct stock twice. Optional reservation: the available quantity seen by sales
= on hand − reserved.
**Permissions:** `SalesOrders.Create`, `.View`, `.Fulfil`, `.Cancel`.

### G4 — Deliveries
**Depends on:** G3.
**Proposed:** delivery fields on the order: `deliveryAddress, contactPhone, deliveryFee, scheduledAt, riderUserId,
status (Pending, Dispatched, Delivered, Failed), dispatchedAt, deliveredAt, proofOfDeliveryUrl, recipientName`.
- `POST /sales-orders/{id}/dispatch`, `/deliver`.
- The fee is a sale line on a service product (no stock), so it counts as revenue.
- Report: delivery performance (on-time %, failed).

Counter collection stays the default.

### G5 — Reusable containers and deposits
**Today:** containers can only be sold as products.
**Proposed:** a container *type* (e.g. "20L jerrycan", with its deposit amount) and a `ContainerLedger` per customer: `Issued, Returned,
Damaged, Lost`, with quantities **separate from water** (never a stock movement of the water product).
- The deposit is posted to a **deposit liability**, not revenue. It is refunded on return, or kept on loss (then it becomes revenue
  or other income; accountant to confirm).
- `GET /customers/{id}/containers`: held, awaiting return.
- Customer-owned containers are not recorded at all.

**Owner questions:** see onboarding §11.

### G6 — Water-quality, cleaning and maintenance log
**Proposed:** an optional module, kept apart from stock and sales.
- `MaintenanceRecord` (`type: QualityTest, FilterChange, TankCleaning, PumpService, Inspection, Other`; `date, performedBy, result
  (free text and an attachment URL), cost?, nextDueOn?`).
- `LicenceRecord` (`name, number, issuedOn, expiresOn`).
- Reminders through the existing notification sweep when `nextDueOn` or `expiresOn` approaches.
- A cost may link to an expense; it is not a second copy of it.
- **No quality thresholds or compliance claims in software.** Results are recorded as reported by the lab.

**Permissions:** `Maintenance.View`, `.Record`.

### G7 — Close a purchase order short
**Today:** a short-delivered order stays `PartiallyReceived` forever, and cancelling is refused once anything was received.
**Proposed:** `POST /purchase-orders/{id}/close` with `{ reason }`. It sets a new status `ClosedShort` (keeps received quantities,
stops further receipts, clears the overdue alert), and records the discrepancy (ordered − received) and the reason.
`GET /purchase-orders/{id}/receipts` lists each receipt with date, user, delivery-note number and quantities. Today these are only on
stock movements and the supplier ledger.

### G8 — Receive without an order
**Today:** two calls (create the order, then receive it). This is auditable and works.
**Proposed (optional):** `POST /purchase-orders/receive-direct` with supplier and lines. It creates the order as `Received` in one
transaction. Same permission as receiving.

### G9 — Landed cost
**Today:** transport is an operating expense. That is consistent and counted once.
**Proposed (only if the owner wants transport inside the cost per litre):** `additionalCharges[]` on a receipt, spread over its lines
by value into `unitCostPerBaseUnit`, and **not** posted as an expense. Choose one treatment per business and document it, so it is
never both.

### G10 — Daily cash-up
**Proposed:** `GET /reports/cash-up?date=&branchId=&cashierId=`, with no schema change:
- cash sales, customer payments in cash, cash refunds and cash expenses, giving the **expected cash in the drawer**
- M-Pesa: till payments received, matched and unmatched, and STK confirmed
- per cashier

An optional `CashCount` (counted amount and variance) would need a table.

### G11 — Approvals
**Today:** adjustments and expenses post immediately. `Stock.Adjust` and `Expenses.Create` are the control.
**Proposed (if the owner wants it):** a `Pending` status on adjustment and expense requests, with `POST …/{id}/approve | reject`
(`Stock.ApproveAdjustments`, `Expenses.Approve`). Stock and expense totals move only on approval.

### G12 — Station dashboard in one call
**Today:** every card has a source endpoint (onboarding §10).
**Proposed (optional):** add to `GET /dashboard/summary` the fields `unitsSoldInBaseUnits` (by product), `receivedValue`,
`expensesTotal` and `payablesTotal`, each behind the permission that already guards it.

### G13 — Supplier lead time and reorder recommendation
**Proposed:** `Supplier.LeadTimeDays`. The stock-status report adds `suggestedOrderQuantity` = (average daily usage × (lead time +
cover days)) − on hand − on order. **Never creates or sends an order.**

### G14 — Currency and timezone per business
Hard-coded KES and UTC+3 are correct for Kenya. Needed only if the platform serves businesses outside Kenya.

### G15 — Customer-specific prices
**Today:** a customer buys at Retail or Wholesale prices.
**Proposed:** a `CustomerPrice` (`customerId, productId, unitOfMeasureId, price, effectiveFrom`), used before the price list.
`Products.ManagePricing`.

### G16 — Role templates
**Today:** only Admin is created with a business.
**Proposed:** seed optional, editable roles (Manager, Cashier, Storekeeper, Accountant) from a template, as in onboarding §9. Or add
`POST /roles/from-template`.

---

## Remaining risks found during the review (not fixed in this change)

**R1 — Cancel vs receive.** Receiving is now locked and re-checked, but `POST /purchase-orders/{id}/cancel` still checks the status
from an unlocked read. A cancel racing a receipt can leave an order `Cancelled` with stock received.
*Fix:* lock the order row in `SetPurchaseOrderStatusAsync` and refuse to cancel if anything has been received. Same pattern as
`ReceivePurchaseOrderAsync`.

**R2 — M-Pesa receipt numbers.** C2B duplicates are filtered by an existence check before insert, but `MpesaTransactions` has no unique
index on `MpesaReceiptNumber`, so two simultaneous deliveries of one Safaricom confirmation could store two rows.
*Fix:* a filtered unique index `("MpesaReceiptNumber") WHERE "MpesaReceiptNumber" IS NOT NULL`, after checking existing data for
duplicates, and treat the unique violation as "already received".
