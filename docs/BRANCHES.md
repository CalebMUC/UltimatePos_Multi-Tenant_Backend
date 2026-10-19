# Branches

A business can trade from several shops. This document covers how the API models them. The frontend's view is in `docs/Frontend-Branches-Prompt.md`.

## Decisions

| Question | Decision |
|---|---|
| Branch identity | `Branch.BranchNumber` (int, 0–99) is the KRA eTIMS branch id, formatted two-digit (`KraBranchId` → `00`, `01` …). `00` is the head office, created with the business; it can't be closed. Numbers are unique per business and never reused (a closed branch keeps its number). |
| What's shared | Products, prices (one price list for every branch), categories, units, customers, suppliers, roles, formulas. |
| What's per branch | Stock levels, batches, stock movements, sales, returns, purchase orders (delivery branch), production runs, expenses (or business-wide), M-Pesa transactions (or business-wide). |
| Costing | **Weighted average cost per product and branch.** A transfer moves stock at the source's average cost, which blends into the destination's average. Business-wide figures are value-weighted across branches. |
| Document numbers | One sequence per business (`SAL-`, `PO-`, `TRF-` …). The branch is a separate field on the document. |
| Branch limit | Optional `BusinessLicense.MaxBranches`, kept on `BusinessProfile.LicenseMaxBranches` by `LicensePolicy.Coverage`. Checked when a branch is created or reopened (`BRANCH_LIMIT_REACHED`). Null = unlimited. |

## Scoping: `IBranchContext`

`BranchContext` (scoped, in `Application/Common`) sits next to `ITenantContext`:

- **`ActiveBranchId`** is where writes happen. It comes from the token's `branch_id` claim, or from `Use(id)` for system code, or from `Narrow(id)` during a narrowed read. `RequireActiveBranchId()` throws `ForbiddenException` when there is none.
- **`Unrestricted`** is true when the caller holds `Branches.AccessAll`, or when there's no signed-in user (webhooks, workers). Reads then see every branch.
- **`VisibleBranchIds`** comes from the token's `branch` claims (the user's assigned branches), or is the single narrowed branch.
- **`Narrow(id)`** is used by `BranchScopeMiddleware` for any GET with `?branchId=`, after checking `CanSee`. Otherwise it returns 403 `BRANCH_FORBIDDEN`.

### Query filters

EF allows one filter per entity, so the branch condition is **ANDed with the business condition**, in `UltimatePosDbContext.OnModelCreating`:

```csharp
e.BusinessId == CurrentBusinessId && (BranchUnrestricted || VisibleBranchIds.Contains(e.BranchId))
```

- **Branch-owned entities:** StockLevel, StockMovement, StockBatch, Sale, SaleReturn, PurchaseOrder and ProductionRun.
- **Line tables** filter through their parent's `BranchId`: SaleLine, SaleLineBatchAllocation, SalePrescription, SaleReturnLine, PurchaseOrderLine, ProductionRunInput and ProductionRunCost.
- **Expense:** a null `BranchId` (business-wide) is visible only when unrestricted.
- **MpesaTransaction:** a null `BranchId` (paid to the business's short code) is visible to everyone in the business.
- **Not branch-filtered:**
  - `Branch` (business filter only). `BranchService` hides branches the caller can't see.
  - `StockTransfer` (business filter only). A transfer touches two branches, so `StockTransferService` shows it to staff of either end.
  - `UserBranch`. Every query on it uses `IgnoreQueryFilters()`, which is why EF logs a "required end … has a global query filter" warning at startup; the warning is harmless.

### Stamping and guards (`SaveChanges`)

For every added `IBranchEntity`:

- an empty `BranchId` is stamped with the active branch (or throws when there is none);
- an explicit `BranchId` must be one the caller `CanSee`.

Changing the `BranchId` of an existing row is refused. Stock moves between branches only by transfer.

## Stock ledger

`StockLedger` keys everything by `(ProductId, BranchId)`:

- the `StockLevels` primary key is `(ProductId, BranchId)`, and every upsert uses `ON CONFLICT ("ProductId", "BranchId")`;
- batches are unique per `(BranchId, ProductId, BatchNumber)`, and FEFO only reads the movement's branch;
- `LockAsync(productIds, branchId?)` and `RevalueAsync(productId, cost, branchId?)` default to the active branch;
- a movement's `BranchId` defaults to the active branch when left empty.

Callers that act on another branch's document set it explicitly:

- sales use `sale.BranchId` (the M-Pesa callback has no signed-in branch);
- voids and returns restock the sale's branch;
- purchase receipts go to `order.BranchId`;
- transfers use the source branch on dispatch and the destination on receipt.

Threshold events (low, out of stock, replenished) carry the branch:

- `AlertKeys.StockLow(productId, branchId)` and `StockOut(productId, branchId)`;
- the summary names the branch, for example "… at 01 Westlands";
- `SystemEvent.BranchId` is set.

### Reads

- **Lists** (`GetStockLevelsAsync`, batches, movements) return one row per product and branch over the visible branches.
- **Operational reads** use the active branch: `GetStockLevelAsync`, `GetQuantitiesOnHandAsync`, `GetSellableQuantitiesAsync` and `GetAverageCostsAsync` feed the till, production, adjustments and revaluation.
- **Aggregates:**
  - stock valuation and the stock-status report give one row per product (quantity summed, value summed at each branch's own cost, average = value ÷ quantity);
  - margins compare prices with `GetWeightedAverageCostsAsync`, weighted by the stock each branch holds;
  - profit comes from the sale lines' own costs.
- **`CatalogRepository.GetQuantityOnHandAsync`** (the guard on switching batch tracking) sums absolute stock across **all** branches, ignoring visibility.

## Stock transfers

`StockTransfer` (`TRF-000001`) → lines → lot rows for batch-tracked products.

1. **Create (Draft).** Nothing moves. The source defaults to the active branch and must be visible to the caller; both branches must be open and different.
2. **Dispatch.** Only staff of the source branch can dispatch. A `TransferOut` movement is recorded at the source, guarded so it can't go negative. Batch-tracked products are issued FEFO from unexpired lots, recording which lots left. `UnitCost` is the source average at that moment.
3. **Receive.** Only staff of the destination can receive. What arrived comes in as `TransferIn` at the dispatch cost. Lots keep their number, expiry and manufacture date (`ReceiveIntoBatchAsync` at the destination). A short delivery fills lots in expiry order. The shortage is recorded on the line; its value has already left the source, so it's a loss.
4. **Cancel.** Drafts only.

All of this runs in a transaction, with the transfer row locked `FOR UPDATE` and products processed in id order. Events go out as follows:

- `StockTransfer.Dispatched` reaches holders of `StockTransfers.Receive` at the destination;
- `StockTransfer.Received` reaches holders of `StockTransfers.Dispatch` at the source, as a warning when short.

## Sign-in

`AuthService.IssueSessionAsync` picks the branch as follows:

1. the requested one, if allowed (otherwise `ForbiddenException`, except on refresh, where it falls back);
2. otherwise the default `UserBranch`;
3. otherwise the first allowed branch.

"Allowed" means every open branch when the user holds `Branches.AccessAll`, otherwise their assigned open branches. If nothing is allowed, sign-in fails with `BusinessAccessDeniedException("NO_BRANCH")`.

- The token carries `branch_id`, plus one `branch` claim per assigned branch. There are none for AccessAll users, who are unrestricted anyway.
- `UserSession.BranchId` records the branch.
- `POST /auth/switch-branch` re-issues tokens for another allowed branch and records `User.SwitchedBranch`.
- Login, refresh, switch and `/auth/me` return `activeBranchId` and `branches`.
- **New members:**
  - `RegisterRequest.BranchIds` (default: the creator's active branch) are assigned on registration.
  - A business created by a super user gets its head office, with the owner assigned there as their default.
  - `AddBusinessAdminAsync` assigns the head office too.

## M-Pesa

- **C2B:** the short code is looked up as a branch till first (`Branch.MpesaShortCode`, giving that branch), then as the business short code (no branch). Short codes are unique across all businesses and branches.
- **STK:** the transaction gets the sale's branch.

## Permissions

| Permission | Purpose |
|---|---|
| `Branches.Manage` | Create, edit, close and reopen branches; assign users to branches |
| `Branches.AccessAll` | See every branch; switch to any; post business-wide expenses |
| `StockTransfers.View` / `.Create` / `.Dispatch` / `.Receive` | Transfers |

`DbSeeder` grants new business permissions to every business's Admin role at startup.

## Endpoints

- `GET/POST /branches`, `GET/PUT /branches/{id}`, `POST /branches/{id}/activate|deactivate`
- `GET/PUT /users/{id}/branches`
- `GET/POST /stock-transfers`, `GET /stock-transfers/{id}`, `POST /stock-transfers/{id}/dispatch|receive|cancel`
- `POST /auth/switch-branch`
- `?branchId=` on any GET

## Migration `Branches` (20261019022155)

The migration:

1. creates `Branches`, `UserBranches`, `StockTransfers`, `StockTransferLines` and `StockTransferLineBatches`;
2. adds `BranchId` to the branch-owned tables, plus `MaxBranches` and `LicenseMaxBranches`;
3. changes the `StockLevels` key to `(ProductId, BranchId)` and the batch indexes to include `BranchId`.

It then backfills existing data:

- inserts a **Head Office (00)** for every business;
- assigns all existing stock levels, movements, batches, sales, returns, purchase orders and production runs to it;
- gives STK transactions their sale's branch;
- leaves till (C2B) payments and existing expenses branch-less (business-wide);
- assigns every business member to their business's head office as default.

`Down` restores the old `StockLevels` key and fails if a product has stock rows at more than one branch. To go back, restore a backup instead.

## Known limits

- A purchase order is delivered to one branch. Split a delivery into several orders, or receive at one branch and transfer.
- A sale is always recorded at the active branch. There's no recording on behalf of another branch.
- A transfer shortage is written off on receipt, with no separate approval step. The loss is visible as `shortageValue` and in the received event.
- The audit log and notifications are not filtered by `?branchId=`. Notifications are routed by branch at delivery: branch-restricted users only hear about their branches.
