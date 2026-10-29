# Frontend Task: KRA eTIMS

> **For:** the coding agent working on the Ultimate POS frontend.
> **Depends on:** the API's eTIMS release (migrations `Etims` and `BusinessEtimsEnabled`).
> Before you start, read these files in the API repo:
> - `src/UltimatePos.Api/Controllers/EtimsController.cs`
> - `src/UltimatePos.Application/Etims/EtimsDtos.cs`
> - `src/UltimatePos.Domain/Entities/Etims.cs` (the two enums)
> - the `ChangeEtims` action in `src/UltimatePos.Api/Controllers/PlatformController.cs`
> - `src/UltimatePos.Api/Controllers/PlatformEtimsController.cs` and `src/UltimatePos.Application/Etims/PlatformEtimsDtos.cs`
> - `docs/ETIMS.md`, which explains how reporting works
>
> If the code and this prompt disagree, the code wins.

## Goal

KRA eTIMS is Kenya's electronic tax invoicing. The API now reports a business's trading to KRA by itself: products, sales, voids,
returns and every stock movement, through one eTIMS "device" per branch. Nobody presses "send".

**Not every business needs eTIMS.** A super user chooses it per business, when registering the business and later on the platform.
A business without it must see **nothing** of eTIMS: no menu, no product tab, no receipt block, no errors.

For a business with it, staff must be able to:

1. Register each branch's device with KRA once, and download KRA's code lists.
2. Give products (or whole categories) their KRA item classification before they are first sold.
3. See what was sent to KRA, read why KRA refused something, and send it again.
4. Print receipts that carry KRA's signature and QR code. This is what makes a receipt valid for KRA.

**The platform is the integrator.** Every business reports to KRA through the platform's own KRA credentials, so super users must
also be able to do steps 1 and 3 for any business (onboarding it), and see what KRA refused **across all businesses** in one place.

## The switch: show eTIMS only to businesses that have it

The API decides; the frontend only reads it.

- `BusinessDto.etimsEnabled: boolean` (on `GET /businesses/current` and on the platform's business DTOs).
- `modules` (on sign-in memberships and on `GET /businesses/current`) contains `"etims"` only when it is on. **Gate everything below on
  the `etims` module**, exactly like `production` and `bundles`:
  - add `'etims'` to `ModuleKey` and `moduleLabels` (label: `"KRA eTIMS"`) in `src/config/businessCapabilities.ts`;
  - put `module: 'etims'` on the nav items;
  - use `<IfModule module="etims">` for blocks inside existing pages (product tab, category field, receipt block);
  - use `PermissionRoute module="etims"` for the new pages.
- In `moduleEnabled`, an API without a `modules` list must return **false** for `etims`, the same as for `bundles` and `cashUp`.
  Older APIs have no eTIMS.
- The switch can change while someone is signed in (a super user turns it on). `useBusinessFreshness` already refreshes the business,
  so menus follow it. Don't cache the module list anywhere else.

While eTIMS is off, the API refuses eTIMS writes with `400 INVALID_ASSIGNMENT` ("KRA eTIMS is not switched on for this business ...").
With the gating above, users never reach those calls.

## Permissions (new)

Add both to `BUSINESS_PERMISSIONS` in `src/api/permissions.ts`. Their module is `Etims`, so they group under it on the Roles screen.

| Permission | Allows |
|---|---|
| `Etims.View` | Devices, submissions, KRA's code lists, products' eTIMS details |
| `Etims.Manage` | Register and edit devices, download KRA's lists, "send now", set classifications, retry submissions |

Reading a sale's KRA signature for its receipt needs only `Sales.View`, so cashiers can print receipts.

Two **platform** permissions (module `Platform`, held by the Super User role) go with the other platform permissions:

| Permission | Allows |
|---|---|
| `Platform.Etims.View` | The platform's eTIMS overview, any business's devices, submissions across businesses |
| `Platform.Etims.Manage` | Register and edit any business's devices, download KRA's lists, "send now", retry submissions |

## API

All eTIMS routes are under `/api/v1/etims`. They use the usual `ApiResponse<T>` envelope and camelCase JSON, and enums are numbers.
Add the types to a new `src/api/dto/etims.ts`, the calls to a new `src/api/endpoints/etims.ts`, and the enums to `src/api/enums.ts`:

```ts
export enum EtimsDocumentType { Item = 0, Sale = 1, CreditNote = 2, Purchase = 3, StockIO = 4, StockMaster = 5 }
/** Pending: not sent yet (or waiting for an item). Failed: will be retried. Sent: KRA accepted it. Rejected: gave up; needs a person. */
export enum EtimsSubmissionStatus { Pending = 0, Failed = 1, Sent = 2, Rejected = 3 }
```

### Business registration and the switch (super users)

| Call | Body | Returns |
|---|---|---|
| `POST /platform/businesses` (existing) | new optional field `etimsEnabled: boolean` (default `false`) | as today; `business.etimsEnabled` |
| `PUT /platform/businesses/{id}/etims` | `{ "enabled": true }` | `BusinessSummaryDto`. Needs `Businesses.Register`; `400 INVALID_ASSIGNMENT` if it is already in that state |

Add `etimsEnabled?: boolean` to `RegisterBusinessRequestDto` and `BusinessDto` in `src/api/dto/business.ts` (optional: older APIs
don't send it). Add `changeEtims: (id, enabled) => api.put('/platform/businesses/${id}/etims', { enabled })` next to `changeSector` in
`src/api/endpoints/platform.ts`.

### The platform's eTIMS (super users)

All under `/api/v1/platform/etims`. These call the same rules as the business's own routes, inside the business named, and the
changes appear in that business's audit log (`Platform.SupportEtimsDeviceRegistered`, `...DeviceUpdated`, `...SubmissionRetried`).

| Call | Permission | Returns |
|---|---|---|
| `GET /platform/etims` | `Platform.Etims.View` | `PlatformEtimsOverviewDto`: totals, and one row per business with eTIMS on (or with devices) |
| `GET /platform/etims/submissions?page&pageSize&businessId&deviceId&status&documentType` | `Platform.Etims.View` | Paged `PlatformEtimsSubmissionDto[]`, most recently attempted first. **No `status` = Failed and Rejected** (what needs attention) |
| `GET /platform/etims/submissions/{id}` | `Platform.Etims.View` | `EtimsSubmissionDetailDto` (`{ submission, request, response }`) |
| `POST /platform/etims/submissions/{id}/retry` | `Platform.Etims.Manage` | `EtimsSubmissionDto` |
| `GET /platform/etims/businesses/{businessId}` | `Platform.Etims.View` | `PlatformEtimsBusinessDto`: every branch with its device, or `device: null` |
| `POST /platform/etims/businesses/{businessId}/devices` | `Platform.Etims.Manage` | `EtimsDeviceDto`; body `RegisterEtimsDeviceRequestDto`; calls KRA (~30 s) |
| `PUT /platform/etims/businesses/{businessId}/devices/{deviceId}` | `Platform.Etims.Manage` | `EtimsDeviceDto`; body `UpdateEtimsDeviceRequestDto` |
| `POST /platform/etims/businesses/{businessId}/devices/{deviceId}/sync-codes` | `Platform.Etims.Manage` | as the business's `sync-codes` |
| `GET /platform/etims/businesses/{businessId}/devices/{deviceId}/item-classes?search=&take=50` | `Platform.Etims.View` | as the business's `item-classes` |
| `POST /platform/etims/businesses/{businessId}/devices/{deviceId}/run` | `Platform.Etims.Manage` | `{ queued, sent, failed, waiting }` |

Errors are the same as on the business routes; in particular `400 INVALID_ASSIGNMENT` "KRA eTIMS is not switched on for this
business" when the business's switch is off.

```ts
interface PlatformEtimsBusinessRowDto {
  businessId: Guid; businessName: string; kraPin: string; etimsEnabled: boolean; businessActive: boolean; businessSuspended: boolean;
  deviceCount: number; activeDeviceCount: number; pending: number; failed: number; rejected: number; sent: number;
  lastSentAt: DateTimeString | null;       // when KRA last accepted something from it
  oldestProblemAt: DateTimeString | null;  // oldest Failed/Rejected submission; null = nothing needs attention
}
interface PlatformEtimsOverviewDto {
  businessCount: number; deviceCount: number; activeDeviceCount: number; pending: number; failed: number; rejected: number;
  businesses: PlatformEtimsBusinessRowDto[];
}
interface PlatformEtimsBranchDto { branchId: Guid; name: string; kraBranchId: string; isActive: boolean; device: EtimsDeviceDto | null }
interface PlatformEtimsBusinessDto {
  businessId: Guid; businessName: string; kraPin: string; etimsEnabled: boolean; etimsEnabledAt: DateTimeString | null;
  branches: PlatformEtimsBranchDto[];
}
interface PlatformEtimsSubmissionDto {
  businessId: Guid; businessName: string; branchName: string; tin: string; bhfId: string;
  submission: EtimsSubmissionDto;
}
```

Put the calls in `src/api/endpoints/platform.ts` (or a `platformEtims.ts` next to it) and the types in `src/api/dto/etims.ts`.

### Devices (one per branch)

| Call | Permission | Notes |
|---|---|---|
| `GET /etims/devices` | `Etims.View` | `EtimsDeviceDto[]` |
| `GET /etims/devices/{id}` | `Etims.View` | |
| `POST /etims/devices` | `Etims.Manage` | `RegisterEtimsDeviceRequestDto`; calls KRA, so allow ~30 s |
| `PUT /etims/devices/{id}` | `Etims.Manage` | `UpdateEtimsDeviceRequestDto` (full replace) |
| `POST /etims/devices/{id}/run` | `Etims.Manage` | "Send now": `{ queued, sent, failed, waiting }` |
| `POST /etims/devices/{id}/sync-codes` | `Etims.Manage` | Downloads KRA's lists: `[{ kind, resultCode, resultMessage, count }]` |
| `GET /etims/devices/{id}/codes?codeClass=10` | `Etims.View` | `[{ codeClass, codeClassName, code, name, useYn }]`. `404 NOT_FOUND` until the lists are downloaded |
| `GET /etims/devices/{id}/item-classes?search=&take=50` | `Etims.View` | `[{ itemClassCode, itemClassName, level, taxTypeCode, useYn }]` |

```ts
interface RegisterEtimsDeviceRequestDto {
  branchId: Guid;
  deviceSerialNumber: string;     // the serial the PIN's device was registered with on KRA's eTIMS portal, e.g. "UPOSP052446551J001"
  defaultItemClassCode: string;   // required: classification for products with none of their own
  tin?: string | null;            // defaults to the business's KRA PIN
  cmcKey?: string | null;         // only for a device KRA already initialised (KRA answers "902 This device is installed")
  lastInvoiceNumber?: number;     // optional; the API follows KRA's expected numbers anyway
  lastStockAdjustmentNumber?: number;
  lastItemSequence?: number;
  receiptTopMessage?: string | null;    // KRA prints at most 20 characters
  receiptBottomMessage?: string | null; // KRA prints at most 20 characters
}
interface UpdateEtimsDeviceRequestDto { defaultItemClassCode: string; isActive: boolean; receiptTopMessage: string | null; receiptBottomMessage: string | null }
interface EtimsDeviceDto {
  etimsDeviceId: Guid; branchId: Guid; branchName: string; tin: string; bhfId: string; deviceSerialNumber: string;
  cmcKeyHint: string;             // masked, e.g. "233...851"; the key itself is never sent to the browser
  sdcId: string | null; mrcNo: string | null; taxpayerName: string | null; kraBranchName: string | null;
  defaultItemClassCode: string | null; receiptTopMessage: string | null; receiptBottomMessage: string | null;
  isActive: boolean; activatedAt: DateTimeString; sweptUntil: DateTimeString;
  lastInvoiceNumber: number; lastStockAdjustmentNumber: number; lastItemSequence: number;
  openingStockQueuedAt: DateTimeString | null; lastCodeSyncAt: DateTimeString | null;
  submissionCounts: Record<'Pending' | 'Failed' | 'Sent' | 'Rejected', number>; // keys are status NAMES
}
```

### Products and categories

| Call | Permission | Notes |
|---|---|---|
| `GET /etims/products/{productId}` | `Etims.View` | `EtimsProductProfileDto` (works before the product was ever reported) |
| `PUT /etims/products/{productId}` | `Etims.Manage` | `{ itemClassCode, packagingUnitCode, quantityUnitCode, originCountryCode }`; nulls keep the current value. `400 INVALID_ASSIGNMENT` once KRA holds the item; `400 VALIDATION_FAILED` for a unit that is not two letters |
| `PUT /etims/categories/{categoryId}` | `Etims.Manage` | `{ itemClassCode }` → 204 |

```ts
interface EtimsProductProfileDto {
  productId: Guid;
  itemCode: string | null;          // KRA's code, e.g. "KE2NTBX0000014"; null until KRA accepts the item
  itemClassCode: string | null;     // the product's own classification
  effectiveItemClassCode: string | null; // own, else the category's (the device default applies when both are null)
  packagingUnitCode: string;        // "NT"
  quantityUnitCode: string;         // two letters: KG, BX, NO ...
  originCountryCode: string;        // "KE"
  isRegistered: boolean;            // KRA holds it: nothing can change any more
}
```

### What was sent

| Call | Permission | Notes |
|---|---|---|
| `GET /etims/submissions?page&pageSize&deviceId&status&documentType&sourceId` | `Etims.View` | Paged `EtimsSubmissionDto[]`, newest first, with `meta` |
| `GET /etims/submissions/{id}` | `Etims.View` | `{ submission, request, response }`: the exact JSON sent and KRA's reply |
| `POST /etims/submissions/{id}/retry` | `Etims.Manage` | Rejected or Failed → Pending, sent on the next pass |

```ts
interface EtimsSubmissionDto {
  etimsSubmissionId: Guid; etimsDeviceId: Guid; branchId: Guid; documentType: EtimsDocumentType;
  sourceType: string;              // "Product", "Sale", "SaleVoid", "SaleReturn", "OpeningStock", or a stock movement type ("Sale", "Adjustment" ...)
  sourceId: Guid;                  // the product, sale or return it reports (link to it where you can)
  documentNumber: number | null;   // KRA invoice number (sales, credit notes) or stock number; null until KRA accepts it
  endpoint: string; status: EtimsSubmissionStatus; attempts: number;
  nextAttemptAt: DateTimeString; lastAttemptAt: DateTimeString | null; sentAt: DateTimeString | null;
  resultCode: string | null;       // KRA's code, e.g. "000", "910", "924"
  resultMessage: string | null;    // KRA's words, or "Waiting for 1 item(s) to be registered with KRA."
  receiptNumber: number | null; createdAt: DateTimeString;
}
```

### The receipt block

`GET /etims/sales/{saleId}/receipt` (`Sales.View`):

```ts
interface EtimsReceiptDto {
  saleId: Guid; status: EtimsSubmissionStatus;
  invoiceNumber: number | null;      // KRA's invoice number (not the POS sale number)
  receiptNumber: number | null; totalReceiptNumber: number | null;
  internalData: string | null; receiptSignature: string | null;
  sdcId: string | null; mrcNo: string | null; sdcDateTime: string | null;
  verificationUrl: string | null;    // the QR code's content
  resultMessage: string | null;      // why it is not signed yet, when it is not
}
```

It returns `404 NOT_FOUND` when the sale will never be reported: its branch has no active device, or the sale predates it. Treat that
as "no KRA block", not as an error.

### Reads passed through from KRA (optional, for a "KRA" tab on the device page)

`GET /etims/devices/{id}/kra/{what}?argument=`, where `what` is one of: `taxpayer`, `branches`, `notices`, `items`, `purchases`,
`imports`, `sales`, `stock-moves`, `customer-pin` (argument = PIN), `invoice` (argument = KRA invoice number). It returns
`{ endpoint, resultCode, resultMessage, resultDate, data, error }`, where `data` is KRA's raw JSON. Show it as formatted JSON. `001` means
"nothing found", not an error.

## Screens

### 1. Platform → Register business (`src/features/platform/RegisterBusinessWizard.tsx`, `BusinessFields` with `registering`)

- Add a switch **"Reports to KRA eTIMS"** under "VAT registered". Hint: *"Turn on for businesses that must issue KRA eTIMS invoices.
  Their branches then report sales and stock to KRA, and staff see eTIMS menus. Can be changed later."*
- Default it **on when "VAT registered" is ticked**, off otherwise. Keep it editable either way.
- Send it as `etimsEnabled` in `businessesApi.register(...)`.
- Show it on the review step ("KRA eTIMS: On / Off") and on the "registered" panel.
- The field is **registration-only** inside `BusinessFields`. The business's own profile page (`/admin/business`) shows it read-only:
  *"KRA eTIMS: On — set by the platform"*. Never send it on `PUT /businesses/{id}`; the API ignores it there.

### 2. Platform → Business detail (`src/app/platform/businesses/[id]/page.tsx`)

- In the details list, next to "Sector": **"KRA eTIMS"** with an On/Off pill.
- A button beside "Change sector", behind `Businesses.Register`: **"Turn eTIMS on"** or **"Turn eTIMS off"**. It opens a confirm
  dialog in the style of `ChangeSectorDialog`:
  - **On:** *"{Business} will start reporting to KRA from now. Register each branch's device next, here on the platform or by its
    admin under Administration → KRA eTIMS."*
  - **Off:** *"{Business} will stop reporting to KRA now. Sales made while it is off are never reported. Devices and history are kept."*
  - Calls `platformApi.changeEtims(id, enabled)`, then refreshes the business.
- The businesses list may show a small "eTIMS" tag on businesses that have it.
- When eTIMS is on, a **"Set up eTIMS devices"** link (`Platform.Etims.View`) goes to `/platform/etims/{businessId}` (screen 2a).

### 2a. Platform → KRA eTIMS (new pages, `Platform.Etims.View`)

Nav item in the platform area, after "Licences": `{ label: 'KRA eTIMS', href: '/platform/etims', permission: 'Platform.Etims.View' }`.
These pages are not gated on the `etims` module (that is a business's module; super users have no business).

**`/platform/etims` — overview.**

- Stat tiles from the totals: businesses, devices (active / all), Pending, Failed, **Rejected** (red when > 0).
- A table, one row per business: name, KRA PIN, eTIMS On/Off pill (and "Suspended"/"Closed" when so), devices (active / all),
  Pending, Failed, Rejected, "Last accepted by KRA" (`lastSentAt`, relative), "Oldest problem" (`oldestProblemAt`, relative, amber
  over an hour, red over a day). Default sort: businesses with problems first, oldest problem first.
- Flag a business with eTIMS on but `activeDeviceCount === 0`: *"Not reporting: no device registered."*
- Row click → `/platform/etims/{businessId}`. The Failed/Rejected counts link to the submissions tab filtered to that business.
- A **"Needs attention"** tab: the cross-business table from `GET /platform/etims/submissions` (no status = Failed and Rejected).
  Columns: business, branch (`branchName`, `bhfId`), created, document type, KRA's code and message, attempts, last attempt.
  Filters: business, status (with "Needs attention" as the default and "All statuses" as an option), document type. The same detail
  drawer as screen 4 (`GET /platform/etims/submissions/{id}`), with **Retry** (`Platform.Etims.Manage`).
- Don't link the `sourceId` to sale or product pages here: those are inside the business, which a super user can't open.

**`/platform/etims/{businessId}` — onboarding one business.**

- Header: business name, KRA PIN, eTIMS On/Off (`etimsEnabledAt`). When it is off, show *"eTIMS is off for this business"* with the
  "Turn eTIMS on" action from screen 2, and disable the device actions.
- One card per branch from `branches`, exactly like screen 3's cards (reuse the component: give it the endpoints as props, or a
  `scope: { kind: 'business' } | { kind: 'platform', businessId }`):
  - `device: null` → **Register device** (`Platform.Etims.Manage`), the same dialog as screen 3, posting to
    `POST /platform/etims/businesses/{businessId}/devices`. `tin` defaults to the business's KRA PIN; show it pre-filled.
  - With a device → the same header, counts, **Download KRA lists**, **Send now**, **Edit**, all through the platform routes.
  - The classification picker uses `.../devices/{deviceId}/item-classes`.
- A suggested order as a short checklist at the top: *1. Turn eTIMS on. 2. Register the head office (00) device. 3. Download KRA's
  lists. 4. Register the other branches. 5. Ask the business to check product classifications before its first sale.*
- Products and categories stay with the business; there is no platform screen for them.

### 3. Administration → KRA eTIMS (new page `/admin/etims`, `PermissionRoute permission="Etims.View" module="etims"`)

Add the nav item under Administration, after "M-Pesa Setup":
`{ label: 'KRA eTIMS', href: '/admin/etims', permission: 'Etims.View', module: 'etims' }`.

The page has one card per branch. List the branches (existing branches API) and match each to its device by `branchId`.

- **Branch without a device:** "Not reporting to KRA" and a **Register device** button (`Etims.Manage`). It opens a dialog with:
  - Branch: fixed.
  - Device serial number: required, with the hint *"As registered on KRA's eTIMS portal for this branch."*
  - Default item classification: required. Pick it from KRA's list (see below). Before any device has downloaded the lists, accept
    the code as typed, with the hint *"e.g. 1010160100. You can change it after downloading KRA's lists."*
  - Receipt top and bottom messages: max 20 characters each, with a counter.
  - A collapsed "Device already initialised with KRA?" section with the existing key (`cmcKey`). Explain: *"KRA initialises a device
    once. If it was set up before (for example while testing), paste the key KRA issued then."*

  While the call runs (KRA can be slow), show "Contacting KRA…". If KRA refuses, show its message from the error; the most likely one
  is "This device is installed", which is the cue to fill in the key.
- **Branch with a device:**
  - Header: branch name and `bhfId`, an Active/Inactive pill, `taxpayerName`, `sdcId`, `mrcNo`, `deviceSerialNumber`, the masked key.
  - Counts from `submissionCounts`: Sent, Pending, Failed, **Rejected** (red when > 0, linking to the Submissions page filtered to
    this device and status).
  - "Last invoice no.", "Last stock no.", "KRA's lists downloaded {lastCodeSyncAt}" or a warning *"KRA's lists not downloaded yet"*.
  - Buttons (`Etims.Manage`):
    - **Download KRA lists** (`sync-codes`): toast the per-list counts.
    - **Send now** (`run`): toast "Queued N, sent N, failed N, waiting N".
    - **Edit** (default classification, receipt messages, Active switch). Deactivating warns: *"This branch stops reporting to KRA. What
      it sells while inactive is never reported."*

Item classification picker (reuse it on the product and category screens): a searchable combobox (`cmdk` is already installed) over
`GET /etims/devices/{anyDeviceId}/item-classes?search=…` (debounced), showing `code — name` and the tax type. Use the first active
device. If there is none, or its lists aren't downloaded (404), fall back to a plain text input.

### 4. Administration → eTIMS submissions (new page `/admin/etims/submissions`, `Etims.View`, module `etims`)

Nav item: `{ label: 'eTIMS Submissions', href: '/admin/etims/submissions', permission: 'Etims.View', module: 'etims' }`.

- **Filters:** device (branch), status, document type. Read `?deviceId=&status=` from the URL so the device card's links work.
- **Table** (server-paged, `@tanstack/react-table` like the other lists): created; document type; source (link to the sale, product or
  return); KRA number; status pill (Sent green, Pending grey, Failed amber, Rejected red); attempts; next attempt (for Failed);
  KRA's message.
- A row opens a **detail drawer** with the submission fields and two read-only, copyable JSON panes: "Sent to KRA" (`request`) and
  "KRA replied" (`response`).
- **Retry** (`Etims.Manage`) on Failed and Rejected rows and in the drawer. After retrying, refetch.
- **Explain the common states** above the table (as help text, not a modal):
  - *Pending "Waiting for N item(s) to be registered"*: the products it names have not been accepted by KRA yet. Look at the Item
    rows.
  - *Rejected Item*: usually a wrong or missing item classification. Fix it on the product or category, then Retry the item. What was
    waiting on it follows by itself.
  - *Sales waiting for "the original sale"*: credit notes go after the sale they reverse.

### 5. Product page → "KRA eTIMS" tab (`src/app/(app)/products/[id]/page.tsx`, inside `<IfModule module="etims">`, `Etims.View`)

- Show the KRA item code (or *"Not registered with KRA yet"*), the classification (own, or *"From category: …"* / *"Device default"*
  using `effectiveItemClassCode`), the quantity unit, the packaging unit and the origin.
- **Edit** (`Etims.Manage`, only while `!isRegistered`):
  - classification: the picker, with an empty option *"Use the category's"*;
  - quantity unit: a select of two-letter units only. Load class `10` from `GET /etims/devices/{id}/codes?codeClass=10`, keep the codes
    that are two characters, and default to the API's suggestion. Explain why: *"KRA builds item codes only from two-letter units.
    Pieces, litres and grams are reported as NO (number)."*;
  - packaging unit (class `17`, two letters, default `NT`).
- Once `isRegistered`, everything is read-only with the note: *"KRA registers an item once and does not allow changes."*
- The new-product flow does not need eTIMS fields: the category or device default covers it. Mention it in the success toast only
  when the business has eTIMS: *"Set its KRA classification on the product's eTIMS tab before its first sale, if the category's is not
  right."*

### 6. Categories (`src/app/(app)/categories/page.tsx`, `<IfModule module="etims">`, `Etims.Manage`)

- An action per category, **"KRA classification"**, that opens the picker and saves with `PUT /etims/categories/{id}`.
- Explain: *"Products in this category are registered with this classification unless they have their own. Products KRA already
  registered keep theirs."*
- There is no GET for it. Show "Set" after saving in this session; don't try to display the current value on load.

### 7. Receipts (`src/features/sales/Receipt.tsx`) — the part cashiers see

When the business has the `etims` module, fetch `GET /etims/sales/{saleId}/receipt` with React Query (key `['etims-receipt', saleId]`)
and add a **KRA block** after the totals:

```
---------------- KRA eTIMS ----------------
SCU ID:          {sdcId}
MRC:             {mrcNo}
Invoice No:      {invoiceNumber}
Receipt No:      {receiptNumber} / {totalReceiptNumber}
Date:            {sdcDateTime}
Internal Data:   {internalData}
Receipt Sign:    {receiptSignature}
             [ QR code: verificationUrl ]
```

- **QR code:** add `qrcode.react` (pin an exact version, e.g. `4.2.0`) and render `<QRCodeSVG value={verificationUrl} size={…} />`,
  about 28 mm on 80 mm paper and 35 mm on A4. Black on white; it must survive thermal printing.
- **Not signed yet** (`status !== Sent`): print *"KRA signature pending — reprint shortly"*, with no QR.
  - In the POS "sale complete" dialog (`PosPage.tsx` → `ReceiptDialog`), refetch every 5 s for up to 60 s while unsigned, so the
    cashier usually gets the signed receipt before printing. The worker runs every 15 s.
  - For Rejected, show `resultMessage` on screen (not on paper) to users with `Etims.View`.
- **404:** print no KRA block at all.
- The heading stays "TAX INVOICE" / "SALES RECEIPT" as today.
- Never recompute anything KRA returned; print it as given.
- `sales/[id]` reprints use the same component, so they get the block too.

### 8. Roles screen

No code needed beyond the two permissions. Check that `Etims.View` and `Etims.Manage` show under their module and that the Admin role
has them.

## Rules that do not change

- **Modules decide what is shown; permissions decide what is allowed.** Gate on both: `module: 'etims'` and the permission.
- The API reports by itself. There is no "send this sale to KRA" button anywhere on sales, returns or stock screens.
- Never show or store the device key; the API only ever sends `cmcKeyHint`.
- KRA's messages are shown as KRA wrote them (they are what support will ask about).

## Acceptance checks

1. A business registered **without** eTIMS:
   - no eTIMS menu items, no eTIMS tab on products, no category action, no KRA block on receipts;
   - `/admin/etims` shows "Not used by this business";
   - no request to `/etims/*` is made anywhere (check the network tab, including the POS).
2. A super user registers a VAT-registered business: the eTIMS switch is pre-ticked, shows on the review step and is sent as
   `etimsEnabled: true`.
3. A super user turns eTIMS on for an existing business from its detail page. After its admin's business refreshes, the KRA eTIMS
   menu appears, without signing out.
4. An admin registers the head office device; it shows SCU ID, MRC and the masked key. "Download KRA lists" fills the classification
   picker.
5. A product's eTIMS tab offers only two-letter units. After KRA registers the product, the tab is read-only and shows the item code.
6. A sale's receipt shows "KRA signature pending" at first, then, within about 15 s and without reprinting by hand in the POS dialog,
   the KRA block with a QR code that scans to the `verificationUrl`.
7. A Rejected submission shows KRA's message and the exact request and response. Retry moves it back to Pending.
8. A cashier without `Etims.View` still gets the KRA block on receipts, but no eTIMS menus.
9. A super user opens Platform → KRA eTIMS, opens a business with eTIMS on and no devices, and registers its head office device
   without signing in to the business. The business's own Administration → KRA eTIMS page then shows the same device.
10. A submission KRA refused at any business shows on the platform's "Needs attention" tab with its business and branch; Retry moves
    it to Pending and it leaves the tab.

## Tests to add

- `businessCapabilities`: `etims` only when listed in `modules`; **false** when `modules` is missing.
- Navigation: eTIMS items are hidden without the module, and hidden without the permission.
- `Receipt`: renders the KRA block and QR when signed, the "pending" line when not, and nothing on 404 or without the module. Mock
  the API with MSW the way `src/features/customers/insights.test.tsx` does (`setupServer`, plus `renderWithProviders`, `signIn` and
  `ok` from `@/test/render`).
- Register wizard: the eTIMS default follows "VAT registered", and the request carries `etimsEnabled`.
- Platform business detail: the eTIMS dialog calls `PUT /platform/businesses/{id}/etims` with the right value.
- Product eTIMS tab: read-only when `isRegistered`; unit select offers two-letter codes only.
- Platform eTIMS: the nav item needs `Platform.Etims.View`; the submissions call sends no `status` by default; registering a device
  posts to `/platform/etims/businesses/{businessId}/devices`.
