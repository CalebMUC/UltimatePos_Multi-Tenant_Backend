# Frontend Task: Age-Restricted Sales (alcohol and tobacco) and the Liquor Sector

> **For:** the coding agent working on the Ultimate POS frontend (`Ultimate_Multi_Tenant_Frontend`).
> **Depends on:** the API release with migration `LiquorAgeRestrictedSales`.
> Before you start, read `AgeRestrictedSalesSettingsDto` in `src/UltimatePos.Application/Business/Dtos/BusinessDtos.cs`,
> `SellingHours.cs` and `UpdateAgeRestrictedSalesAsync` in `src/UltimatePos.Application/Business/`, `CheckAge` and
> `EnsureWithinSellingHours` in `src/UltimatePos.Application/Sales/SalesService.cs`, and the `AgeCheck*` records in
> `src/UltimatePos.Application/Sales/Dtos/SalesDtos.cs`. If the code and this prompt disagree, the code wins.

## Why

A wines and spirits shop may sell alcohol only to customers aged 18 or over, and the seller must ask for proof of age. Selling to a
minor can cost a fine of up to KES 150,000, a year in prison, and the licence. The licence also sets the **hours** alcohol may be sold,
and they differ by county. Cigarettes are 18+ too. The API now:

- marks products as **age-restricted** (`isAgeRestricted`);
- refuses a sale with such a product unless it says **how the customer's age was checked** (`ageCheck`);
- refuses such products **outside the business's selling hours**, when the business has set them, at the till and when a delivery
  goes out with a rider;
- keeps an **age-check register**: each sale of alcohol or tobacco, how the age was checked, and by whom.

There is also a new sector, **Liquor** (`8`), and a module key `ageRestricted` (see
[Frontend-Sector-Modules-Prompt.md](Frontend-Sector-Modules-Prompt.md)).

## Enums (numbers on the wire)

```ts
// AgeCheckMethod
0 NationalId      // "National ID"
1 Passport        // "Passport"
2 DrivingLicence  // "Driving licence"
3 OtherId         // "Other ID (alien card, student ID…)"
4 KnownCustomer   // "Regular: ID seen before"
// BusinessSector: 8 Liquor ("Wines & spirits / liquor store")
```

## 1. Settings → Age-restricted sales (module `ageRestricted`, permission `Businesses.Update`)

`GET /api/v1/businesses/current` → `BusinessDto.ageRestrictedSales`:

```jsonc
"ageRestrictedSales": {
  "minimumAge": 18,          // 18–25; the policy of 2025 proposes 21
  "weekdayFrom": "10:00",    // "HH:mm", East Africa Time; null with weekdayTo = any time
  "weekdayTo": "23:00",      // before weekdayFrom = runs past midnight ("17:00" → "02:00")
  "weekendFrom": null,       // Saturday and Sunday when different; null = the weekday hours
  "weekendTo": null
}
```

`PUT /api/v1/businesses/current/age-restricted-sales` with the same object, a **full replace**, returns the `BusinessDto`. Times are
"HH:mm" (an `<input type="time">` value works as is); seconds are dropped. A 400 has `details` keyed by `minimumAge`, `weekdayFrom`,
`weekdayTo`, `weekendFrom`, `weekendTo`. Show each under its field.

Screen:
- **Minimum age** (number, 18–25).
- **Selling hours**: a switch *"Limit the hours alcohol and tobacco can be sold"*. Off = both weekday times null. On = *From* / *To*;
  then a checkbox *"Different hours at weekends"* with its own *From* / *To*.
- When *To* is before *From*, show *"Runs past midnight: until {To} the next morning."*
- Help text: *"Use the hours on your liquor licence. Public holidays use the weekday hours."*
- A preview line, e.g. *"Alcohol and cigarettes can be sold 17:00–23:00 on weekdays, 14:00–23:00 at weekends."*

## 2. Product form

`ProductDto` / `ProductDetailDto` have `isAgeRestricted: boolean`.

- Create (`POST /products`): add an **Age-restricted (18+)** toggle, sent as `isAgeRestricted`. It defaults to off, except in a
  **Liquor** business, where it defaults to **on** and the user switches it off for soda, ice and snacks.
- Update (`PUT /products/{id}`): send `isAgeRestricted` as `true` or `false`. **Leaving it out (or null) keeps the current value**, so an
  older screen never clears it.
- Product list: an *18+* chip on age-restricted products, and a filter `GET /products/get-products?isAgeRestricted=true`.
- Show the toggle when `hasModule('ageRestricted')`; if the module is hidden but the product is already age-restricted, show it read-only.

## 3. The till

### Preview

`POST /sales/preview` (`SalePreviewDto`) now has:

| Field | Meaning |
|---|---|
| `requiresAgeCheck` | A line (or a bundle's part) is age-restricted |
| `minimumAge` | The age to check, when `requiresAgeCheck` |
| `withinSellingHours` | `false` = the sale will be refused now |
| `sellingHours` | The hours as text, e.g. `"10:00-23:00 every day"` |
| `lines[].isAgeRestricted` | Which lines need it |

- Mark age-restricted lines in the cart with an *18+* chip.
- When `withinSellingHours` is `false`, show a red banner on the cart: *"Alcohol and cigarettes can be sold {sellingHours}. Remove the
  18+ items to finish this sale."* and disable *Pay* until they are removed. Soda and snacks still sell.

### Checkout

When `requiresAgeCheck` is true, *Pay* first opens **Check the customer's age**:

> **Is the customer {minimumAge} or over?** Ask for an ID if you are not sure.
> [National ID] [Passport] [Driving licence] [Other ID] [Regular: ID seen before]
> [Cancel]

- One tap records the method and continues to payment. Nothing is typed: there is no ID number to store, and none should be.
- Order the buttons by what the cashier uses most; *Regular* last. Do not pre-select a button, and do not remember the last choice
  between sales.
- Send it as `ageCheck` on `POST /sales`. Without it the API answers **422 `AGE_CHECK_REQUIRED`**; if that happens (an older cached
  preview), open the same dialog and resend.
- **422 `OUTSIDE_SELLING_HOURS`**: show the message (it names the products and the hours) and leave the cart as it is.
- Do not send `ageCheck` from a saved setting or a default. The point is that the cashier makes the check, sale by sale.

### Receipt

`SaleDto.ageCheck` is set on sales of alcohol or tobacco. Print one line above the footer, 80 mm and A4: *"Age checked: National ID"*
(or the label of the method). Nothing when null.

## 4. Deliveries (module `deliveries`)

- **Dispatch** (*Send out with rider*, and checkout with delivery by rider): outside the hours the API answers
  **422 `OUTSIDE_SELLING_HOURS`**. Show the message on the order; the order stays *Pending*. Taking the order is always allowed.
- **Record delivery** (`POST /delivery-orders/{id}/deliver`): when the order has an age-restricted line, show the same age-check buttons,
  titled *"How did the rider check the customer's age at the door?"*, and send `ageCheck`. Without it the API answers
  `AGE_CHECK_REQUIRED`. The hours are not checked again here: the goods left in time.
- On the delivery note and the rider's list, print *"18+: see ID before handing over"* when the order has an age-restricted line.

## 5. Age-check register (module `ageRestricted`, permission `SalesReports.View`)

`GET /api/v1/sales/age-checks?from=YYYY-MM-DD&to=YYYY-MM-DD&utcOffsetMinutes=180` → `AgeCheckRegisterEntryDto[]`, newest first:

```jsonc
{ "saleId": "…", "saleNumber": "INV-000123", "soldAt": "2026-11-04T17:42:10Z", "saleStatus": 1,
  "ageCheck": 0, "checkedByName": "Mercy Atieno (Cashier)", "deliveryMode": 0, "customerName": null,
  "lines": [{ "productId": "…", "productName": "Tusker Lager 500ml Bottle", "quantity": 2, "unitSymbol": "btl", "lineTotal": 500 }] }
```

Page *Reports → Age-check register*:
- Date range (default today), a table: time, sale (link), cashier, method, items, total of the 18+ items, *Delivery* chip when
  `deliveryMode = 1`, *Voided* chip when `saleStatus` is Voided.
- Above it, per cashier: sales, and how many of them were *Regular: ID seen before*, as a share. A cashier who records "regular" on most
  evening sales is what the owner is looking for.
- Print / PDF, for an inspector.

## 6. Liquor sector

- Register business: sector **Liquor** suggests kind Trader and *Allow negative stock* off. After registering: *"Starts with Bottle,
  Crate, Carton and Case units and Packaging and Refrigeration expense categories."*
- Modules: core + `batches`, `deliveries`, `containers`, `bundles`, `ageRestricted`. The containers toggle reads, for a crate:
  *"Customer brought a crate of empties?"* (it is the same `containerProductId` mechanism as the refill station).

## Acceptance checks

- In a Liquor business, add a beer and a soda to the cart: the beer has an *18+* chip; *Pay* asks for the age check; the receipt prints
  *Age checked: National ID*.
- The soda alone: no age dialog.
- A bundle containing gin asks for the age check.
- Set selling hours that start an hour from now: the cart shows the red banner, *Pay* is disabled; remove the beer and the soda sells.
- Set *From 17:00 To 02:00*: the settings screen says it runs past midnight; at 01:00 the next day beer still sells.
- Edit an age-restricted product's price or name in an old screen that does not send `isAgeRestricted`: it stays age-restricted.
- Dispatching a delivery with beer outside hours shows the message and leaves the order Pending; recording the delivery asks how the
  rider checked the age.
- The register lists today's beer sales with the method and cashier, and not the soda-only sales.
- In a Pharmacy business, no *18+* toggle, no age register.
