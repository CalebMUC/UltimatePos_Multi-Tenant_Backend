# Frontend Task: Super User Area, Licences and Business Access

> **For:** the coding agent working on the Ultimate POS frontend.
> **Depends on:** the API's `SuperUserAndLicensing` release. Backend reference: `docs/PLATFORM.md`.
> Before you start, read `src/UltimatePos.Api/Controllers/PlatformController.cs`, `src/UltimatePos.Application/Platform/PlatformDtos.cs` and `src/UltimatePos.Application/Licensing/LicensePolicy.cs`. If the code and this prompt disagree, the code wins.

## Goal

1. A **Super User area** (`/platform/*`): a separate shell for the people who run the platform. They register businesses, watch performance, issue and renew licences, suspend and close businesses, support business users, and manage other Super Users.
2. **Business-side licence awareness**: a licence page in Settings, warning banners, and clear screens when a business is suspended, closed or out of licence.
3. **Forced password change** whenever someone else set the user's password.

## How the backend behaves

- **A Super User belongs to no business.** Their login returns `activeBusinessId: null`, `businesses: []`, and only platform permissions. They can't open any business screen (those return 403), so **don't show them the business app at all**. Route them to `/platform`.
- **A business user** never sees the platform area. Gate everything on the platform permissions.
- **Access refusals** are HTTP **403** with `error.code` set to one of these. Show a full-page blocking screen, not a toast:

| Code | Screen |
|---|---|
| `BUSINESS_SUSPENDED` | "This business is suspended", with `error.message` (includes the reason) |
| `BUSINESS_CLOSED` | "This business has been closed" |
| `LICENSE_EXPIRED` | "The licence has expired. Renew to continue" |
| `UNLICENSED` | "This business has no licence yet" |

  Every blocking screen offers **Switch business** (when the user has others), **Sign out**, and a contact line for the Ultimate POS team.

- These refusals can arrive **at login** and **on any request**. They come from the server middleware as soon as a business is suspended or its licence runs out.
- `USER_LIMIT_REACHED` (also 403) is not a blocking screen. It belongs as an error on the Add-user or Activate-user form.
- **Warnings** while still allowed (licence ending within 14 days, or in its 7-day grace period) arrive in:
  - `licenseWarning` on the login and refresh responses;
  - the `X-License-Warning` response header on any request (URL-encoded; decode it);
  - `GET /auth/me` → `businessAccess.message`.

  Show a dismissible amber banner under the header (red during the grace period), linking to the licence page. Dismissing hides it for the day; it returns tomorrow.
- **`mustChangePassword: true`** on login means someone else set the password: a new account, an admin reset, or a support reset. Route to a blocking **Change password** screen (`POST /auth/change-password`) before anything else. Treat `GET /auth/me` → `mustChangePassword` the same way.

Enums are numbers in JSON:

```ts
export enum LicenseKind { Trial = 0, Paid = 1, Complimentary = 2 }
export enum LicenseState { Active = 0, Cancelled = 1 }
export enum SettlementMethod { Cash = 0, Mpesa = 1, BankTransfer = 2, Cheque = 3 }
type AccessStatus = 'Active' | 'ExpiringSoon' | 'GracePeriod' | 'Expired' | 'Unlicensed' | 'Suspended' | 'Closed'; // strings
```

## Endpoints

All under `/api/v1`, using the standard envelope.

| Area | Method and path | Permission |
|---|---|---|
| Dashboard | `GET platform/dashboard?from&to&utcOffsetMinutes` (defaults to the last 30 days) | `Platform.Dashboard.View` |
| Businesses | `GET platform/businesses`, `GET platform/businesses/{id}` | `Businesses.ViewAll` |
| Register | `POST businesses` with `{ ...profile, owner: { fullName, email, phoneNumber?, password? } }` → `{ business, owner: { userId, email, fullName, created, temporaryPassword? }, trialLicense? }` | `Businesses.Register` |
| Suspend | `POST platform/businesses/{id}/suspend` `{ reason }` / `POST …/reactivate` | `Businesses.Suspend` |
| Close | `POST platform/businesses/{id}/close` `{ reason }` / `POST …/reopen` | `Businesses.Deactivate` |
| Licences | `GET platform/businesses/{id}/licenses`, `POST platform/businesses/{id}/licenses`, `POST platform/licenses/{licenseId}/cancel` `{ reason }`, `GET platform/licenses/expiring?withinDays=30` | `Licenses.View` / `Licenses.Manage` |
| Support | `GET platform/businesses/{id}/users`, `POST platform/businesses/{id}/admins` (owner body), `POST platform/businesses/{id}/users/{userId}/reset-password` → `{ user, temporaryPassword }`, `POST …/users/{userId}/unlock`, `GET platform/audit-log?businessId&…` | `Platform.Support` |
| Super Users | `GET platform/super-users`, `POST platform/super-users` `{ fullName, email, phoneNumber?, password? }` → `{ user, temporaryPassword? }`, `POST platform/super-users/{id}/activate`, `POST …/deactivate` | `Platform.Users.Manage` |
| Business side | `GET businesses/current/license` → `{ access, maxUsers, licenses[] }` | `Businesses.View` |

Request body for `IssueLicenseRequestDto`:

```ts
{ kind, planName, months?, startsOn?, endsOn?, maxUsers?, amount, paymentMethod?, paymentReference?, notes? }
```

- Give **either** `months` (1–36) **or** `endsOn`.
- Leave `startsOn` empty for a renewal: it continues the day after the current licence ends.
- A paid licence needs an amount, and a payment reference unless it was paid in cash.
- Overlapping an active licence returns `VALIDATION_FAILED` on `startsOn`.

## What to build

### 1. Routing and shell

- After login:
  - if `mustChangePassword`, go to `/change-password` (blocking);
  - else if the user has any platform permission and `activeBusinessId` is null, go to `/platform`;
  - else, the normal business app.
- **`/platform/*`** has its own layout: sidebar with Dashboard, Businesses, Licences, Super Users and Platform Audit Log; a header with the Super User's name, the notification bell (platform notifications land there, with links to `/platform/businesses/{id}`) and Sign out. No business switcher.
- The business app's sidebar never shows platform items.

### 2. Platform dashboard (`/platform`)

- A period picker (default: last 30 days).
- Stat cards: businesses; active; expiring soon; in grace; expired / unlicensed; suspended; closed; sales; revenue; licence income.
- A table with one row per business: name, kind/type, **access status badge**, licence ends on (with days left), active users, products, sales, revenue, average sale, last sale, last sign-in. Sortable, searchable on the client, with CSV export.
  - Status colours: Active green, ExpiringSoon amber, GracePeriod orange, Expired/Unlicensed red, Suspended red outline, Closed grey.
- A "Needs attention" list from `GET platform/licenses/expiring?withinDays=30`, with a **Renew** action on each row.

### 3. Businesses (`/platform/businesses`, `/platform/businesses/:id`)

- **List:** the same status badges, a status filter, and **Register business**.
- **Register wizard:**
  1. Business profile: name, trading name, type, **kind** (Manufacturer or Trader, fixed once created), KRA PIN, VAT-registered, M-Pesa short code, discount cap, contact details.
  2. Owner: name, email, phone, and an optional password.
  3. Review.

  On success, show a **one-time panel** with the owner's email and the `temporaryPassword` (copy button; "this won't be shown again"), plus the trial end date. If `owner.created` is false, say "Added to an existing account" and show no password.
- **Business detail:**
  - Header: name, status badge, licence end date, and actions **Suspend / Reactivate** and **Close / Reopen**. Each needs a confirmation dialog with a required reason, and the dialog spells out the effect ("Its N users will be signed out and refused").
  - **Overview tab:** last-30-days performance and the profile.
  - **Licences tab:** history with number, kind, plan, period, users, amount, reference, and a cancelled state. Actions **Issue / Renew licence** (form as above; show the computed period before saving) and **Cancel** (needs a reason).
  - **Users tab** (`Platform.Support`): members with roles and status. Row actions: **Reset password** (one-time temporary password panel) and **Unlock**. Page action: **Add administrator** (owner form).
  - **Activity tab:** `GET platform/audit-log?businessId={id}`, reusing the audit-log table from the notifications task.

### 4. Licences (`/platform/licenses`)

- The expiring-licences list with a `withinDays` filter (7/30/60/90), each row linking to its business's Licences tab.

### 5. Super Users (`/platform/super-users`)

- **List and create:** the create dialog takes name, email, phone and an optional password, then shows the one-time temporary password panel.
- **Activate / deactivate.** The API refuses to deactivate yourself or the last active Super User; show its message.
- The email must be new. The API refuses an email that belongs to a business account, so show that as a field error.

### 6. Business side

- **`/settings/license`** (`Businesses.View`): status card (status badge, ends on, days left, user limit), the history table (read-only), and "To renew, contact the Ultimate POS team".
- **Licence banner:** shown in the business app shell from `licenseWarning`, the `X-License-Warning` header or `me.businessAccess`.
- **Blocking screens:** for the four access codes. Catch them globally in the API client: a 403 whose `error.code` is one of them redirects to `/blocked?code=…`.
- **Business picker:** `businesses[].accessStatus` comes with the login response. Show the badge, and disable businesses that are blocked, with the reason on hover.
- **Users page:** handle `USER_LIMIT_REACHED` on Add and Activate, and show "N of M users" when the licence has a limit (`maxUsers` from `/businesses/current/license`).

### 7. Change password (`/change-password`)

- Fields: current password, new password, confirm. Rules: at least 8 characters, with a letter and a digit.
- On success, continue to where the user was going.
- The page can't be skipped while `mustChangePassword` is true.

## Not in scope

- Online payment of licences, invoices or billing integration. Licences are recorded by Super Users after payment.
- Impersonating a business user. Support works only through the endpoints above.
- Per-plan pricing configuration. The plan is free text.

## Tests

- **Unit:** the access-code → screen mapping, status badge colours, decoding `X-License-Warning`, and the licence form's "computed period" preview (`months` versus `endsOn`, renewal start).
- **Component, with MSW:**
  - the register wizard shows the temporary password once;
  - suspend requires a reason;
  - a 403 `BUSINESS_SUSPENDED` from any query lands on the blocking screen;
  - the banner appears from the header and dismisses for the day;
  - `mustChangePassword` blocks navigation.
- **E2E**, against a dev API (Super User `superuser@ultimatepos.local` / `SuperUser@12345`):
  - register a business with a new owner;
  - sign in as the owner with the temporary password, and be forced to change it;
  - as the Super User, suspend the business: the owner's next request shows the blocking screen;
  - reactivate it, then issue a 12-month licence and see the end date extend.

## Done when

- Super Users and business users each land in their own app, and neither can reach the other's screens.
- Every platform action works against the real API with confirmations, one-time password panels and error states.
- Blocking screens, warning banners and the forced password change behave as described, from login and from any request.
- The tests above pass.
