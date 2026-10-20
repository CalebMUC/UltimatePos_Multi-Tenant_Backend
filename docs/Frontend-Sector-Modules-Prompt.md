# Frontend Task: Business Sectors and Sector-Based Modules

> **For:** the coding agent working on the Ultimate POS frontend.
> **Depends on:** the API's business-sector release (`BusinessSector`, `BusinessModules`).
> Before you start, read `src/UltimatePos.Application/Business/BusinessModules.cs`, `src/UltimatePos.Application/Business/BusinessDefaults.cs`, `src/UltimatePos.Application/Business/Dtos/BusinessDtos.cs`, `src/UltimatePos.Application/Identity/Dtos/AuthDtos.cs`, `src/UltimatePos.Application/Platform/PlatformDtos.cs` and `src/UltimatePos.Api/Controllers/PlatformController.cs`. If the code and this prompt disagree, the code wins.

## Goal

Each business now has a **sector**: what line of business it is in. The API turns sector and kind into a list of **modules**: the areas of the app that business uses. The app must:

1. **Show only the modules a business uses.** A water refill station has no Production menu. An agrovet has no Containers features. A pharmacy has the Prescriptions register.
2. **Guard routes.** Opening a hidden module's URL directly shows a friendly "not used by this business" page.
3. **Hide module-specific options inside shared screens**, e.g. the container toggle at the till, or the Raw material item type on the product form.
4. **Platform (super user):** a sector picker at registration, a sector filter on the business list, a by-sector table on the dashboard, and a *Change sector* action.

## The model in one minute

A business has three independent settings:

| Setting | Answers | Values (enums are sent as **numbers**) |
|---|---|---|
| `sector` | What business is it? | `0` General, `1` Pharmacy, `2` Agrovet, `3` WaterRefill, `4` Minimart, `5` Supermarket, `6` Hardware |
| `kind` | How does it get stock? | `0` Manufacturer, `1` Trader |
| `businessType` | Who does it sell to? | `0` Wholesaler, `1` Retailer |

They combine freely. An agrovet can make its own feeds (Manufacturer) or only resell (Trader). A pharmacy can be retail or a wholesaler. **General** means not classified yet; every business registered before sectors existed is General.

**`modules` is worked out by the API from sector + kind. Never rebuild it in the frontend.** Don't write your own sector → menu table. Read `modules` and show what it lists. When the rules change (a new sector, a new module), only the API changes.

### Module keys

`modules` is a `string[]`. The keys are stable. **Ignore any key you don't recognise**; new ones may be added.

**Always present (core):** `dashboard`, `sales`, `customers`, `catalog`, `stock`, `purchasing`, `expenses`, `reports`, `mpesa`, `branches`, `settings`.

**Depending on the business:**

| Key | What it covers | Present for |
|---|---|---|
| `production` | Formulas, production runs, production cost report | Any business whose **kind is Manufacturer**, whatever its sector |
| `batches` | Batch and expiry setup, the expiry report, the near-expiry dashboard card | General, Pharmacy, Agrovet, Minimart, Supermarket |
| `prescriptions` | Prescription-only setup on products, the prescription register | General, Pharmacy |
| `deliveries` | Delivery orders: new order, delivery board, record delivery, rider summary | General, WaterRefill |
| `containers` | Containers sold with a pack: the "customer has own container?" toggle, the container link on unit conversions, refill quick-sale buttons | General, WaterRefill |

So, for example:
- a refill station (WaterRefill, Trader) gets core + `deliveries` + `containers`: **no production**
- a reselling agrovet (Agrovet, Trader) gets core + `batches`: **no containers, no production**
- an agrovet that manufactures (Agrovet, Manufacturer) gets core + `production` + `batches`
- an existing General business keeps every menu it has today (production only if it's a Manufacturer)

## Where to read it

- **Sign-in:** login, refresh, `POST /auth/switch-business` and `GET /auth/me` return `businesses: BusinessMembership[]`. Each membership now also has:
  ```ts
  sector: number;          // BusinessSector
  modules: string[];       // BusinessModules keys for that business
  ```
  The active business is the membership whose `businessId` equals the response's `activeBusinessId`. Read its `modules`.
- **Business profile:** `GET /businesses/current` → `BusinessDto` now also has `sector: number` and `modules: string[]`.
- **When it changes:** a super user can reclassify a business. The new `modules` arrive on the next refresh, switch-business or `GET /businesses/current`. Re-read `GET /businesses/current` when the app regains focus and after a token refresh, and rebuild the menu if `modules` changed. No logout is needed.

Keep `modules` in the same store as the active business, e.g. a `useActiveBusiness()` hook. Expose one helper and use it everywhere; don't scatter `modules.includes(...)` calls:

```ts
const hasModule = (key: string) => activeBusiness?.modules?.includes(key) ?? true; // missing list (older API) = show, as today
```

## Rule: modules decide what is offered; permissions decide what is allowed; data decides what must be shown

1. **A menu item, page or button appears only when** `hasModule(module)` **and** the user has the permission for it. Both are needed. Keep your existing permission checks.
2. **Modules are presentation only.** The API doesn't refuse calls for a hidden module; permissions are the security. Never treat "module hidden" as a reason to skip a permission check, or the reverse.
3. **Data that needs a field always gets the field, even when its module is hidden.** Hide *setup options and entry points*, never fields an existing record needs. The API still requires them:
   - A product with `isBatchTracked = true` still needs **batch number and expiry** when receiving a purchase order, adjusting or transferring stock, even if `batches` is hidden. Show those fields whenever the product is batch-tracked.
   - A product with `requiresPrescription = true` still needs the **prescription capture** at checkout, even if `prescriptions` is hidden.
   - A pack whose unit conversion has a `containerProductId` still offers the container line at checkout, even if `containers` is hidden.
   - An existing delivery order or production run can always be opened from a link: a sale's "from delivery DEL-…" link, the audit log, a notification. Show it read-only, without the module's menu.

## Where each module appears

| Module | Hide when absent |
|---|---|
| `production` | Production menu: formulas, runs, new run. *Production costs* report and dashboard production section. On the product form, the item types **Raw material** and **Packaging** (offer Finished good only). Formula/ingredient fields. |
| `batches` | On the product form, the *Batch-tracked (expiry dates)* checkbox (keep it read-only if the product already has it). *Expiry report*, near-expiry dashboard card, *Batches* tab on stock. Keep batch fields for batch-tracked products (rule 3). |
| `prescriptions` | On the product form, the *Prescription only*, generic name, strength and dosage form fields (read-only if already set). *Prescription register* menu. Keep prescription capture for prescription-only products (rule 3). |
| `deliveries` | Delivery menu: board, new order, rider summary. *Deliver this order* button. The delivery-related stock column ("on delivery orders", `reservedQuantity`), shown only when it isn't 0. |
| `containers` | The till's *Customer has own container?* toggle and the container line it adds. The *Container sold with this pack* picker on unit conversions (`containerProductId`). Refill quick-sale buttons (1L / 10L / 20L). |

Core modules are always present. Keep gating them by permission only, as today.

## Route guards

Wrap each module's routes in a guard: `requireModule('production')`, and so on. If the module is absent, show a **"Not used by this business"** page instead of the screen. It explains: *"Production isn't part of this business's setup. If you need it, contact support."* Give it a *Back to dashboard* button. This is **not** a 403: the user did nothing wrong. Keep the existing 403 handling for permission errors from the API.

## Platform screens (super user)

1. **Register business** (`POST /businesses`):
   - Add a **Sector** picker (required in the UI, default General).
   - When the chosen sector suggests a kind, pre-fill **Kind** and let the user change it. Read the profile in `BusinessDefaults.Sectors`: Pharmacy, WaterRefill, Minimart, Supermarket and Hardware suggest Trader. **Agrovet and General must choose**; the API returns `400` on `kind` otherwise.
   - **Allow negative stock** may be left unset. The API uses the sector default: `false` for WaterRefill, `true` otherwise. Show that default in the switch, editable.
   - After registering, show what the business starts with. A short note is enough, e.g. *"Starts with 10L and 20L refill units and Filter, Sanitation and Water Testing expense categories."*
2. **Businesses list** (`GET /platform/businesses?sector=`): add a sector filter and a Sector column.
3. **Business detail:** show sector, and add **Change sector** (`PUT /platform/businesses/{id}/sector` `{ sector }`, permission `Businesses.Register`). Explain in the dialog: *"Only the classification and its menus change. Units, expense categories and settings it already has stay as they are."* `400` means the sector is the same as now or unknown; show the message.
4. **Platform dashboard** (`GET /platform/dashboard`): a **By sector** table from `bySector[]` (`sector, businessCount, saleCount, revenue`), and a Sector column on the per-business rows (`businesses[].sector`).

Labels: `General` → "General / unclassified", `WaterRefill` → "Water refill station". For any other sector number, show "Other".

## Acceptance checks

- Sign in to a **WaterRefill / Trader** business:
  - No Production menu, and the product form offers only Finished good.
  - Deliveries menu, container toggle and refill quick buttons are present.
  - `/production` shows "Not used by this business".
- **Agrovet / Trader:** no container toggle at the till, no container picker on unit conversions, no Production. Expiry report present.
- **Agrovet / Manufacturer:** Production present. Containers still absent.
- **Pharmacy:** Prescription register present. A prescription-only product still asks for the prescription at checkout.
- **General** (e.g. any existing business): every menu it had before is still there.
- In a business without `batches`, receiving a purchase order line for a product that **is** batch-tracked still asks for batch and expiry.
- A cashier without `Production.View` in a Manufacturer business still doesn't see Production: module present, permission missing.
- A super user changes a business from General to WaterRefill. After that business's user refreshes (or the app regains focus), Production and Prescriptions disappear and Deliveries stays.
- An unknown key in `modules` (e.g. `"loyalty"`) is ignored without errors.
