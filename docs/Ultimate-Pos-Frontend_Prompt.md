# Ultimate POS Frontend — Multi-Business, API-Aligned Build Prompt

> **Audience:** the coding agent building the Ultimate POS web frontend.
> **Revision 2:** Aligned with the Ultimate POS .NET 8 API after the multi-business and pharmacy release (`MultiBusinessAndPharmacy` migration, October 2026).
> Replaces the earlier "DreamsPOS Pharmacy Variant" prompt and revision 1 of this one.

---

## 0. Sources of Truth (read this first)

When these sources disagree, the higher one wins:

1. **The API source code** (`src/UltimatePos.*`): controllers, DTOs, services and enums. It defines what the frontend can do.
2. **This prompt**, which summarizes the API's business rules and turns them into screens.
3. **The DreamsPOS Pharmacy POS template** (https://dreamspos.dreamstechnologies.com/pharmacy-pos/html/index.html), which is the **visual and UX reference only**: shell, sidebar, header, cards, tables, forms, modals, POS screen layout, and density.

Rules that follow from this order:

- **Build no screen whose data the API cannot provide or accept.** §17 lists what is still missing. Reserve a navigation slot behind a feature flag for those, and nothing more.
- **Invent no fields, endpoints or statuses.** Every form field maps to a request DTO property. Every column maps to a response DTO property, or to a documented join.
- **Before you build a module, read its controller and DTO files.** The paths in this prompt are relative to `src/`.

Do not copy DreamsPOS source code, assets, logos or branding. Recreate the information architecture and UX patterns with original code, under the Ultimate POS brand.

---

## 1. What Ultimate POS Is

Ultimate POS is a **multi-business** POS and back-office system for Kenyan businesses. All businesses share one platform and one database, and the API isolates their data. Two kinds of business run on it today:

- **Manufacturers**, such as the agrovet that was the first client. They buy raw materials and packaging, produce finished goods from formulas, and sell them, mostly on account.
- **Traders**, such as a **pharmacy**, supermarket or retail shop. They buy finished goods and resell them. A pharmacy sells **batch-tracked medicines with expiry dates**, some of them **prescription-only**.

### 1.1 The two business dimensions

Every business (`BusinessDto`, from `Application/Business/Dtos/BusinessDtos.cs`) is classified on two independent dimensions:

| Field | Enum values (JSON number) | Meaning | Drives |
|---|---|---|---|
| `kind` | `0 Manufacturer`, `1 Trader` | How the business **acquires** stock | What it may purchase, and whether Production and Formulas exist |
| `businessType` | `0 Wholesaler`, `1 Retailer` | Who it **sells to** | The default price type of new customers: Wholesale or Retail |

A business profile also carries these settings:

- `isVatRegistered`: whether VAT lines appear on sales and receipts.
- `mpesaShortCode`: the till or paybill number. Incoming till payments are routed to the business by it.
- `maxDiscountPercent`: the largest discount allowed on one sale. `null` means no cap.

`kind` is **chosen at registration and can never change**: `UpdateBusinessRequestDto` has no `kind` field.

### 1.2 Item types and the purchase policy

Every product has an `itemType`: `0 RawMaterial`, `1 Packaging` or `2 FinishedGood`.

| Rule (enforced by the API) | Source |
|---|---|
| Only `FinishedGood` products can be sold | `SalesService` |
| A Manufacturer may purchase `RawMaterial` and `Packaging` | `Purchasing/PurchasePolicy.cs` |
| A Trader may purchase `FinishedGood` only | `Purchasing/PurchasePolicy.cs` |
| Only a `FinishedGood` can be a formula's output | `ProductionService` |

Never hard-code the purchase policy in the frontend. Call `GET /purchase-orders/purchasable-item-types` and filter the product picker with the result.

### 1.3 Core flows

```text
Manufacturer:  PO (raw/packaging) → Receive (bill posted to supplier) → Formula → Production run (preview → record)
               → Finished-good stock at real cost → Sale → Customer ledger → Payment

Pharmacy/Trader: PO (finished goods) → Receive BY BATCH (lot no. + expiry; bill posted) → Sale (FEFO from unexpired lots,
               prescription captured for Rx items, discounts, VAT) → Return (into original lots) → Expiry report / write-off

Both:          Supplier payments · Customer payments · Expenses · P&L (revenue net of VAT and returns) · Dashboard · Reports
```

---

## 2. Multi-Business Model (as implemented)

### 2.1 How the API isolates businesses

- **The access token is issued for one business** (claim `business_id`). Every request is scoped to it on the server. Products, stock, batches, sales, customers, suppliers, purchase orders, formulas, expenses, roles, M-Pesa transactions and reports belong to the business, and another business's records simply do not exist from inside it: they return 404.
- **Users are global; access is per business.** One person can belong to several businesses, with different roles in each. A user's roles and permissions in the token are the ones they hold **in that business**, plus any platform roles.
- **Platform permissions** (`Businesses.Register`, `Businesses.ViewAll`, `Businesses.Deactivate`) come from the `Platform Admin` role, which belongs to no business. Everything else is a business permission.
- A business id from the client is never trusted. Older parameters such as `GET /customers?businessId=` are still accepted, but they **must equal the signed-in business** or the API returns `403 FORBIDDEN`. Do not send them in new code.
- Document numbers count separately per business. Both a pharmacy and the agrovet have an `INV-000001`.

### 2.2 How the frontend must handle it

1. **Sign in, then pick a business.** `POST /auth/Login` returns `activeBusinessId` and `businesses[]` (`businessId`, `businessName`, `tradingName`, `kind`, `businessType`, `isDefault`).
   - If the user belongs to more than one business, show a **business switcher** in the header and on the post-login screen.
   - Pass `businessId` in the login request to sign straight in to a remembered business.
2. **Switch business** with `POST /auth/switch-business { businessId, refreshToken }`. It returns a new token pair for that business and revokes the old refresh token. Then:
   - Replace the tokens.
   - **Clear the whole TanStack Query cache.**
   - Reset the POS cart.
   - Reload the business profile from `GET /businesses/current`.
3. **Platform users with no business** (`activeBusinessId: null`) see only the platform area: register a business, list businesses. After registering one, switch to it.
4. **Include `activeBusinessId` in every query key** (`['products', businessId, params]`).
5. **Keep a capability map derived from `kind`**, in one config file:

```ts
// src/config/businessCapabilities.ts
export const capabilitiesByKind = {
  [BusinessKind.Manufacturer]: { production: true },
  [BusinessKind.Trader]:       { production: false },
} as const;
// Purchasable item types come from the API. Pharmacy features are driven by product flags (isBatchTracked,
// requiresPrescription) and permissions, not by business kind.
```

### 2.3 Business administration

| Screen | Endpoint | Permission |
|---|---|---|
| Businesses list (my businesses, or all of them for platform users) | `GET /businesses` | `Businesses.View` (all: `Businesses.ViewAll`) |
| Register a business (onboarding wizard) | `POST /businesses` | `Businesses.Register` (platform) |
| Current business profile and settings | `GET /businesses/current`; `PUT /businesses/{currentId}` | `Businesses.Update` (own business only) |
| Activate or deactivate a business | `POST /businesses/{id}/activate` and `/deactivate` | `Businesses.Deactivate` (platform) |

Registration body: `businessName`*, `tradingName`, `businessType`*, `kind`* (Manufacturer or Trader), `registrationNumber`, `kraPin`*, `physicalAddress`, `county`, `phoneNumber`, `email`, `logoUrl`, `backgroundImageUrl`, `isVatRegistered`, `mpesaShortCode` (5–10 digits, unique), `maxDiscountPercent` (0–100).

The new business starts with:
- an **Admin** role holding every business permission, given to the person who registered it;
- default units (`pcs`, `kg`, `g`, `l`, `ml`, `box`, `pkt`);
- default expense categories.

Errors to expect: a duplicate KRA PIN returns `409 BUSINESS_EXISTS`, and a duplicate short code returns `400 VALIDATION_FAILED` on `mpesaShortCode`. `logoUrl` is a plain URL, because there is no upload endpoint.

---

## 3. Technology Stack

- React 18 + TypeScript (strict) + Vite
- React Router (data routers, lazy routes)
- Tailwind CSS + shadcn/ui + Lucide icons
- TanStack Query + TanStack Table (one shared `DataTable`)
- React Hook Form + Zod
- Axios
- Zustand, only for client state: the POS cart, UI preferences, theme and the session
- Recharts
- Vitest + React Testing Library + Playwright; MSW for mocks

Add nothing else without a stated reason.

---

## 4. API Contract Essentials

### 4.1 Base URL, environments and CORS

All routes live under **`/api/v1`**.

```env
# .env.development
VITE_API_BASE_URL=http://localhost:5062/api/v1   # or /api/v1 behind the Vite proxy
VITE_USE_MOCKS=false
VITE_DEFAULT_UTC_OFFSET_MINUTES=180
```

- **CORS:** the API now honours `Cors:AllowedOrigins`. In Development it allows `http://localhost:5173` and `http://localhost:3000`. For staging and production, list the frontend origin in the API configuration.
- **Dev ports:** `https://localhost:7500` and `http://localhost:5062`.
- Never put secrets in `VITE_*` variables: everything with that prefix ships to the browser.

### 4.2 Response envelope

```json
{ "success": true,  "data": { }, "error": null, "meta": null }
{ "success": false, "data": null, "error": { "code": "VALIDATION_FAILED", "message": "Name is required.",
                                              "details": { "name": ["Name is required."], "code": ["Code is required."] } }, "meta": null }
```

- Property names are always camelCase.
- `success` now correctly reports failures, but still treat the HTTP status as authoritative.
- `meta` on paged lists has the shape `{ page, pageSize, totalCount, totalPages }`.
- Unwrap the envelope in **one** place in the Axios client.

### 4.3 Errors

| HTTP | `error.code` | When | UI treatment |
|---|---|---|---|
| 400 | `VALIDATION_FAILED` | Field validation, from the services or from model binding. **`details` maps camelCase field paths to messages** (for example `name`, `lines`, `prescription.patientName`) | Show each message beside its field; also show `message` in a form alert |
| 400 | `INVALID_ASSIGNMENT` | Business-rule failures that are not about a single field (still common) | Form-level alert, showing `message` verbatim |
| 401 | `INVALID_CREDENTIALS` | Bad login, or an inactive or locked user | Login form alert |
| 401 | `INVALID_TOKEN` | Refresh token missing, expired or revoked | Clear the session, go to `/login?returnTo=` |
| 401 | *(empty body)* | Access token expired | Refresh once (§4.6), then retry the request; if refresh fails, log out |
| 403 | `FORBIDDEN` | Wrong business, own-account restrictions, a missing permission checked in the service (discount, credit override, dispensing), or no business selected | Toast or alert with `message`. Do not log out |
| 403 | *(empty body)* | Missing permission policy on the endpoint | "You don't have permission to do this" |
| 404 | `NOT_FOUND` | Missing record, **or a record of another business** | Not-found state |
| 409 | `INSUFFICIENT_STOCK` | Not enough stock (for medicines: not enough in *unexpired* lots) | Highlight the affected lines |
| 409 | `CREDIT_LIMIT_EXCEEDED` | A credit sale would exceed the customer's limit | Offer "Override" if the user has `Sales.OverrideCreditLimit` |
| 409 | `USER_EXISTS`, `BUSINESS_EXISTS`, `DUPLICATE` | Duplicates. `DUPLICATE` means a concurrent create | Field error, or "Already exists — reload" |
| 422 | `PURCHASE_NOT_ALLOWED` | Purchase-policy violation | Form alert |
| 422 | `PRESCRIPTION_REQUIRED` | An Rx product was sold without prescription details | Open the prescription panel (§7.6) |
| 500 | `INTERNAL_ERROR` | Anything else | Generic error state with Retry |

### 4.4 Enums are numbers in request and response bodies

- **Send integers in request bodies.** Strings are rejected.
- **Typed-enum DTO properties come back as integers.**
- **Some DTOs return pre-formatted strings:** `ExpenseDto.paymentMethod/status`, `StockMovementDto.movementType`, `StockBatchDto.expiryStatus`, and all report DTOs. Normalise them in mappers.
- **Query strings** accept either the enum name or the number.

```ts
export enum ItemType { RawMaterial = 0, Packaging = 1, FinishedGood = 2 }
export enum TaxClassification { Standard = 0, ZeroRated = 1, Exempt = 2 }
export enum PriceType { Wholesale = 0, Retail = 1 }
export enum PurchaseOrderStatus { Draft = 0, Ordered = 1, PartiallyReceived = 2, Received = 3, Cancelled = 4 }
export enum SaleStatus { Pending = 0, Completed = 1, Voided = 2, Failed = 3 }
export enum CustomerLedgerEntryType { Invoice = 0, Payment = 1, Reversal = 2, Return = 3 }
export enum PaymentMethod { Cash = 0, Credit = 1, MpesaTill = 2, MpesaStkPush = 3 }
export enum MpesaTransactionType { C2B = 0, StkPush = 1 }
export enum MpesaTransactionStatus { Received = 0, Matched = 1, Pending = 2, Confirmed = 3, Failed = 4 }
export enum ProductionCostType { Labour = 0, Packaging = 1, Utilities = 2, Transport = 3, Overhead = 4, Other = 5 }
export enum ExpensePaymentMethod { Cash = 0, Mpesa = 1, BankTransfer = 2, Cheque = 3 }
export enum ExpenseStatus { Posted = 0, Voided = 1 }
export enum StockMovementType { PurchaseReceipt = 0, Sale = 1, Adjustment = 2, ProductionConsumption = 3, ProductionOutput = 4, SaleVoid = 5, Revaluation = 6, SaleReturn = 7 }
export enum SettlementMethod { Cash = 0, Mpesa = 1, BankTransfer = 2, Cheque = 3 }      // customer & supplier payments
export enum SupplierLedgerEntryType { Bill = 0, Payment = 1, Reversal = 2 }
export enum RefundMethod { Cash = 0, Mpesa = 1, CreditNote = 2 }
export enum BusinessType { Wholesaler = 0, Retailer = 1 }
export enum BusinessKind { Manufacturer = 0, Trader = 1 }
export enum EventSeverity { Info = 0, Warning = 1, Critical = 2 }
export type ExpiryStatus = 'Expired' | 'NearExpiry' | 'Ok' | 'NoExpiry';
export type StockStatus = 'InStock' | 'LowStock' | 'OutOfStock';
```

### 4.5 Pagination, search and sorting

| Endpoint | Paged | Server-side filters |
|---|---|---|
| `GET /products/get-products` | Yes (`pageSize` ≤ 200) | `categoryId`, `itemType`, **`search`** (matches name, SKU, barcode and generic name), `isActive`, `isBatchTracked`, `requiresPrescription`, **`sortBy`** (`name`, `sku`, `createdAt`, `category`), `sortDirection` (`asc` or `desc`) |
| `GET /sales` | Yes (≤ 100) | `customerId`, `status`, **`from`**, **`to`** (local days, inclusive), **`createdBy`** (cashier), **`paymentMethod`**, **`search`** (sale number or customer name), `utcOffsetMinutes` |
| `GET /sales/returns` | Yes | `from`, `to`, `utcOffsetMinutes` |
| `GET /purchase-orders` | Yes | `supplierId`, `status` |
| `GET /production-runs` | Yes | `productId` |
| `GET /expenses` | Yes | `from`, `to`, `categoryId`, `supplierId`, `paymentMethod`, `status`, `search`, `missingEtims` |
| `GET /customers` | No | **`search`** (name, phone, KRA PIN) |
| Suppliers, categories (tree), units, stock, batches, users, roles, formulas, expense categories | No | Few or none |

`DataTable` supports both `mode="server"` and `mode="client"`. Only the product list supports server-side sorting.

### 4.6 Authentication and session

| Endpoint | Purpose |
|---|---|
| `POST /auth/Login` (anonymous) `{ email, password, businessId? }` | Returns `{ token, expiresAt, user, refreshToken, refreshTokenExpiresAt, activeBusinessId, businesses[] }` |
| `POST /auth/refresh` (anonymous) `{ refreshToken }` | Returns a new pair (same shape). **The old refresh token stops working.** Roles and permissions are re-read, so permission changes apply here |
| `POST /auth/switch-business` `{ businessId, refreshToken }` | Returns a new pair for the chosen business |
| `POST /auth/logout` `{ refreshToken }` | Revokes this device's session |
| `GET /auth/me` | Returns `{ user, activeBusinessId, businesses[] }`, with roles and permissions read fresh |
| `POST /auth/change-password` `{ currentPassword, newPassword }` | Changes the caller's password and signs out their other devices. Rules: at least 8 characters, with a letter and a digit |

- Access tokens last **30 minutes** and refresh tokens **14 days**.
- Keep the access token in memory and the refresh token in `localStorage` (or `sessionStorage` for shared tills, chosen in settings).
- **Refresh silently** about a minute before `expiresAt`, and on any 401 without a body. **Queue concurrent requests behind a single in-flight refresh**, because refresh tokens rotate and a second refresh with the same token fails with `INVALID_TOKEN`.
- After 5 failed logins the account locks. The error is still the generic `INVALID_CREDENTIALS`, and an administrator unlocks the account (§14.4).
- There is still no forgot-password email flow. An administrator resets the password instead (`POST /users/{id}/reset-password`).

---

## 5. Permissions

Use exactly these strings. Generate a TypeScript union from `Application/Identity/PermissionCatalog.cs`.

```text
Platform:     Businesses.Register  Businesses.ViewAll  Businesses.Deactivate
Users:        Users.Create  Users.View  Users.Update  Users.AssignRoles  Users.Deactivate  Users.Lock  Users.ResetPassword
Roles:        Roles.View  Roles.Create  Roles.Update  Roles.Deactivate  Roles.AssignPermissions   Permissions.View
Business:     Businesses.View  Businesses.Update
Customers:    Customers.Register  Customers.View  Customers.Update  Customers.Deactivate  Customers.ViewLedger  Customers.RecordPayment
Catalog:      Categories.View  Categories.Create  Categories.Update  Categories.Deactivate  Categories.BulkImport
              Units.View  Units.Create  Units.Update  Units.Deactivate
              Products.View  Products.Create  Products.Update  Products.Deactivate  Products.ManagePricing
              Products.BulkImport  Products.ManageUnitConversions
Suppliers:    Suppliers.Register  Suppliers.View  Suppliers.Update  Suppliers.Deactivate  Suppliers.ViewLedger  Suppliers.RecordPayment
Purchasing:   PurchaseOrders.Create  PurchaseOrders.View  PurchaseOrders.Receive  PurchaseOrders.Cancel
Stock:        Stock.View  Stock.Adjust
Production:   Formulas.Create  Formulas.View  Formulas.Deactivate  Production.Run  Production.View
Sales:        Sales.Create  Sales.View  Sales.Void  Sales.Return  Sales.ApplyDiscount  Sales.OverrideCreditLimit
Pharmacy:     Prescriptions.Dispense  Prescriptions.View
M-Pesa:       Mpesa.Configure  Mpesa.View
Costing:      Costing.View  Costing.Revalue
Expenses:     ExpenseCategories.Create  ExpenseCategories.View  ExpenseCategories.Update  ExpenseCategories.Deactivate
              Expenses.Create  Expenses.View  Expenses.Update  Expenses.Void
Reports:      Dashboard.View  SalesReports.View  CustomerReports.View  ProductionReports.View  InventoryReports.View  PurchaseReports.View
Audit:        AuditLog.View
```

- Implement `usePermission`, `<Can permission="…">`, and a `PermissionRoute` that renders a 403 page.
- Navigation items declare `permission` and optionally `requires: 'production'`.
- **Permissions checked inside the service, not on the endpoint.** The UI must reflect these in advance:
  - Giving any discount needs `Sales.ApplyDiscount`.
  - Overriding a credit limit needs `Sales.OverrideCreditLimit`.
  - Selling a prescription-only product needs `Prescriptions.Dispense`.
- **Other non-obvious requirements:**
  - Cost fields need `Costing.View`. Without it they come back `null`, so render "—", not 0.
  - `POST /sales/preview` needs `Sales.View`, not `Sales.Create`.
  - STK reconcile needs `Sales.Create`.
- The role permission matrix never offers platform permissions: `GET /permissions` already leaves them out.
- The UI hides what a user can't do, and **the API enforces it**.

---

## 6. Shell and Navigation

Follow the DreamsPOS shell. The POS screen uses a full-width layout without the sidebar.

**Header:**
- Sidebar toggle
- **Business switcher** (name, logo, Kind/Type badges, a "VAT" badge when the business is VAT-registered)
- Global search: `Ctrl+K` for navigation commands, plus a product search through `GET /products/get-products?search=`
- **New Sale** button
- **Notification bell** (§14.8): unread badge, dropdown of the latest notifications
- Theme toggle
- User menu: profile, change password, session, sign out

**Sidebar** (config-driven, in `src/config/navigation.ts`):

```text
Dashboard                                Dashboard.View
Point of Sale
    New Sale (/pos)                      Sales.Create
    Sales                                Sales.View
    Returns                              Sales.View
    M-Pesa Payments                      Mpesa.View
Pharmacy
    Prescription Register                Prescriptions.View
    Batches & Expiry                     Stock.View
Customers
    Customers                            Customers.View
    Receivables (aging)                  CustomerReports.View
Catalog
    Products                             Products.View
    Categories                           Categories.View
    Units of Measure                     Units.View
    Bulk Import                          Products.BulkImport | Categories.BulkImport
Inventory
    Stock Levels                         Stock.View
    Stock Adjustments                    Stock.Adjust
    Stock Valuation                      Costing.View
    Revaluations                         Costing.Revalue
Purchasing
    Purchase Orders                      PurchaseOrders.View
    Suppliers                            Suppliers.View
    Payables                             Suppliers.ViewLedger
Production       [Manufacturer]
    Formulas                             Formulas.View
    Production Runs                      Production.View
Expenses
    Expenses                             Expenses.View
    Expense Categories                   ExpenseCategories.View
Reports          (§14.2)
Administration
    Business Profile                     Businesses.Update
    Businesses (platform)                Businesses.ViewAll | Businesses.Register
    Users                                Users.View
    Roles & Permissions                  Roles.View
    M-Pesa Setup                         Mpesa.Configure
    Audit Log                            AuditLog.View
Notifications    (/notifications, every signed-in user)
Settings         (client preferences, §14.7)
```

Show the **Pharmacy** group only when the business has at least one product with `isBatchTracked` or `requiresPrescription`, or when the user opts in from settings. It is not tied to business kind.

---

## 7. Point of Sale (`/pos`)

The layout follows DreamsPOS: product grid and search on the left, cart and payment on the right. It must be usable on a 10-inch tablet in landscape. **The API does all pricing, discount, VAT, stock, batch and credit logic.**

### 7.1 Finding products

- **Search** through `GET /products/get-products?itemType=FinishedGood&isActive=true&search=…&pageSize=24`, debounced at 200 ms. Category chips set `categoryId`.
- **Barcode scanners:** treat fast keyboard input that ends with Enter as a scan, and call `GET /products/lookup?code=…`. It matches the exact barcode first, then the SKU, and returns `ProductDetailDto` including inactive products, so you can explain why one won't sell. Add the product on a match; on 404 show "No product with this barcode".
- **Product cards** show the name, generic name and strength (for medicines), the price for the current price type, and the stock. Use `GET /stock`, and for batch-tracked items show `sellableQuantity` (stock in unexpired lots only). Show badges for **Rx** (`requiresPrescription`) and **Batch** (`isBatchTracked`).

### 7.2 Units, prices and price types

- Each line has a unit selector: the base unit plus any *active* conversions from `GET /products/{id}/unit-conversions`.
- Prices come from **price tiers** for each (unit, price type). Walk-in customers pay `Retail`; a registered customer pays at their `priceType`. The cashier never types a price.

### 7.3 Live totals with `POST /sales/preview`

Whenever the cart changes, send the full sale request, debounced at 300 ms, and cancel stale requests. The response contains:

- **Sale level:** `subtotal`, `discountTotal`, `taxTotal`, `totalAmount` (VAT included), `priceType`, `allStockSufficient`, `requiresPrescription`, `customerBalance`, `creditLimit`, `creditLimitExceeded`.
- **Per line:** `unitPrice`, `lineTotal`, `discountAmount`, `taxRate`, `taxAmount`, `availableInBaseUnits`, `requiredInBaseUnits`, `stockSufficient`, `requiresPrescription`, `isBatchTracked`.

Display these values exactly as returned; any client-side arithmetic is only a provisional display while the preview loads. Then:

- **Batch-tracked lines** with `stockSufficient = false` **block payment**, because the API refuses to sell medicines beyond the unexpired lots.
- **Other lines** that are short show a warning but may still be sold.
- When `requiresPrescription` is true, show the **Prescription required** banner and open the panel (§7.6).
- For credit sales with `creditLimitExceeded`, show the warning (§7.5).

### 7.4 Discounts

These controls are visible only with `Sales.ApplyDiscount`:

- **Line discount:** either an amount (`discountAmount`) or a percentage (`discountPercent`), never both.
- **Sale discount:** `discountAmount` on the request. The API spreads it across the lines in proportion to their value.
- Show the business's `maxDiscountPercent` cap. Going over it returns `VALIDATION_FAILED`.

### 7.5 Customer and credit

- The default is **Walk-in** (`customerId: null`).
- Search with `GET /customers?search=`. Quick-create with `POST /customers` using `customerName`, `phoneNumber`, `kraPin`, `priceType` and `creditLimit`.
- For a registered customer, show the balance, the credit limit and the **available credit** (`GET /customers/{id}/balance`).
- **Credit** payment requires a customer. If `creditLimitExceeded`:
  - Users with `Sales.OverrideCreditLimit` see an "Override limit" checkbox that sends `overrideCreditLimit: true`.
  - Everyone else sees the payment refused.

### 7.6 Prescriptions (pharmacy)

When any line is prescription-only, payment requires `prescription` on the sale request:

```ts
prescription: { prescriptionNumber?, prescriberName*, prescriberRegistrationNumber?, prescriberFacility?,
                patientName*, patientPhoneNumber?, patientAgeYears?, prescriptionDate* /* yyyy-MM-dd, not in the future */, notes? }
```

- Only users with `Prescriptions.Dispense` can complete such a sale. For anyone else, show "A pharmacist must dispense this" before they reach the payment step.
- Field errors come back keyed as `prescription.patientName`, `prescription.prescriptionDate` and so on.
- The API records the details as given and applies **no clinical rules**. Do not add any in the UI either.

### 7.7 Payment

Sale request body:

```ts
{ customerId, paymentMethod, notes?, lines: [{ productId, unitOfMeasureId, quantity, discountAmount?, discountPercent? }],
  amountTendered?, mpesaPhoneNumber?, mpesaTransactionId?, discountAmount?, overrideCreditLimit?, prescription? }
```

| Method | UI | API rules |
|---|---|---|
| Cash | Amount tendered, quick-cash buttons, change due | `amountTendered >= totalAmount`; `changeDue` comes back in the response |
| Credit | Customer required; show balance before and after the sale | Credit limit as in §7.5; posts an invoice to the customer ledger |
| M-Pesa Till | Pick the payment from `GET /mpesa/transactions/unmatched` (shows only this business's till payments, routed by short code), pre-filtered to the amount equal to the total | Amount must equal the total exactly |
| M-Pesa STK | Phone input, then **Send prompt** | Sale created as `Pending`; poll `GET /sales/{id}` every 3 s for up to 120 s. On timeout, call **`POST /sales/{id}/mpesa/reconcile`** ("Check with M-Pesa"), which settles the sale as Completed or Failed |

Disable **Pay** while a request is in flight. There is no idempotency key, so never retry a sale automatically.

### 7.8 Cart state and receipt

- **Cart:** a Zustand store keyed by business id, persisted to `sessionStorage`. Store `{ customerId, lines, paymentMethod, discounts, prescription draft }` and never prices. Hold and recall carts locally. Clear the cart when the business switches.
- **Receipt** (80 mm and A4 print layouts):
  - Header from `GET /businesses/current`: logo, name, KRA PIN, address.
  - Body: sale number and date, then lines with unit symbol, quantity, price, discount and VAT rate. For medicines, the **batch numbers and expiry dates** from `lines[].batches`.
  - Totals: subtotal, discount, **VAT breakdown** (only when `taxTotal > 0`), total, then tendered and change.
  - Rx sales add the patient, the prescriber and "Dispensed by".
  - Keep an eTIMS slot that renders nothing, because eTIMS is not implemented (§17).

---

## 8. Sales and Returns

- **List:** `GET /sales` with date range, status, cashier (`createdBy`; pick from `GET /users`), payment method, and search.
  - Columns: Sale number, Date, Customer (`customerName`, or "Walk-in"), Cashier (`createdByName`), Payment method, Total, Returned (`returnedAmount`), Status.
- **Detail:** lines with discount, VAT and quantity returned, plus the batches each line came from; the prescription, if any; the returns against the sale (`GET /sales/{id}/returns`); and a reprint action.
- **Void** (`Sales.Void`): allowed only for `Completed` sales that have **no returns**. Stock goes back into the original lots, and credit invoices are reversed.
- **Return** (`Sales.Return`): `POST /sales/{id}/returns`.
  - Pick the lines and a quantity for each, up to `quantity - quantityReturned`, in the unit sold.
  - Toggle **Restock** per line. Turn it off for damaged, opened or expired goods: they are refunded but not put back on the shelf.
  - Give a **reason** and a **refund method**: `Cash`, `Mpesa` (needs `refundReference`, the M-Pesa code) or `CreditNote` (needs a registered customer, and is **the only option for a credit sale**).
  - Show the refund amount. The final return of a line refunds exactly what remains of it.
  - The response is a `SaleReturnDto` (`RET-000001` and so on). Restocked goods go back into the lots they came from, latest expiry first.
- **Returns list:** `GET /sales/returns`, with a date range.

---

## 9. Customers and Receivables

- **List** with `GET /customers?search=`. **Create and edit** with these fields:
  - `customerName`* plus the contact fields;
  - **`priceType`**: Wholesale or Retail. New customers default to the business's type; on edit, `null` keeps the current value;
  - **`creditLimit`**: `null` means no limit. Edits are a full replace, so sending `null` removes the limit.
- **Detail tabs:**
  - Overview: balance, limit and available credit (`GET /customers/{id}/balance`).
  - Ledger: `GET /customers/{id}/ledger`. Entries are `Invoice`, `Payment`, `Reversal` and `Return` (credit note); each payment shows `paymentMethod` and `paymentReference`.
  - Sales: `GET /sales?customerId=`.
  - Aging.
- **Record a payment** (`Customers.RecordPayment`): `{ amount, paymentMethod, paymentReference, notes }`. A reference is required for anything but cash.
- **Receivables:** `GET /reports/customer-aging`.

---

## 10. Catalog

### 10.1 Products

- **List:** server search, filters and sort (§4.5). `ProductDto` now includes `categoryName`, `baseUnitSymbol`, `barcode`, `isBatchTracked`, `requiresPrescription`, `genericName`, `strength`, `dosageForm` and `manufacturer`.
- **Form fields:**
  - `name`*, `description`, `categoryId`*, `itemType`*, `taxClassification`*, `baseUnitOfMeasureId`*, `reorderLevel`*;
  - `barcode`: optional, unique within the business, no spaces;
  - **medicine section**: `isBatchTracked`, `requiresPrescription`, `genericName`, `strength`, `dosageForm`, `manufacturer`.
  - The SKU is generated by the server.
  - **Batch tracking can only be switched on or off while the product has zero stock.** Explain that in the form.
- **Actions:** activate and deactivate (`Products.Deactivate`). There is no delete.
- **Detail tabs:**
  - Units & conversions: list, add, edit the factor (`PUT /products/{id}/unit-conversions/{conversionId}`), activate and deactivate.
  - Pricing: price tiers. A new tier means a new effective date; the unit must be the base unit or an active conversion, and the price must be greater than 0.
  - Stock & batches.
  - Cost & margin.
  - Formulas (Manufacturer only).

### 10.2 Categories and units

- **Categories:** a tree view with create, **edit** (`PUT /categories/{id}` with `{ name, parentCategoryId }`; the code is fixed, and the API rejects moving a category under itself), and **activate/deactivate**.
- **Units:** create, **edit** (`PUT /units-of-measure/{id}` with `{ name, symbol }`), and activate/deactivate. Units belong to the business.

### 10.3 Bulk import

For categories and products: download the template, upload with `dryRun=true`, review the row errors, then commit. The product template **does not yet include barcode or medicine columns** (§17). After import, set those on each product.

---

## 11. Inventory, Batches and Costing

- **Stock levels:** `GET /stock?belowReorderOnly=`. For batch-tracked products show both `quantityOnHand` and `sellableQuantity`; the gap between them is expired stock.
- **Movements:** `GET /stock/{productId}/movements` includes `stockBatchId` and `batchNumber`. A movement with type `SaleReturn` means goods came back from a return.
- **Batches and expiry (`/inventory/batches`):** `GET /stock/batches?productId&expiringWithinDays&includeExpired&includeEmpty`.
  - Columns: Product, Batch, Expiry, Days to expiry, Status badge (`Expired`, `NearExpiry`, `Ok`, `NoExpiry`), Quantity, Unit cost (with `Costing.View`), Supplier/PO.
  - Default sort: soonest expiry first. Quick filters: expired, ≤ 30 days, ≤ 90 days.
- **Expiry report:** `GET /stock/expiry-report?withinDays=90` (`InventoryReports.View`). Shows totals of expired and near-expiry lots and their values (with `Costing.View`).
  - Each row has a **Write off** action that opens the adjustment form with the lot preselected and a negative quantity.
- **Stock adjustment** (`Stock.Adjust`): `{ productId, unitOfMeasureId, quantityChange, reason, unitCost?, stockBatchId?, batchNumber?, expiryDate?, manufactureDate? }`.
  - **Batch-tracked products, removing stock:** a `stockBatchId` is required (pick the lot).
  - **Batch-tracked products, adding stock:** either `stockBatchId` (an existing lot) or `batchNumber` plus `expiryDate` (a new lot). **This is how opening stock is entered.**
  - Other products: leave the batch fields out.
  - `unitCost` applies only when adding stock. Adding stock to a product with no cost yet requires it.
- **Valuation and revaluation:** unchanged. Stock is valued at the product's weighted-average cost; batch costs are informational.

---

## 12. Purchasing and Payables

- **Suppliers:** `GET /suppliers` (the current business). Create and edit, with `paymentTermDays` between 0 and 365.
- **Purchase orders:**
  - Create with the product picker limited to purchasable item types (§1.2).
  - The DTOs now carry `supplierName`, and `lines[].productName/sku/unitSymbol/isBatchTracked`.
  - A new order starts as `Ordered`. Cancel is allowed only before anything is received.
- **Receive** (`POST /purchase-orders/{id}/receive`): `{ lines: [{ purchaseOrderLineId, quantityReceived, batchNumber?, expiryDate?, manufactureDate? }], notes?, supplierInvoiceNumber? }`.
  - **Batch-tracked lines need a `batchNumber` and an expiry date.** One line can appear several times, once per lot, as in "40 of LOT-A expiring 2027-03 and 60 of LOT-B expiring 2027-09".
  - A lot number already on file must carry the same expiry date.
  - Receiving **posts a bill** to the supplier's account, worth quantity × the ordered unit cost.
- **Payables (`/payables`, `Suppliers.ViewLedger`):**
  - `GET /suppliers/balances` lists who the business owes, largest first.
  - Supplier detail adds Balance (`GET /suppliers/{id}/balance`) and a ledger statement (`GET /suppliers/{id}/ledger`: Bill, Payment, Reversal, with the supplier invoice number).
  - **Pay a supplier** (`Suppliers.RecordPayment`): `{ amount, paymentMethod, paymentReference, notes }`. A reference is required for anything but cash.

---

## 13. Production (Manufacturer only)

This module is unchanged from revision 1:

- **Formulas** are versioned. Editing one creates a new version.
- **Production runs** go through preview, then record. Show shortfalls, the estimated cost and price checks.
- Hide the whole module, including its routes, for Traders.

---

## 14. Expenses, Reports, Dashboard, Administration and Settings

### 14.1 Expenses

These are unchanged. Amount, date and payment method are locked after posting; a mistake is corrected by voiding and re-entering. eTIMS control numbers are tracked, and a `missingEtims` filter is available.

### 14.2 Reports

| Route | Endpoint | Notes |
|---|---|---|
| Sales | `GET /reports/sales` | Takings **including VAT**. Returns are not netted here (§17) |
| Profit & Loss | `GET /costing/profit-and-loss` | **Revenue is net of VAT and of returns**. Restocked returns reduce COGS; written-off returns don't. Show "Incomplete" when `isComplete=false` |
| Profit by product | `GET /costing/profit` | Same basis as the P&L |
| Margins | `GET /costing/margins` | — |
| Customer aging | `GET /reports/customer-aging` | — |
| Stock status, movements, adjustments | `GET /reports/inventory/*` | — |
| **Expiry** | `GET /stock/expiry-report` | Pharmacy |
| **Prescription register** | `GET /sales/prescriptions?from&to&search` | Patient, prescriber, Rx items with generic name, strength and batches dispensed, and who dispensed them (`Prescriptions.View`). Printable |
| Purchases summary, open orders, price trends | `GET /reports/purchases/*` | — |
| **Payables** | `GET /suppliers/balances` | — |
| Production costs | `GET /reports/production-costs` | Manufacturer |
| Expense summary | `GET /expenses/summary` | — |

All reports offer client-side CSV export and printing, and send `utcOffsetMinutes`.

### 14.3 Dashboard

`GET /dashboard/summary` returns the sections the user may see. Render only those. Add these widgets:

- expiring lots, from `GET /stock/batches?expiringWithinDays=30&includeExpired=true` (with `Stock.View`);
- pending STK sales;
- unmatched till payments;
- supplier payables total, from `GET /suppliers/balances` (with `Suppliers.ViewLedger`).

### 14.4 Users

Users are always the **members of the current business**.

- **List** with `GET /users`. `isActive` reflects the membership in this business.
- **Create** with `POST /auth/Register` and `{ email, password, fullName, phoneNumber, roleName }`, where the role is one of this business's roles.
- **Edit** with `PUT /users/{id}` and `{ fullName, email, phoneNumber }` (`Users.Update`).
- **Reset password** with `POST /users/{id}/reset-password` and `{ newPassword }` (`Users.ResetPassword`). This also unlocks the account and signs the user out everywhere.
- **Assign roles:** `PUT /users/{id}/roles` replaces the user's roles **in this business only**.
- **Deactivate** removes access to **this business only**; the user keeps their other businesses. You can't deactivate or lock yourself.
- **Lock and unlock** as before.

### 14.5 Roles

Roles belong to the business. Role names are unique within it.

- The permission matrix uses `GET /permissions` (platform permissions are already excluded) and saves with `PUT /roles/{id}/permissions`, which replaces the set.
- Permission changes reach users at their **next token refresh**, which happens within 30 minutes or when they switch business.

### 14.6 M-Pesa

- **Unmatched till payments** are per business, routed by the business's `mpesaShortCode`. Set the short code in Business Profile; without it, till payments won't reach the business.
- **Register C2B URLs** (`Mpesa.Configure`): unchanged.

### 14.7 Settings

Settings are client-side preferences only:

- theme
- receipt paper size
- quick-cash amounts
- STK polling timeout
- scanner suffix key
- **where to keep the refresh token**, for shared tills
- "Show pharmacy menu"

Business settings (VAT, short code, discount cap) live in **Administration → Business Profile**.

### 14.8 Notifications and the audit log

The API records every significant action as an event. Events that someone should act on become **notifications** for the
users whose permissions make them responsible, in the business where it happened. Delivery is asynchronous: a background worker
turns events into notifications within about 5 seconds, so a notification appears shortly after the action, not in its response.

**The bell (every signed-in user):**

| Endpoint | Use |
|---|---|
| `GET /notifications/unread-count` | `{ unreadCount }` for the badge. Poll every 30–60 s while the app is open, and on window focus |
| `GET /notifications?unreadOnly&page&pageSize` | The dropdown (latest 10) and the full `/notifications` page (paged) |
| `POST /notifications/{id}/read` | When the user opens one. Then navigate to its `link` |
| `POST /notifications/read-all` | "Mark all as read" |

- `NotificationDto` has `notificationId`, `eventType`, `severity` (a number: `0` Info, `1` Warning, `2` Critical), `title`, `message`, `link`, `isRead`, `readAt`, `createdAt` and `businessId`.
- Severity sets the icon and colour: Critical is red, Warning amber, Info neutral. When a new **Critical** notification arrives between polls, also show a toast.
- `link` is a frontend path such as `/inventory?productId=…`, `/sales/{id}` or `/users/{id}`. Make sure those routes exist.
- Lists only ever contain the signed-in user's notifications for the current business, plus platform notifications. Switching business changes the list, so include `businessId` in the query key.
- There is no push channel yet. Real-time delivery (SignalR) is planned, so keep the polling behind one hook (`useNotifications`) that can be swapped later.

What users receive, as of today:

| Event | Who | Channels |
|---|---|---|
| Low stock (crossing the reorder level) | `PurchaseOrders.Create` or `Stock.Adjust` | In-app |
| Out of stock | same | In-app + email |
| Batch expiring within 30 days / expired | `Stock.Adjust` | In-app / in-app + email |
| Purchase order overdue | `PurchaseOrders.Receive` or `PurchaseOrders.Create` | In-app |
| Unmatched till payment (after 30 min) | `Mpesa.View` | In-app |
| STK payment failed | the cashier who made the sale | In-app |
| Sale voided, credit limit overridden, expense voided, stock revalued | the matching manager permission, not the person who did it | In-app |
| New user, user deactivated, role permissions changed | `Users.View` / `Roles.View` | In-app; the new user gets a welcome **email** |
| Account locked (5 failed sign-ins) | `Users.Lock` + the locked user | In-app + email; the user also gets an SMS |
| Password reset by an admin / changed by the user | the user | Email (+ SMS for a reset) |

Each condition-type alert (low stock, expiry, overdue order, unmatched payment) is sent **once**, and sent again only after it has cleared and come back.

**Audit log (`/admin/audit-log`, `AuditLog.View`):**

- `GET /audit-log?page&pageSize&from&to&utcOffsetMinutes&eventType&actorUserId&entityType&entityId&minimumSeverity&search` returns `AuditLogEntryDto`: `eventType`, `severity`, `summary`, `actorName`, `subjectName`, `entityType`, `entityId`, `data` (JSON text) and `occurredAt`.
- `eventType` takes an exact type (`Sale.Voided`) or a whole area (`Sale`). `GET /audit-log/event-types` lists them for the filter dropdown.
- Show a table with columns Time, Severity, Event, Who, Summary. Each row expands to show `data` as formatted JSON.
- Add an "Activity" tab to the product, sale, user and supplier detail pages, filtered with `entityType` plus `entityId`.
- The log covers sign-ins and sign-outs, failed sign-ins, user and role changes, sales voided and returned, stock adjustments and revaluations, receipts, payments, and every alert.

---

## 15. Frontend Architecture

The folder layout is the same as revision 1, with these additions:

- **`api/client.ts`:**
  - attaches the bearer token;
  - unwraps the envelope;
  - normalises errors into `ApiError { status, code, message, fieldErrors }`, with `fieldErrors` taken from `error.details`;
  - **runs a single-flight refresh on 401**;
  - after a business switch, drops the query cache.
- **`api/dto/`** mirrors the C# records one to one. Mappers normalise enums.
- **Query keys** take the form `[resource, businessId, params]`. Mutations invalidate precisely:
  - a sale invalidates sales, stock, batches, the customer balance and ledger, the dashboard, and unmatched M-Pesa payments;
  - a return invalidates the sale, stock, batches and the customer ledger;
  - receiving an order invalidates the order, stock, batches and the supplier balance;
  - an adjustment invalidates stock and batches.
- **Forms:** map `fieldErrors` onto React Hook Form with `setError(path, …)`, including nested paths such as `prescription.patientName` and `lines`.
- **Shared components:**
  - `DataTable`, `EntityCombobox` (server search), `UnitSelect`, `MoneyText`, `QuantityText`, `StatusBadge` (statuses, plus expiry status and Rx/Batch badges);
  - `BatchPicker` (lots of a product with expiry, for adjustments and write-offs);
  - `BatchEntryRows` (repeatable lot number + expiry + quantity rows, for receiving and opening stock);
  - `PrescriptionPanel`, `DiscountControl`, `ConfirmDialog`, `FormAlert`, `EmptyState`, `ErrorState`, `PrintFrame`.

---

## 16. Testing

- **Unit:** mappers, the error normaliser (both the `details` and the single-message shapes), and the refresh queue (concurrent 401s trigger only one refresh).
- **Component:** POS with discounts, VAT and prescription; the batch receive form; the return form; the business switcher.
- **E2E**, against a dev API seeded through the API:
  - register a pharmacy business, then switch to it;
  - create a batch-tracked Rx product with a barcode and a price;
  - PO, then receive two lots;
  - sale by barcode scan, without a prescription (refused), then with one;
  - partial return, then expiry write-off;
  - pay the supplier;
  - prescription register;
  - **switch to the other business and check that none of that data is visible**.

---

## 17. Not Supported by the API Yet: do not build (feature-flag a slot only)

| Feature | Status / workaround |
|---|---|
| KRA eTIMS submission and control numbers | Not implemented; it needs VSCU device credentials. Keep an empty `<EtimsBlock />` slot on the receipt |
| Bulk-import columns for barcode, batch tracking and medicine fields | Not in the template. Set them per product after import |
| Sales report netted for returns, and excluding VAT | The sales report shows takings including VAT; use the P&L for revenue net of VAT and returns |
| Split or partial payments, card payments at the POS | Only Cash, Credit, M-Pesa Till and M-Pesa STK |
| Customer groups, brands, product images | No entities or fields |
| Branches, locations, stock transfers between businesses or sites | No API |
| Purchase returns to suppliers, supplier bill reversal UI | The ledger supports `Reversal`, but there is no endpoint to post one |
| Forgot-password email, self-service sign-up | Admins reset passwords instead |
| Notifications, server-side settings, server-side export or PDF | Use client-side CSV and print |
| Till sessions (open/close, supervisor approval) | Not implemented |
| Field-keyed errors everywhere | New and updated validations use `VALIDATION_FAILED` with `details`; many older business rules still return a single `INVALID_ASSIGNMENT` message |

---

## 18. Delivery Phases

1. **Foundation:** API client (envelope, errors, single-flight refresh), auth, **business picker and switcher**, permissions, shell, shared components.
2. **Administration:** business onboarding and profile (VAT, short code, discount cap), users (including edit and password reset), roles.
3. **Catalog:** categories, units, products (barcode, medicine fields), conversions, prices, bulk import.
4. **Purchasing and inventory:** suppliers, POs, **receive by batch**, stock, **batches and expiry**, adjustments and write-offs, valuation, payables.
5. **POS and sales:** search, scan, preview, discounts, VAT, prescription, credit limit, four payment flows including STK reconcile, receipt, sales list, void, **returns**.
6. **Customers:** price types, credit limits, ledger, payments with method, aging.
7. **Production** (Manufacturer).
8. **Expenses.**
9. **Dashboard and reports**, including the expiry report and the prescription register.
10. **Hardening:** accessibility, performance, E2E across two businesses, plus `BACKEND_INTEGRATION.md` and `BACKEND_GAPS.md` (start from §17).

## 19. Definition of Done

- Every screen in §§7 to 14 works against the real API, using only documented endpoints, fields and permissions.
- A user in two businesses can switch between them. Caches, the cart and the profile reset, and **no data from one business ever shows in the other**.
- Tokens refresh silently. Rotated refresh tokens are never reused, and concurrent 401s trigger one refresh.
- Medicines are received by lot, sold first-expiry-first-out, returned into their lots and written off by lot. Expired stock never appears as sellable.
- Prescription-only sales capture the prescription, and only dispensers can complete them.
- Discounts respect the permission and the cap. VAT shows only for VAT-registered businesses.
- Credit limits are enforced, with the override path.
- Every error shape in §4.3 renders correctly, with field errors placed beside their fields.
- The tests in §16 pass and the production build succeeds.

## 20. Working Rules for the Coding Agent

1. Read the relevant controller, DTO and service before you build each module. If the code and this prompt disagree, the code wins: record the difference in `BACKEND_GAPS.md`.
2. Inspect the frontend repository before you change anything, and preserve working code.
3. Do not modify the backend unless you are explicitly asked to.
4. Use DreamsPOS for layout and interaction patterns, never for scope.
5. Work through each module in this order: API, then DTO types and mappers, then Zod schemas, then hooks, then components, then page, then tests.
6. Ask before adding a dependency or building anything listed in §17.
