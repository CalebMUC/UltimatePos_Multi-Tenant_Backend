# Frontend Task: Bundles and a Fast Refill-Station Counter

> **For:** the coding agent working on the Ultimate POS frontend.
> **Depends on:** the API's bundles release (migration `SaleBundles`).
> Before you start, read `src/UltimatePos.Application/Catalog/ProductBundles.cs`, `src/UltimatePos.Api/Controllers/ProductBundlesController.cs`,
> `src/UltimatePos.Application/Sales/Dtos/SalesDtos.cs` and §3.5 of `docs/WATER_REFILL_STATION_ONBOARDING.md`. If the code and this
> prompt disagree, the code wins.

## Goal

A refill station sells three things over the counter:

1. **Water into the customer's own bottle**: 1L = 10, 10L = 100, 20L = 200 (a plain water line).
2. **Bottle + water at one price**: 20L heavy-duty 1500, 20L semi-flexible 1300, 10L 250, 1L 30, 500ml 20. Bottles are never sold
   empty.
3. **Packs**: 12 × 1L, 24 × 500ml.

Items 2 and 3 are **bundles**: one item at one price whose parts (litres of water, bottles) each leave their own stock. The API does
all the splitting. The frontend must:

- make any of these **one or two taps** at the counter, with the total, change and stock always taken from the server;
- let the owner **set bundles up** (name, price, parts) without a developer;
- show a bundle as **one row** on the cart, receipt and sale detail.

## Rules that do not change

- Enums are sent as **numbers**: `paymentMethod` `0` Cash, `1` Credit, `2` MpesaTill, `3` MpesaStkPush; `priceType` `0` Wholesale, `1` Retail.
- Every response is `{ success, data, error: { code, message, details }, meta }`. `details` is keyed by camelCase field name.
- **Never compute prices, splits or stock in the browser.** Show what `POST /sales/preview` returns. The client never sends a price.
- Hiding a button is not security. The API checks permissions on every call.

## Module and permissions

| Need | Check |
|---|---|
| Show anything about bundles | `modules` (from sign-in `businesses[].modules` or `GET /businesses/current`) contains **`bundles`** |
| See bundles at the counter | `Products.View` |
| See how many can be sold (`availableQuantity`) | `Stock.View`. Without it the field is `null`: do not disable the tile |
| Create, edit, deactivate | `Products.ManageBundles` |
| Discount on a bundle | `Sales.ApplyDiscount` (as for lines) |

## API

### Bundles

| Call | Body / query | Returns |
|---|---|---|
| `GET /api/v1/product-bundles?isActive=true&search=` | `search` matches name or code | `ProductBundleDto[]`, sorted by name |
| `GET /api/v1/product-bundles/{id}` | | `ProductBundleDto` |
| `POST /api/v1/product-bundles` | `CreateProductBundleRequestDto` | `201`, `ProductBundleDto` |
| `PUT /api/v1/product-bundles/{id}` | same shape: **replaces** name, code, prices and all parts | `ProductBundleDto` |
| `POST /api/v1/product-bundles/{id}/deactivate` · `/activate` | | `ProductBundleDto` |

```jsonc
// CreateProductBundleRequestDto / UpdateProductBundleRequestDto
{
  "name": "20L Heavy-duty bottle + water",
  "code": "HD20",                 // optional, unique, stored upper-case
  "description": null,
  "retailPrice": 1500,
  "wholesalePrice": null,         // null = wholesale customers pay retailPrice
  "components": [
    { "productId": "<water>",     "unitOfMeasureId": "<20L>", "quantity": 1 },
    { "productId": "<HD bottle>", "unitOfMeasureId": "<pcs>", "quantity": 1 }
  ]
}

// ProductBundleDto
{
  "productBundleId": "…", "name": "…", "code": "HD20", "description": null,
  "retailPrice": 1500, "wholesalePrice": null, "isActive": true,
  "availableQuantity": 9,          // whole bundles the signed-in branch can sell now; null without Stock.View
  "components": [{
    "productBundleComponentId": "…", "productId": "…", "productName": "Purified Water", "sku": "…", "productIsActive": true,
    "unitOfMeasureId": "…", "unitSymbol": "20L", "quantity": 1,
    "quantityInBaseUnits": 20, "baseUnitSymbol": "l",   // per bundle
    "availableInBaseUnits": 4980                         // null without Stock.View
  }],
  "createdAt": "…", "lastUpdatedAt": null
}
```

Validation (`400 VALIDATION_FAILED`, shown beside the field from `details`): `name` required (≤200); `code` ≤50 and unique;
`retailPrice` > 0; `wholesalePrice` > 0 or null; `components` 1–20, quantity > 0, each product + unit once. `400 INVALID_ASSIGNMENT`
when a part is inactive, not a finished good, prescription-only, or **not sold in the unit given** (a bottle has `pcs`, not `20L`).

### Selling

`POST /api/v1/sales/preview` and `POST /api/v1/sales` take a new optional array next to `lines`:

```jsonc
{
  "customerId": null, "paymentMethod": 0, "amountTendered": 2000,
  "lines":   [{ "productId": "<water>", "unitOfMeasureId": "<20L>", "quantity": 1 }],          // own bottle, 200
  "bundles": [{ "productBundleId": "<HD20>", "quantity": 1, "discountAmount": null, "discountPercent": null }]  // 1500
}
```

- A sale needs at least one line **or** one bundle.
- `quantity` on a bundle is a **whole number** > 0. Each `productBundleId` appears **once**: tapping the same tile again increments it.
- A bundle discount covers all of that bundle's quantity, not each one.

The **response** (preview and sale) has one line per part, and a summary per bundle:

```jsonc
"lines": [
  { "productName": "Purified Water", "unitSymbol": "20L", "quantity": 1, "lineTotal": 200,
    "productBundleId": "<HD20>", "bundleName": "20L Heavy-duty bottle + water", "bundleQuantity": 1, /* preview: stockSufficient… */ },
  { "productName": "20L Heavy-duty bottle", "unitSymbol": "pcs", "quantity": 1, "lineTotal": 1300,
    "productBundleId": "<HD20>", "bundleName": "20L Heavy-duty bottle + water", "bundleQuantity": 1 },
  { "productName": "Purified Water", "unitSymbol": "20L", "quantity": 1, "lineTotal": 200, "productBundleId": null }
],
"bundles": [{ "productBundleId": "<HD20>", "name": "20L Heavy-duty bottle + water", "quantity": 1, "total": 1500, "discountAmount": 0 }],
"totalAmount": 1700
```

`bundles` is `null` when the sale has none. **Group lines by `productBundleId`**: lines with it belong to that bundle row; lines without
it are ordinary rows.

| Error | When | Show |
|---|---|---|
| `409 INSUFFICIENT_STOCK` | Any part short (e.g. *"'20L Heavy-duty bottle' (1 available, 2 needed)"*) | The message; keep the cart |
| `400 VALIDATION_FAILED`, `details.bundles` | Fraction, zero, or the same bundle twice | The message (a client bug if it happens) |
| `400 INVALID_ASSIGNMENT` | The bundle was deactivated meanwhile | The message; refresh bundles; remove the tile |
| `404 NOT_FOUND` | Bundle deleted or from another business | Refresh bundles |

## Screens

### 1. Counter (refill station layout)

Use this layout when `modules` contains `bundles` **and** `containers` (a refill station). Other businesses keep their normal
search-and-scan till, plus bundles in search results (below).

```
┌─ OWN BOTTLE ───────────────────────────────────────────────┐
│  [ 1L  10 ]   [ 10L  100 ]   [ 20L  200 ]                   │   water product × its units
├─ WITH BOTTLE ──────────────────────────────────────────────┤
│  [ 20L Heavy-duty  1500 · 9 ]  [ 20L Semi-flex  1300 · 12 ] │   active bundles, one tile each
│  [ 10L  250 · 4 ]  [ 1L  30 · 88 ]  [ 500ml  20 · 75 ]      │   "· n" = availableQuantity
├─ PACKS ────────────────────────────────────────────────────┤
│  [ 12 × 1L  360 · 7 ]  [ 24 × 500ml  480 · 3 ]              │
└────────────────────────────────────────────────────────────┘
 CART                                  Total 1,700   [ Pay ▸ ]
```

Build every tile from data. **Hard-code nothing:** no product names, sizes or prices.

- **Own bottle row:**
  - Find the water product as the product in the `Water Refill` category. Better: let the owner pick it once in the station's
    settings and store its id per business in local settings.
  - Show one tile per unit that has a Retail price: its base unit plus `GET /products/{id}` → `unitConversions` that are active.
  - Read the prices from `priceTiers`; order the tiles by `conversionFactor`.
- **With bottle / Packs rows:** `GET /product-bundles?isActive=true`.
  - A bundle with exactly **one** piece-counted part goes in *With bottle*; any other bundle goes in *Packs*. A piece-counted part
    is one whose `baseUnitSymbol` is `pcs` and whose `quantityInBaseUnits` is 1.
  - Order each row by the litres in the bundle (the sum of `quantityInBaseUnits` over parts with `baseUnitSymbol` `l`), largest first.
- **Tile:** name (or code), price (`retailPrice`, or `wholesalePrice ?? retailPrice` for a wholesale customer), and
  `availableQuantity`. Disable the tile at `0`; show *Out* rather than hiding it, so the cashier knows why.
- **Tap = add 1.**
  - A tile already in the cart increments its quantity; it never adds a second entry (the API refuses duplicates).
  - Long-press or a `×` on a cart row sets the quantity.
  - Cart rows: bundle rows show the name, quantity and server `total`; own-bottle rows show the size and quantity.
- **Quick keys.** Optional, but this is what makes it fast:
  - Keys `1`–`9` map to the tiles in order.
  - Typing a bundle `code` (e.g. `HD20`) in the search box adds it.
  - `Enter` opens *Pay*.
- **Live total:**
  - On every cart change, debounced by about 250 ms, call `POST /sales/preview` with `lines` and `bundles`.
  - Show `totalAmount`, `discountTotal` and the bundle totals from the response.
  - Flag a bundle row in red when any of its lines has `stockSufficient: false`, and disable *Pay* while any line is short.
- **Pay:** unchanged (Cash with change, Till payment picker, STK push, Credit for a registered customer). Send exactly the cart's
  `lines` and `bundles`.
  - On `409 INSUFFICIENT_STOCK`, keep the cart and show the message.
- **After a sale:**
  - Clear the cart and print or show the receipt.
  - Re-fetch `GET /product-bundles?isActive=true` and the water stock in the background, so the `availableQuantity` figures stay current.
- **Container toggle:** for a refill station with bundles, do **not** show the older *"Customer has own container?"* toggle. It still
  appears only where a unit conversion has `containerProductId`, and the station should not set one. The *Own bottle* and *With
  bottle* rows replace it.

**Generic till (every business with `bundles`):** include bundles in product search results, marked with a *Bundle* badge. Search
with `GET /product-bundles?isActive=true&search=` alongside the product search. Choosing one adds a bundle cart row, as above.

### 2. Receipt, sale detail, sales list

- Show one row per entry of `bundles[]`: `quantity × name … total`. Do **not** print its part lines on the customer receipt.
- On the sale detail screen, show the part lines indented under the bundle row, with a "Parts" caption.
- Lines with no `productBundleId` print as today.
- The totals block is unchanged (`subtotal`, `discountTotal`, `taxTotal`, `totalAmount`).

### 3. Returns and voids

- **Void:** unchanged; it puts back every part.
- **Return:** returns stay **per line**. Under each bundle, list its parts with a quantity box each, e.g. *Return the cracked bottle
  only → refund 1300*. The API refunds each line's own share. Add a *Return whole bundle* shortcut that fills every part of that
  bundle with the quantity left to return.

### 4. Bundles screen (catalogue → Bundles)

- **List:**
  - Columns: name, code, retail price, wholesale price, parts (e.g. *20L water + 1 × Heavy-duty bottle*), available, active.
  - Search box; *Active / Inactive / All* filter.
- **Form:**
  - Name, code, description, retail price, wholesale price (optional).
  - **Parts** table: product picker (finished goods only), unit picker (the product's base unit and its active pack units only,
    from `GET /products/{id}`), quantity (decimals allowed: `0.5` litre for 500ml), and *Add part* / remove.
  - Under the table, a read-only line: *"Per bundle: 20 l Purified Water, 1 pcs 20L Heavy-duty bottle"* (from the saved
    `components[].quantityInBaseUnits` + `baseUnitSymbol`).
  - Help text: *"Parts with their own price (water) count at that price; parts without one (bottles) take the rest."*
- **Saving:**
  - Create sends `POST`; edit sends `PUT` with the **full** parts list.
  - Show `details` errors per field.
  - Show `INVALID_ASSIGNMENT` above the parts table.
- **Deactivate / Activate** actions with a confirm dialog. A deactivated bundle disappears from the counter.

### 5. Delivery orders

Not supported for bundles yet. In the delivery order form, offer only the existing product lines. Do not show bundle tiles there.

## Acceptance checks (with AQUA HUB's price list)

1. Tap *20L Heavy-duty* once. The cart shows 1500, and the preview has two lines (water 200, bottle 1300) grouped under one row.
2. Tap it again. The quantity becomes 2 and the total 3000. There is still one row, and one `bundles` entry in the request.
3. Add *20L own bottle*. The total is 3200. Pay cash 5000; the change is 1800. The receipt shows two rows.
4. With 1 heavy-duty bottle in stock, its tile shows `1`. After one sale it shows *Out* and is disabled. Forcing a sale returns 409 and keeps the cart.
5. *Pack of 24 × 500ml* takes 12 litres and 24 bottles. The water's stock and the 500ml bottles' stock both drop after the sale.
6. Return only the bottle from a *10L + bottle* sale. The refund is 150, and the bottle's stock goes back up by 1.
7. A user without `Products.ManageBundles` sees no *New bundle* button. A user without `Stock.View` sees tiles with no counts, all enabled.
8. A business without the `bundles` module sees no bundle UI anywhere.
