# Frontend Task: Branches, Branch Switching and Stock Transfers

> **For:** the coding agent working on the Ultimate POS frontend.
> **Depends on:** the API's `Branches` release. Backend reference: `docs/BRANCHES.md`.
> Before you start, read `src/UltimatePos.Api/Controllers/BranchesController.cs`, `src/UltimatePos.Api/Controllers/StockTransfersController.cs`, `src/UltimatePos.Application/Branches/BranchDtos.cs`, `src/UltimatePos.Application/Inventory/StockTransfers.cs` and `src/UltimatePos.Api/Middleware/BranchScopeMiddleware.cs`. If the code and this prompt disagree, the code wins.

## Goal

A business can have several shops (branches). Each user works at **one branch at a time**:

1. **Branch picker and switcher**, shown on the login result and in the header.
2. **Branch management** in Settings: create, edit, close and reopen branches, and choose who works where.
3. **Stock transfers** between branches: request → dispatch → receive.
4. **Branch filter** on every list, report and dashboard (managers only).
5. **Branch labels** on stock, batches, sales, purchase orders and expenses.

## The model in one minute

- **Branch numbers follow KRA eTIMS.** `00` is the head office, created with the business. It can't be closed. Further branches are `01`, `02` … up to `99`. Show `kraBranchId` (the two-digit string) next to every branch name, e.g. **01 · Westlands**. A number is never reused, even after a branch closes.
- **Products, prices, categories, units, customers and suppliers are shared** by every branch. Prices are the same everywhere. Don't add branch fields to those screens.
- **Stock is per branch.** Each branch has its own quantity, its own batches and its own **average cost**. A sale, adjustment, production run or purchase receipt changes the stock of the branch where it happens.
- **Document numbers** (`SAL-`, `PO-`, `EXP-`, `RET-`, `TRF-`) are one sequence for the whole business. The branch is a separate field on the document, not part of the number.
- **Who sees what:**
  - A user with **`Branches.AccessAll`** (the Admin role has it; give it to owners and managers) sees every branch and may switch to any open branch.
  - Everyone else sees **only the branches they're assigned to**. Every list the API returns is already filtered. Don't filter client-side, and don't show branch pickers that would only produce 403s.
- **Writes always happen at the active branch** (the one in the token): a sale, an adjustment, a production run. To work at another branch, the user switches branch. There's no "record this sale at branch X" option.
  - Two exceptions take an explicit branch: a **purchase order's delivery branch**, and a **stock transfer's destination**. An expense may also be **business-wide** (see below).

## Sign-in and the active branch

Login (`POST /auth/Login`), refresh, `POST /auth/switch-business` and `GET /auth/me` now also return:

```ts
activeBranchId: string | null;       // the branch the token is for; null only for super users (no business)
branches: BranchMembership[];        // where this user may work, default first
interface BranchMembership { branchId: string; branchNumber: number; kraBranchId: string; name: string; code: string | null; isDefault: boolean }
```

- `LoginRequest` takes an optional `branchId`. Omit it to land on the user's default branch.
- **Branch switcher:** put it in the header beside the business name, e.g. "Afya Pharmacy · 01 Westlands ▾".
  - It lists `branches`.
  - Choosing one calls `POST /auth/switch-branch` `{ branchId, refreshToken }`. The response has the same shape as login, with new tokens. **Replace both tokens**, then **reload all branch data**: invalidate every query cache, clear the POS cart and close open drawers.
  - Hide the switcher when `branches.length <= 1`.
- `POST /auth/switch-business` takes an optional `branchId`. Without it, the user lands on their default branch in the new business.
- **`403` with `error.code = "NO_BRANCH"`** at login or switch-business means the user isn't assigned to any open branch in that business. Show a blocking screen: "You haven't been assigned to a branch yet. Ask your administrator." It offers **Switch business** and **Sign out**, like the licence screens.
- A refresh keeps the same branch while the user may still work there. If it was closed or they were unassigned, they land on their default. When `activeBranchId` changes on a refresh, show a toast ("You're now working at 00 · Head Office") and reload data.
- Show the active branch on receipts and printed documents. Use the sale's `kraBranchId` and `branchName` when reprinting, not the current branch.

## Branch management (Settings → Branches)

Permission `Branches.Manage`. Listing is open to every signed-in member, but members only see their own branches.

| Action | Call |
|---|---|
| List | `GET branches?includeInactive=true` → `BranchDto[]` (only the caller's own branches, unless they have `Branches.AccessAll`) |
| Directory | `GET branches/directory` → `BranchDirectoryEntryDto[]`: **every open branch**, names only, for any member |
| View | `GET branches/{id}` |
| Create | `POST branches` `{ name, code?, branchNumber?, physicalAddress?, county?, phoneNumber?, email?, mpesaShortCode? }` |
| Edit | `PUT branches/{id}` (same fields, minus `branchNumber`) |
| Close / reopen | `POST branches/{id}/deactivate`, `POST branches/{id}/activate` |

```ts
interface BranchDto { branchId: string; branchNumber: number; kraBranchId: string; name: string; code: string | null;
  physicalAddress: string | null; county: string | null; phoneNumber: string | null; email: string | null;
  mpesaShortCode: string | null; isHeadOffice: boolean; isActive: boolean; createdAt: string }
```

```ts
interface BranchDirectoryEntryDto { branchId: string; branchNumber: number; kraBranchId: string; name: string; code: string | null;
  isHeadOffice: boolean; isMine: boolean }   // isMine: the caller works there
```

- **Which list to use.** `GET branches` is for managing branches and for pickers of places *you* work (transfer **From**, the branch filter). `GET branches/directory` is for naming or choosing **any** open branch: the transfer **To** list, and labels on another branch's documents.
- **Table columns:** No. (`kraBranchId`), Name, Code, County, Phone, Till, Status. Pin the head office first and label it "Head office".
- **Create form:**
  - Leave **branch number** empty by default, with the hint "Next free number". Show an "Advanced: choose number (1–99)" field for businesses that already have KRA branch numbers.
  - **Code** is optional: up to 10 letters or digits, stored uppercase (e.g. `NBO`).
  - **M-Pesa till / short code** is optional. A branch with its own till gets its customers' payments routed to it (see M-Pesa below).
  - Map `400` validation errors to fields: `branchNumber`, `code`, `mpesaShortCode`, `name`.
- **`403` `BRANCH_LIMIT_REACHED`** on create or reopen: the licence allows no more open branches. Show `error.message` inline with a "Contact Ultimate POS to upgrade" line. It's not a blocking screen.
- **Close:**
  - Confirm first. Show the API's message in the dialog when it refuses (`400` `INVALID_ASSIGNMENT`): the head office can't be closed, and a branch still holding stock can't be closed ("Transfer it to another branch or adjust it to zero first").
  - Link to **New transfer** from that message.
- `GET businesses/current/license` now includes `maxBranches` (null = unlimited) and `openBranches` (every open branch of the business, whoever asks). Show "Branches: 2 of 3 open" on the licence page.

## Who works where (Users → user → Branches tab)

| Action | Call | Permission |
|---|---|---|
| View | `GET users/{id}/branches` → `{ userId, accessAll, branches: BranchDto[], defaultBranchId }` | `Users.View` |
| Set | `PUT users/{id}/branches` `{ branchIds: string[], defaultBranchId? }` (full replace) | `Branches.Manage` |

- Show a checkbox list of open branches, plus a "Default" radio among the checked ones.
- When `accessAll` is true, show the note "Has access to every branch through their role". Assignments still decide their default branch.
- Changes apply at the user's **next sign-in or token refresh**. Say so in the success toast.
- **Add user form** (`POST auth/register`): add a **Branches** multi-select (`branchIds`) and a **Default branch** (`defaultBranchId`). Pre-fill with the admin's active branch. Left empty, the API assigns the creator's current branch.

## Stock transfers (Inventory → Transfers)

Permissions: `StockTransfers.View`, `StockTransfers.Create`, `StockTransfers.Dispatch`, `StockTransfers.Receive`.

```ts
export enum StockTransferStatus { Draft = 0, Dispatched = 1, Received = 2, Cancelled = 3 }   // numbers in JSON
```

| Action | Call |
|---|---|
| List | `GET stock-transfers?page&pageSize&status&branchId` → `data: StockTransferDto[]`, paging in `meta` `{ page, pageSize, totalCount, totalPages }` like every other list (from **or** to the branch) |
| View | `GET stock-transfers/{id}` |
| Create | `POST stock-transfers` `{ fromBranchId?, toBranchId, notes?, lines: [{ productId, unitOfMeasureId, quantity }] }` |
| Dispatch | `POST stock-transfers/{id}/dispatch` |
| Receive | `POST stock-transfers/{id}/receive` `{ lines?: [{ stockTransferLineId, quantityReceived }], notes? }` |
| Cancel | `POST stock-transfers/{id}/cancel` (drafts only) |

- **Flow:**
  - **Draft:** nothing moves.
  - **Dispatched:** stock has left the source branch at its average cost and is **in transit**, counted at neither branch.
  - **Received:** stock arrives at the destination at that same cost.
  - Show this as a three-step status bar on the detail page.
- **Create screen:**
  - **From** defaults to the active branch. Users who see several branches may pick another source they can see.
  - **To** lists every other open branch, from `GET branches/directory`. Restricted staff only get their own branches from `GET branches`, so don't use that here.
  - Lines use the same product and unit picker as purchase orders. Next to each line, show the source branch's available stock: `GET stock?branchId={from}`.
- **Who can act:**
  - **Dispatch** only appears for users who can see the **source** branch.
  - **Receive** only appears for users who can see the **destination**. Otherwise the API returns 403; hide the buttons instead.
  - A dispatched transfer can't be cancelled; it must be received, even with zero quantities.
- **Batch-tracked products:**
  - Dispatch picks lots first-expiry-first-out from unexpired stock.
  - The detail page shows each line's `batches` (lot number, expiry, quantity). The same lots, with the same expiry, arrive at the destination.
  - Insufficient unexpired stock at dispatch returns `409` `INSUFFICIENT_STOCK`. Show the message and keep the draft.
- **Receive screen:**
  - Pre-fill every line with the full quantity. The receiver edits only short deliveries; quantities are in the line's own unit.
  - The response shows `shortageInBaseUnits` per line and `shortageValue` for the transfer. Show shortages in red, plus an optional note.
- **Inbox badge:**
  - On the Transfers menu item, show a count of `status=Dispatched` transfers to the active branch: `GET stock-transfers?status=1&branchId={activeBranchId}`, keeping those with `toBranchId === activeBranchId`.
  - The notifications feed also carries `StockTransfer.Dispatched` (to receivers) and `StockTransfer.Received` (to the senders, with any shortage).
- Show `dispatchedValue` and `shortageValue` only to users with `Costing.View`.

## The branch filter on reads

**Every GET accepts `?branchId=`.** The server narrows that request to one branch. That covers lists, single-product stock reads, reports, the dashboard and valuation. The audit log is not narrowed by branch. Without it, users with `Branches.AccessAll` get **all branches combined**; everyone else gets their own branches.

- Add a **Branch** dropdown ("All branches" plus each branch) to the dashboard, every report, and the stock, batches/expiry, sales, purchase-order, expense and production lists.
  - Show it only when the user can see more than one branch.
  - Keep the choice in the URL query (`?branchId=`) so links and refreshes keep it.
- A branch the user can't see returns `403` `BRANCH_FORBIDDEN`. Reset the filter to "All branches".
- **What "all branches" means:**
  - Stock valuation and the stock status report show **one row per product**, with quantities added up and the cost averaged by quantity.
  - The stock list (`GET stock`) shows **one row per product and branch**. Group it by product with expandable branch rows, or add a Branch column, when not filtered.
  - Sales, profit and expense totals add up across the branches in view.
- Single-product reads such as `GET stock/{productId}` are **at the active branch**, or at the `branchId` filter when given.

## Branch fields on existing DTOs

All of these fields are optional:

| DTO | New fields |
|---|---|
| `StockLevelDto` | `branchId`, `kraBranchId`, `branchName` |
| `StockBatchDto`, `ExpiryReportItemDto` | `branchId`, `kraBranchId`, `branchName` |
| `StockMovementDto` | `branchId`. New movement types `TransferOut` and `TransferIn` (strings), shown as "Transfer to …" / "Transfer from …" using `notes` |
| `SaleDto` | `branchId`, `kraBranchId`, `branchName`, shown in the list and on receipts |
| `PurchaseOrderDto` | `branchId`, `kraBranchId`, `branchName` (the delivery branch) |
| `ExpenseDto` | `branchId` (null = business-wide) |
| `MpesaTransactionDto` | `branchId`, `kraBranchId`, `branchName` (null = paid to the business short code) |
| `BusinessLicenseStatusDto` | `maxBranches`, `openBranches` |
| `BusinessDto` | `licenseMaxBranches` |
| `BusinessLicenseDto` | `maxBranches` |
| Platform `BusinessSummaryDto`, `BusinessPerformanceDto` | `activeBranches` |

Form changes:

- **New purchase order:** add **Deliver to** (branch select, defaulting to the active branch; `branchId` on the request). Goods can only be received at that branch's stock. Show "Deliver to 01 · Westlands" on the PO.
- **New expense:**
  - Add **Branch**, defaulting to the active branch (`branchId`).
  - Users with `Branches.AccessAll` also see a **Business-wide** checkbox (`businessWide: true`, branch left empty) for costs like head-office rent.
  - Business-wide expenses are visible only to users who see every branch.
- **Super User area:**
  - The licence form gains **Max branches** (optional, 1–100; empty = unlimited).
  - The businesses table and dashboard gain a **Branches** column (`activeBranches`).

## M-Pesa

- A payment to a **branch's own till** (`mpesaShortCode` on the branch) belongs to that branch. Only its staff, and managers, see it in **Unmatched till payments**.
- A payment to the **business's short code** has no branch, and any branch can match it to a sale.
- STK pushes belong to the branch of the sale.
- `MpesaTransactionDto` (e.g. `GET mpesa/transactions/unmatched`) carries `branchId`, `kraBranchId` and `branchName`, all null for business short-code payments. Show a **Branch** column ("Any branch" when null). The `?branchId=` filter works on this list too.

## Error codes added

| HTTP | `error.code` | Where | Show |
|---|---|---|---|
| 403 | `NO_BRANCH` | login, switch business | blocking screen (see above) |
| 403 | `BRANCH_LIMIT_REACHED` | create or reopen branch | inline on the form |
| 403 | `BRANCH_FORBIDDEN` | any GET with `?branchId=` | reset the branch filter |
| 403 | `FORBIDDEN` | switch branch, dispatch or receive at a branch the user doesn't work at | toast; hide such actions up front |

## Acceptance checklist

- [ ] Login lands on the default branch. The header shows "Business · NN Branch". Switching branch swaps tokens and reloads all data, POS cart included.
- [ ] `NO_BRANCH` shows the blocking screen.
- [ ] Settings → Branches: create (auto-numbered and chosen number), edit, close (with both refusals surfaced), reopen, and the licence limit message.
- [ ] Users → Branches tab, and branch fields on Add user.
- [ ] Transfers: create, dispatch, receive with a short line, cancel a draft, batch lots shown, inbox badge, buttons hidden for the wrong branch.
- [ ] Branch filter on the dashboard, reports and lists, persisted in the URL, hidden for single-branch users.
- [ ] Branch labels on stock, batches, sales (and receipts), POs, expenses and movements, including transfer movements.
- [ ] PO "Deliver to" and expense branch or business-wide.
- [ ] Super User: max branches on licences, branch counts on businesses.
