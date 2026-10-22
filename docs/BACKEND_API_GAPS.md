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
| G3 | Sales orders (order before fulfilment) | Yes, for deliveries | Yes | **Built as delivery orders**; general orders not needed |
| G4 | Deliveries | Yes (free) | Yes | **Built** (fee, time slots, proof of delivery not yet) |
| G5 | Reusable containers and deposits | No: containers are sold, never lent | — | **Not needed** (owner, 2026-10-19) |
| G6 | Water-quality and maintenance log, expiry reminders | Likely | Yes | Ask owner |
| G7 | Close a purchase order short, with a discrepancy reason | Yes | Small | Recommended |
| G8 | Receive without an order, in one call | Nice to have | No | Optional |
| G9 | Landed cost (delivery charges in stock cost) | No, while transport is an expense | Yes | Optional |
| G10 | Daily cash-up / reconciliation report | Yes | Yes | **Cash built** as cash sessions (2026-10-22); M-Pesa side and per cashier not yet |
| G11 | Approval workflow for stock adjustments and expenses | Owner's choice | Yes | Ask owner |
| G12 | One dashboard call for the station's cards | Nice to have | No | Optional |
| G13 | Supplier lead time and reorder recommendation | Nice to have | Small | Optional |
| G14 | Per-business currency and timezone | No (Kenya only) | Yes | Platform decision |
| G15 | Customer-specific prices | Maybe | Yes | Ask owner |
| G16 | Role templates (Cashier, Storekeeper, …) | Convenience | No | Optional |
| G17 | M-Pesa paid outside the POS, cashier types the code | Yes, until C2B is wired | Yes | **Built** (2026-10-21) |
| G18 | Separate till and paybill on the business profile | Yes | Yes | **Built** (2026-10-21) |
| R1 | Cancel of a purchase order is not serialised against a receipt | — | No | **Risk, fix soon** |
| R2 | Unique M-Pesa receipt number is not enforced | — | Index | **Risk, fix soon** |
| R3 | Two delivery orders taken at the same instant can both reserve the last stock | — | No | Low risk |

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

### G3 / G4 — Delivery orders *(built 2026-10-19)*
**Built:**
- `DeliveryOrder`: `Pending → OutForDelivery → Delivered | Cancelled`.
- An open order reserves its stock.
- Delivery creates an ordinary `Sale` in the same transaction, the only place stock leaves, so an order can never deduct twice.
- Payment on delivery by cash, till or credit with a promised date; credit needs `DeliveryOrders.AcceptCredit`.
- A rider name (no rider login), and the per-rider summary.

See onboarding §6.1.

**Not built, add when needed:**
- *Delivery fee:* a service product (no stock) added as a line, so it counts as revenue.
- *Scheduled delivery:* `scheduledFor` on the order and a "due today" filter.
- *Proof of delivery:* `recipientName`, `proofOfDeliveryUrl`.
- *Delivery from the rider's phone:* a Rider role limited to their own orders, and STK at the door. The owner chose cashier
  recording.
- *Partial delivery:* today the cashier records what was handed over, and the rest is dropped. A follow-up order is taken for it.
- *Editing an order before dispatch:* cancel and re-create today.
- *Delivery performance:* on-time %, failed-delivery rate.

### G5 — Reusable containers and deposits *(not needed)*
The owner confirmed on 2026-10-19 that containers are never lent on deposit. A customer either brings their own (pays for the refill
only) or buys one with the water, which is an ordinary product. Nothing to build. Revisit only if deposits are introduced.

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
**Built (2026-10-22), cash side:** cash sessions per branch: an opening float, pay-ins and pay-outs, and a close that fixes expected,
counted and variance (`/cash-sessions`, onboarding §8.1). **Still open:** the M-Pesa reconciliation and a per-cashier split below.

**Originally proposed:** `GET /reports/cash-up?date=&branchId=&cashierId=`, with no schema change:
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

### G17 — M-Pesa paid outside the POS *(built 2026-10-21)*
A stopgap until till payments reach the POS through Daraja C2B. The customer pays the business's till, paybill or phone, and the
cashier types the confirmation code from the SMS. **Nobody checks it with Safaricom**, so reconcile these sales against the M-Pesa
statement.
- `PaymentMethod.MpesaReference = 4`. `CreateSaleRequestDto.mpesaReference` and `DeliverDeliveryOrderRequestDto.mpesaReference`. The
  code is stripped of spaces, upper-cased and must match `^[A-Z0-9]{10}$`. Otherwise the API returns 400 with
  "Enter the 10-character M-Pesa code from the customer's SMS, e.g. SJK4H7LM2P."
- The code can be on only one sale per business that is not voided or failed, enforced by the filtered unique index
  `IX_Sales_BusinessId_MpesaReference`. A repeat returns **409 `DUPLICATE_REFERENCE`**: "M-Pesa code {code} is already on sale {saleNumber}."
  A voided sale frees its code. The preview accepts the field and does not check it.
- The sale is Completed at once, like cash, with no `amountTendered` or `changeDue`. `SaleDto.mpesaReference` returns the code.
  `GET /sales?search=` matches the code. Reports group the sales as `"MpesaReference"`. Refunds work as for a till sale.
- Deliveries accept it. The rider cash-up counts it in `paidByMpesaTill` (all M-Pesa), not in `cashCollected`.
- Each sale records a `Sale.MpesaReferenceRecorded` audit event with the code. No notification is sent.

### G18 — Till and paybill *(built 2026-10-21)*
`BusinessProfile.MpesaTillNumber` and `MpesaPaybillNumber` are optional, 5–7 digits each, and returned on `BusinessDto`.
`MpesaShortCode` is now derived as **till ?? paybill** and still routes C2B and STK. If a business has both, only its till routes.
- If either new field is sent, the `mpesaShortCode` sent with it is ignored. An older client that sends only `mpesaShortCode` has it
  stored as the till.
- A till or paybill already used by another business (as its short code, till or paybill) or by a branch returns 409 `BUSINESS_EXISTS`.
  The old short-code clash returned 400 and now returns this 409 too.
- Migration `MpesaReferenceAndTillPaybill` copied every existing short code into the till. Where it was really a paybill, an admin
  moves it in Settings.
- A branch's own `mpesaShortCode` is unchanged.

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

**R3 — Reservations at order time.** Creating a delivery order checks *on hand − reserved* without a lock, so two orders taken at the
same instant can both reserve the last water, and the counter then sees less than nothing available until one is delivered or
cancelled. Nothing goes negative: recording a delivery is checked against stock on hand (not reservations) and guarded in SQL, so if
the water really is short, that delivery is refused when it is recorded.
*Fix (if it ever matters):* lock the product's stock row (`StockLedger.LockAsync`) while creating the order.
