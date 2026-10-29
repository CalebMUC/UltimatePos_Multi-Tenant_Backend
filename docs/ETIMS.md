# KRA eTIMS (OSCU)

Ultimate POS reports each branch's trading to KRA through the eTIMS **Online Sales Control Unit** API, by way of KRA's Apigee gateway.
Every branch that reports has its own **device**: the business's KRA PIN, the branch's KRA branch id (`bhfId`: `00` for the head
office, then `01`, `02`...) and the communication key (`cmcKey`) KRA issues when the device is initialised.

Once a branch has an active device, everything below is reported automatically. Nobody has to press "send".

| What happens in the POS | What KRA gets |
|---|---|
| The device is registered | The branch's stock on hand, as one opening stock in (`06`) |
| A product exists | `saveItem`, once (KRA refuses to save an item twice) |
| A sale is completed | `sendSalesTransaction` (receipt type `S`), signed by KRA |
| A completed sale is voided | A credit note (`R`) for the whole sale, naming the original invoice |
| Goods are returned | A credit note (`R`) for what came back, naming the original invoice |
| Any stock movement (sale, return, void, receipt from a supplier, adjustment, transfer, production) | `insert/stockIO`, then `save/stockMaster` for each item in it |

Documents created **before** the device was registered are never sent; KRA would refuse them as late anyway. Purchases are reported
only as stock in (`02`), because KRA's `sendPurchaseTransactionInfo` accepts a purchase only when it already holds the supplier's
invoice (see below).

## Which businesses use it

eTIMS is switched on per business, because not every business needs it. A super user chooses it when registering the business
(`etimsEnabled` on `POST /api/v1/platform/businesses`) and can change it later with `PUT /api/v1/platform/businesses/{id}/etims`
`{ "enabled": true }`. The business's `etimsEnabled` and its `modules` (which include `"etims"` only when it is on) tell the frontend
whether to show anything of eTIMS.

**While it is off:**
- Nothing is reported.
- Device set-up, sends and retries are refused (`InvalidAssignmentException`).
- Devices and what was sent before are kept.

**When it is switched on again**, reporting resumes from that moment; the time in between is never reported. KRA's stock is brought
back in line with what each branch held then, as an adjustment (`06` in, `16` out) against KRA's own balance.

Businesses registered before the switch existed start with eTIMS off.

## Running it as the integrator

The platform is the third-party integrator: one set of Apigee credentials serves every business, and each device adds the business's
own PIN, branch id and `cmcKey`. Super users can onboard and watch every business without signing in to it, under
`/api/v1/platform/etims`:

| | |
|---|---|
| `GET /platform/etims` | Every business with eTIMS on (or with devices): devices, Pending / Failed / Rejected / Sent, when KRA last accepted something, the oldest problem |
| `GET /platform/etims/submissions` | Submissions across businesses, most recently attempted first. No `status` = Failed and Rejected |
| `GET /platform/etims/submissions/{id}`, `POST .../retry` | One submission's request and reply; send it again |
| `GET /platform/etims/businesses/{businessId}` | The business's branches, each with its device or none |
| `POST .../businesses/{businessId}/devices`, `PUT .../devices/{deviceId}` | Register a branch's device with KRA for the business; edit or deactivate it |
| `POST .../devices/{deviceId}/sync-codes`, `GET .../item-classes`, `POST .../run` | KRA's lists; send now |

They need `Platform.Etims.View` or `Platform.Etims.Manage`, which the Super User role holds. Each call runs inside the named business
through the same `EtimsService` the business uses, so the same rules apply (eTIMS must be on, one device per branch, and so on).
Registering, editing a device and retrying are recorded in that business's audit log as platform support actions.

Product and category classifications stay with the business.

KRA's side is not automated: each taxpayer must still apply for eTIMS (OSCU) on KRA's portal and get its device serial number before
its device can be registered here. Production credentials come only after KRA certifies the integration.

## Setting it up

1. **Credentials** (one Apigee app on developer.go.ke serves every business). Set them as user-secrets or environment variables, never in
   `appsettings.json`:

   ```
   dotnet user-secrets set Etims:ConsumerKey    <key>    --project src/UltimatePos.Api
   dotnet user-secrets set Etims:ConsumerSecret <secret> --project src/UltimatePos.Api
   dotnet user-secrets set Etims:ApigeeAppId    <app id> --project src/UltimatePos.Api
   ```

   In Docker use `Etims__ConsumerKey` and so on. `Etims:BaseUrl` and `Etims:TokenUrl` default to the **sandbox**
   (`https://sbx.kra.go.ke/...`). Point them at production only after KRA certifies the integration, and set
   `Etims:ReceiptVerificationUrl` to the production receipt link at the same time.

2. **Permissions.** `Etims.Manage` registers devices, syncs KRA's lists, sets product classifications and retries submissions.
   `Etims.View` reads devices and submissions. A sale's KRA signature for its receipt needs only `Sales.View`.

3. **Register the branch's device:** `POST /api/v1/etims/devices`

   ```json
   { "branchId": "...", "deviceSerialNumber": "UPOSP052446551J001", "defaultItemClassCode": "1010160100",
     "receiptTopMessage": "Karibu", "receiptBottomMessage": "Asante" }
   ```

   This calls KRA's `/initialize` and stores the `cmcKey`, SDC id and MRC number. KRA initialises a device only once and answers
   `902 This device is installed` after that. For such a device, also pass the `cmcKey` it issued the first time; the POS checks it with
   a read before keeping it.

4. **Download KRA's lists:** `POST /api/v1/etims/devices/{id}/sync-codes`. After that:
   - `GET .../codes?codeClass=10` returns the code lists.
   - `GET .../item-classes?search=soap` searches the item classifications.

5. **Describe products before their first sale.** Every item needs an item classification. The POS takes the first one it finds:
   - the product's own: `PUT /api/v1/etims/products/{productId}`
   - its category's: `PUT /api/v1/etims/categories/{categoryId}`
   - the device's `defaultItemClassCode`

   **Once KRA has registered an item, nothing about it can change.** KRA refuses to save it again, so the product's eTIMS settings are
   locked from then on.

## How it works

`EtimsWorker` (in the API host) runs every `Etims:IntervalSeconds` (15 s by default). For each active device it calls
`EtimsSyncService.RunAsync`, which does two things.

**Sweep.** Finds what happened at the branch since the last sweep and queues one `EtimsSubmission` per document, in the order things
happened. At the same moment, stock in comes first, then the sale, then the stock the sale took out. The window reaches 10 minutes
before the last sweep, so a transaction still committing then isn't missed. A document is never queued twice, because `SourceKey` is
unique per device. Products are registered before anything that names them, and every product is checked every 10 minutes.

**Send.** Sends submissions that are due, items first:

- A document waits until every item it names has been registered on the device.
- A credit note waits until its sale is accepted.
- After a failed send, a submission is retried after 1, 2, 4... minutes, up to an hour apart.
- After `Etims:MaxAttempts` attempts (10 by default) it becomes **Rejected** and waits for a manager:
  - `GET /api/v1/etims/submissions?status=Rejected` lists what KRA refused.
  - `GET /api/v1/etims/submissions/{id}` shows the exact request and KRA's reply.
  - `POST /api/v1/etims/submissions/{id}/retry` sends it again. An item is re-described from its current settings when it is retried.

Each submission is saved straight after its send, so a crash never sends an accepted document twice. The worker and
`POST /api/v1/etims/devices/{id}/run` (which runs a pass immediately) share a lock.

### KRA's strict series

KRA keeps three number series per taxpayer and accepts only the exact next number in each:

| Series | Where it appears |
|---|---|
| Item code sequence | The 7 digits ending every item code. One series for the whole PIN, whatever the rest of the code says. |
| Invoice number | `invcNo` of sales and credit notes |
| Stock in/out number | `sarNo` |

A document's number is therefore decided **when it is sent**, and kept only if KRA accepts it. A refused document never leaves a gap.
When KRA refuses a number and names the one it expects ("Expected sequence ending with ********13", "Invalid sarNo: Expected: 25",
"use the expected value: 1"), the device's counter follows it and the document is sent again in the same attempt.

Documents name items by placeholder until they are sent, and go out with the code and classification KRA actually registered. The
request stored on an accepted submission is exactly what KRA holds.

If KRA answers `924 Invoice number already exists`, the POS reads that invoice back (`selectInvoiceDetail`):

- If it is the same document, accepted earlier but with the reply lost, KRA's copy and signature are taken.
- Otherwise the number is used up, and the next one is tried.

Each attempt makes at most three sends.

### Stock

KRA keeps its own running balance of each item from the stock in/outs it accepted, and refuses a stock master (`rsdQty`) that
disagrees with it. The POS keeps that balance per device (`EtimsStockBalances`) and states it.

KRA also refuses to sell an item it holds no stock record for. That is why the branch's stock on hand at registration goes to KRA as
opening stock. Selling stock the branch doesn't have (negative stock) is refused by KRA.

### Receipts

`GET /api/v1/etims/sales/{saleId}/receipt` returns what a receipt must print: the invoice number, KRA's receipt numbers, internal data,
receipt signature, SDC id, MRC number and SDC time. It also returns `verificationUrl`
(`ReceiptVerificationUrl + PIN + bhfId + rcptSign`) for the QR code. The fields are null until KRA has signed the sale.

### Item codes and units

An item code is the origin country, the item type (1 raw material or packaging, 2 finished good), the packaging unit (`NT`), the
quantity unit and the 7-digit sequence, e.g. `KE2NTBX0000014`. KRA's gateway accepts only **two-letter** quantity units in a code. It
refuses `U`, `L`, `LTR`, `GRM` and the like however the code is padded. So:

- `kg` → `KG`, box → `BX`, bag → `BG`, packet → `PA`, dozen → `DZ`, ...
- pieces, litres, grams, metres and anything unrecognised → `NO` (number).

### Amounts and codes

Prices in the POS include VAT. Each line is reported with:

- `pkg` = 1 (KRA refuses anything else for `NT`)
- `splyAmt` = quantity × price
- `dcAmt` = the discount
- `totAmt` = supply − discount
- `taxAmt` = total × rate / (100 + rate)
- `taxblAmt` = total − tax

The header totals are the sums of the lines by tax type. The header rate for `E` is 0, because KRA's code list has `E` (8%) switched
off and refuses `taxRtE: 8`. The receipt's trade name and messages are cut to KRA's 20 characters.

| POS | eTIMS |
|---|---|
| Tax: Standard / Zero-rated / Exempt; business not VAT-registered | `B` (16%) / `C` (0%) / `A` (exempt); `D` (non-VAT) |
| Payment: Cash / Credit / any M-Pesa | `01` / `02` / `06` |
| Stock in: purchase receipt / return or void / transfer in / production output / adjustment up / opening stock | `02` / `03` / `04` / `05` / `06` / `06` |
| Stock out: sale / transfer out / used in production / adjustment down | `11` / `13` / `14` / `16` |
| Refund reason (returns and voids) | `06` |

## Endpoints

All of these are under `/api/v1/etims`.

| | |
|---|---|
| `GET devices`, `GET devices/{id}` | Devices (the cmcKey is masked), their counters and submission counts by status |
| `POST devices`, `PUT devices/{id}` | Register and initialise; edit settings or deactivate |
| `POST devices/{id}/run` | Sweep and send now |
| `POST devices/{id}/sync-codes`, `GET devices/{id}/codes`, `GET devices/{id}/item-classes` | KRA's lists |
| `GET devices/{id}/kra/{what}?argument=` | Reads passed through from KRA: `taxpayer`, `branches`, `notices`, `items`, `purchases` (invoices suppliers issued to you), `imports`, `sales`, `stock-moves`, `customer-pin` (argument = PIN), `invoice` (argument = number) |
| `POST devices/{id}/customers/{customerId}`, `POST devices/{id}/branch-users`, `POST devices/{id}/imported-items` | Register a customer or a branch user with KRA; convert an imported item |
| `GET/PUT products/{productId}`, `PUT categories/{categoryId}` | How products are described |
| `GET submissions`, `GET submissions/{id}`, `POST submissions/{id}/retry` | What was sent and what KRA said |
| `GET sales/{saleId}/receipt` | A sale's signature for its receipt |

## Testing against the sandbox

There are two ways to run against the sandbox.

**The POS's own sync service.** `EtimsSandboxPostgresTests` registers a business with the sandbox PIN on a test database, stocks it,
sells, takes a return and writes off stock. The real `EtimsSyncService` and `KraEtimsClient` then report all of it to KRA.

```
set ULTIMATEPOS_TEST_POSTGRES=Host=localhost;Port=5432;Username=postgres;Password=...
set ETIMS_CONSUMER_KEY=...  set ETIMS_CONSUMER_SECRET=...  set ETIMS_APIGEE_APP_ID=...  set ETIMS_CMC_KEY=...
set ETIMS_SANDBOX_REPORT=etims-pos-sync.json     (optional: every submission and KRA's reply)
dotnet test tests/UltimatePos.Api.Tests --filter EtimsSandboxPostgresTests
```

**The Postman collection's scenario.** `tools/UltimatePos.EtimsSandbox` runs it endpoint by endpoint through the POS's client and
payload builder. It also checks that KRA's code list holds every code the POS sends, and probes what KRA accepts:

```
dotnet run --project tools/UltimatePos.EtimsSandbox -- --out etims-results.json
```

Both default to the collection's test taxpayer (`P052446551J`, branch `00`, `UPOSP052446551J001`). Override with `ETIMS_TIN`,
`ETIMS_BHF_ID` and `ETIMS_DVC_SRL_NO`.

The automated tests don't call KRA:

- `EtimsPayloadBuilderTests` checks the bodies against the collection's figures and KRA's rules.
- `KraEtimsClientTests` checks the HTTP side, including the gateway's `responseHeader` refusals.
- `EtimsPostgresTests` runs sales, returns, voids, purchases and adjustments end to end with a fake KRA. The fake enforces the strict
  series and can lose a reply.

### What the sandbox does (October 2026)

- **Invoices for P052446551J are stuck.** KRA's own automated test made invoice 1 on 13 Oct 2026. The gateway still asks for invoice
  1, and eTIMS refuses it as `924 already exists`. No client can send a sale for this PIN until KRA resets the series. Ask KRA
  (timsupport@kra.go.ke) to reset it, or to issue a fresh test PIN.
- **Purchases need a record:** "You dont have any purchase records for tin ...". `getPurchaseTransactionInfo` returns none for the
  sandbox PIN.
- **Item composition needs stock first.** KRA refuses it until the component has a stock master.

## Not covered yet

- **Purchase transactions.** Confirming the supplier invoices KRA holds (`getPurchaseTransactionInfo` → `sendPurchaseTransactionInfo`)
  needs a matching screen. Until then, receipts go to KRA as stock in.
- **Item composition** (`saveItemComposition`) is available in `EtimsApi`, but formulas are not reported automatically.
- **Several branches under one PIN.** An item KRA registered through one branch is sent with the same code by the others. Whether KRA
  accepts that per branch could not be tested: the sandbox PIN has one branch.
- **The `cmcKey` is stored in plain text** in `EtimsDevices`, like the other integration keys. Restrict database access accordingly.
