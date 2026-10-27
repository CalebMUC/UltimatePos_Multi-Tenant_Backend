# Frontend Task: Product Brands

> **For:** the coding agent working on the Ultimate POS frontend.
> **Depends on:** the API's product-brands release (migration `ProductBrands`).
> Before you start, read `src/UltimatePos.Api/Controllers/BrandsController.cs`, `src/UltimatePos.Application/Catalog/Dtos/CatalogDtos.cs`
> and the brand section of `src/UltimatePos.Application/Catalog/CatalogService.cs`. If the code and this prompt disagree, the code wins.

## Goal

A minimart has many versions of one item: *Maize Flour 2kg* from Soko, Pembe, Jogoo and Ndovu. The category says **what** it is
(Maize Flour); the new **brand** says **whose** it is. Staff must be able to:

1. Keep a list of brands (Catalog → Brands).
2. Give a product its brand on the product form.
3. Find products by brand: a brand filter and column on the product list, and brand search at the till.

Brand is optional. Products without one keep working exactly as before.

## API

All under `/api/v1`, wrapped in `ApiResponse<T>` as usual.

| Call | Permission | Notes |
|---|---|---|
| `GET /brands?search=&isActive=` | `Products.View` | Sorted by name. `BrandDto { brandId, name, isActive, productCount }`. Not paged (a shop has tens of brands, not thousands) |
| `POST /brands { name }` | `Products.ManageBrands` | Name required, at most 100 characters, unique per business **ignoring case** (`"soko"` vs `"Soko"` → 400 on `name`) |
| `PUT /brands/{id} { name }` | `Products.ManageBrands` | Rename. Shows on every product with the brand at once; product names are not changed |
| `POST /brands/{id}/activate`, `/deactivate` | `Products.ManageBrands` | A deactivated brand stays on its products but cannot be given to another one. There is no delete |

**Products:**
- `CreateProductRequestDto.brandId` (optional). Must be an active brand of the business, else 400 on `brandId`.
- `UpdateProductRequestDto.brandId` and `removeBrand`:
  - `brandId` set → changes the brand.
  - `brandId` **null or missing → keeps the current brand** (so an old form never wipes it).
  - `removeBrand: true` → clears it. Sending both is a 400.
  - Saving a product that keeps a now-deactivated brand is allowed.
- `ProductDto` and `ProductDetailDto` (list, detail, `GET /products/lookup`) now carry `brandId` and `brandName`.
- `GET /products/get-products?brandId=` filters by brand. `search` now also matches the brand name, so typing *"pembe"* at the till
  finds every Pembe product.
- **Bulk upload:** the template now has **Description** (column 9) and **Brand** (column 10), both optional. A brand name that does not exist
  yet is **created** when the file is committed, once however many rows use it (matched ignoring case). A row naming a deactivated
  brand is an error.

## Screens

### Catalog → Brands (new menu item, after Categories)

- Show when the user has `Products.View`. Create, rename, activate and deactivate need `Products.ManageBrands`.
- Table: Name, Products (`productCount`, links to the product list filtered by `brandId`), Status, actions.
- Search box (`search`), Active / Inactive / All filter.
- Create and rename in a small modal with one field. Show the API's 400 message under the field.
- Deactivate confirmation: *"Products already under this brand keep it. You won't be able to give it to new products."*

### Product form

- **Brand** picker right after Category. Searchable select from `GET /brands?isActive=true`, with a *"+ New brand"* option that opens the
  create modal and selects the result (only with `Products.ManageBrands`).
- Optional, with a clear (×) button. On edit:
  - send `brandId` when it changes;
  - send `removeBrand: true` when it is cleared;
  - otherwise send neither.
- If the product's current brand is inactive, show it in the picker as *"Pembe (inactive)"* so it is not silently lost.
- Hint under Name: *"Include the brand and size in the name, e.g. Soko Maize Meal 2kg. It is what prints on the receipt."*

### Product list

- Brand column (`brandName`, blank when none) and a Brand filter next to Category (`brandId`).

### New product form: prices and packs in the same step

Staff should not have to create a product, then open it again to price it. `POST /products/create-product` now also takes:

```ts
retailPrice?: number;      // price of one base unit (one packet), from now
wholesalePrice?: number;
packs?: Array<{            // e.g. Bale = 12 packets
  packUnitOfMeasureId: string;
  conversionFactor: number;  // base units in one pack
  retailPrice?: number;
  wholesalePrice?: number;
}>;
```

- Add a **Prices** block to the *create* form only, below Base unit:
  - *Retail price* and *Wholesale price* per base unit, labelled with the unit (*"Retail price per pcs"*).
  - **+ Add pack**: unit picker (excluding the base unit), *"How many {base unit} in one pack"*, retail and wholesale price.
- All price fields are optional. A product saved without prices works as before and is priced later on the Pricing tab.
- **Everything is saved together.** If any price or pack is refused, nothing is created, so a failed save can simply be retried. 400
  errors are keyed per field, e.g. `retailPrice`, `packs[0].conversionFactor`, `packs[1].packUnitOfMeasureId`. Show each under its field.
- **Permissions** (checked by the API, 403 otherwise):
  - Show the price fields only with `Products.ManagePricing`.
  - Show *Add pack* only with `Products.ManageUnitConversions`.
- The response is still `ProductDto` and does not include prices. Go to the product's detail page after saving, which loads them with
  `GET /products/{id}`.
- The **edit** form is unchanged: prices keep their own Pricing tab, because a price change is a new price from a date and past sales
  keep the old one.

### POS

- No change is needed for search: it already matches brand names. Optionally, show `brandName` small under the product name on the
  product tiles.

## Acceptance checks

- Create *Soko* and *Pembe*. Creating *soko* is refused with the message under the field.
- Create *Soko Maize Meal 2kg* with brand Soko. The list shows the brand, and filtering by Soko shows it.
- Edit the product's reorder level only: the brand is still Soko.
- Clear the brand and save: the product has no brand.
- Deactivate Pembe: it disappears from the picker for new products, and an existing Pembe product still shows *"Pembe (inactive)"*.
- Upload a product sheet with a new brand in column 10 on two rows: one brand appears in Catalog → Brands with 2 products.
- At the till, typing *"pembe"* finds Pembe products.
- Create *Soko Maize Meal 2kg* with retail 190 and a Bale of 12 at 2,200 in one save. The till sells it at once by the packet or by the
  bale.
- Create a product with a pack of conversion factor 0: the error shows under that pack, and no product is created.
- A user without `Products.ManagePricing` sees no price fields on the create form.
