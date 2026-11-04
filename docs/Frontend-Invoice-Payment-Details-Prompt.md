# Frontend Task: Invoice Payment Details (bank, branch, account, paybill, till)

> **For:** the coding agent working on the Ultimate POS frontend (`Ultimate_Multi_Tenant_Frontend`).
> **Depends on:** the API release with migration `BusinessInvoicePaymentDetails`.
> Before you start, read `InvoiceSettingsDto` in `src/UltimatePos.Application/Business/Dtos/BusinessDtos.cs` and
> `CheckInvoiceSettings` in `src/UltimatePos.Application/Business/BusinessService.cs`. If the code and this prompt disagree, the code wins.

## Why

Many businesses do not have an M-Pesa till of their own, or want credit customers to pay big invoices into the bank. The invoice must
say **where to send the money**: the bank, its branch, the account name and number, the bank's paybill, a Buy Goods till, or a phone to
send M-Pesa to. These details are saved on the business, so every device prints the same invoice.

These details are **printed only**. They are not the M-Pesa till or paybill on the business profile (`mpesaTillNumber`,
`mpesaPaybillNumber`), which route payments into the POS. Do not merge the two sets of fields, and do not prefill one from the other.

## The data

`GET /api/v1/businesses/current` → `BusinessDto` now has `invoice`. Every field can be null, and a null field is left off the invoice.

```jsonc
"invoice": {
  "bankName": "Equity Bank",            // ≤ 100 characters
  "bankBranch": "Ruiru",                // ≤ 100
  "bankPaybillNumber": "247247",        // 5–10 digits
  "bankAccountName": "Jirani Minimart", // ≤ 100: the name the account is held in
  "bankAccountNumber": "0123456789",    // ≤ 50: also the "Account" for the bank paybill
  "tillNumber": "5123456",              // 5–7 digits, Buy Goods
  "sendMoneyPhoneNumber": "254712345678", // stored as 2547XXXXXXXX / 2541XXXXXXXX
  "dueDays": 14,                        // 0–365; null = no due date
  "note": "Thank you for your business." // ≤ 500, printed at the foot
}
```

## API

### 1. Business settings: `PUT /api/v1/businesses/current/invoice-settings` (`Businesses.Update`)

The body is the `invoice` object above, and the call returns the updated `BusinessDto`. It is a **full replace**: send every field, because
a blank or missing field is cleared. The API trims spaces, turns blank strings into null and normalises the phone (`0712 345 678` →
`254712345678`).

A 400 has `details` keyed by field: `bankName`, `bankBranch`, `bankPaybillNumber`, `bankAccountName`, `bankAccountNumber`, `tillNumber`,
`sendMoneyPhoneNumber`, `dueDays`, `note`. Show each message under its field.

### 2. Onboarding: `POST /api/v1/businesses` (Super User, `Businesses.Register`)

The register body accepts an optional `invoice` object, the same shape and the same validation. Leave it out, or send `null`, when the
owner does not have the details yet; they can add them later in Settings. A 400 for these fields uses the same keys as above.

```jsonc
{
  // ...the profile and owner fields, unchanged...
  "mpesaTillNumber": "000333",
  "invoice": { "bankName": "Equity Bank", "bankBranch": "Ruiru", "bankPaybillNumber": "247247",
               "bankAccountName": "Jirani Minimart", "bankAccountNumber": "0123456789",
               "tillNumber": null, "sendMoneyPhoneNumber": null, "dueDays": 7, "note": null }
}
```

## Screens

### 1. Super User → Register business wizard

Add an optional step, **"Payment details (for invoices)"**, after the M-Pesa till/paybill fields:

| Field | Input | Notes |
|---|---|---|
| Bank name | text, or a combo box of common Kenyan banks with free text allowed | Equity, KCB, Co-op, Family, NCBA, Absa, Stanbic, I&M, DTB, Standard Chartered |
| Branch | text | |
| Account name | text | Hint: "As it appears on the bank account" |
| Account number | text | Do not strip leading zeros: keep it a string |
| Bank paybill | digits | Hint: "The bank's M-Pesa paybill, e.g. Equity 247247" |
| Till number (Buy Goods) | digits | |
| Send-money phone | phone | |
| Days to pay | number 0–365 | Empty = no due date |
| Invoice note | textarea, 500 max, with a counter | |

- The whole step is skippable ("Add later"). If every field is empty, send `invoice: null`.
- Prefill nothing. Show the API's 400 messages on this step, not as a generic toast.
- The review step lists what was entered under **"Send payment to"**.

### 2. Settings → Invoices (`Businesses.Update`; read-only with `Businesses.View`)

A card titled **"Send payment to"**, with the same fields as the wizard plus *Days to pay* and *Invoice note*.

- Load from `business.invoice`. Save with `PUT /businesses/current/invoice-settings`, sending all nine fields, then refresh the current
  business in the store.
- On the right, a **live preview** of the invoice block (below) built from the form values.
- A *Clear all* button sends every field as null, after a confirmation.

### 3. The printed invoice and the customer statement

Wherever the app prints or shares an invoice (a credit sale, a customer statement, a PDF or WhatsApp share), add a **"Send payment to"**
block. Print only the lines whose fields are set, in this order:

```
SEND PAYMENT TO
Bank:          Equity Bank, Ruiru branch
Account name:  Jirani Minimart
Account no.:   0123456789
M-Pesa Paybill 247247, Account 0123456789
Buy Goods Till 5123456
Send Money     0712 345 678
Due by 18 Nov 2026
Thank you for your business.
```

- **Paybill line:** when both `bankPaybillNumber` and `bankAccountNumber` are set, print "Paybill X, Account Y". With a paybill and no
  account number, print the paybill alone.
- **Phone:** print in local form, `0712 345 678`.
- **Due date:** use the sale's `paymentDueDate` (the customer's promise) when it is set. Otherwise use the invoice date plus `dueDays`.
  When both are null, print no due line.
- If `invoice` is null or every payment field is null, leave the block off completely. Never print empty labels.
- **80 mm thermal paper** (see `docs/Frontend-Thermal-Printing-Prompt.md`): the same lines, wrapped to the paper width, with a
  `--------` rule above. Long account names wrap; they are never cut short.
- Cash receipts for sales already paid do **not** get this block. Only invoices and statements with a balance do.

## Tests to add

- Wizard: skipping the step sends `invoice: null`; filling it sends the nine fields; 400 `details.tillNumber` (and the others) show
  under their fields.
- Settings: Save sends all nine fields, including nulls for blanks; the preview hides empty lines; the read-only view without
  `Businesses.Update`.
- Invoice print: only set lines appear; the paybill line with and without an account number; the phone in local form; the due date from
  `paymentDueDate` first, then from `dueDays`, then none; the block is absent when `invoice` is null.
