# Super Users, Licensing and Business Access

How the platform layer works: the Super User role, licences, and the rules that decide whether a business can be used. This guide is for developers and the people running the platform.

## Two kinds of account

| | Business user | Super User |
|---|---|---|
| Belongs to | One or more businesses (`UserBusinesses`), with roles in each | **No business**, ever |
| Signs in to | A business; the token carries `business_id` | Nothing; `activeBusinessId` is `null` |
| Permissions | Business permissions from that business's roles | The 9 platform permissions, through the `Super User` role |
| Can see | Data of the business signed in to | Every business through `/api/v1/platform/*`, but never business data directly (business endpoints return 403) |

The separation is enforced in the API:
- A Super User account cannot be made an owner or admin of a business.
- An account that belongs to a business cannot be made a Super User.
- The bootstrapper refuses a business account.
- The `SuperUserAndLicensing` migration removed the platform role from everyone who belonged to a business.

**Platform permissions:** `Businesses.Register`, `Businesses.ViewAll`, `Businesses.Deactivate` (close or reopen), `Businesses.Suspend`, `Licenses.View`, `Licenses.Manage`, `Platform.Dashboard.View`, `Platform.Support`, `Platform.Users.Manage`.

## Getting the first Super User

- **Development:** the seeder creates `superuser@ultimatepos.local` / `SuperUser@12345`. `admin@ultimatepos.local` is a normal account and is Alvet's Admin.
- **Production:** set these environment variables (or user-secrets), start the API, sign in, change the password, then remove them:

```text
Platform__BootstrapSuperUser__Email=you@yourcompany.co.ke
Platform__BootstrapSuperUser__FullName=Your Name
Platform__BootstrapSuperUser__Password=<a strong temporary password>
```

The bootstrapper runs at every startup but does nothing once an active Super User exists. Without the settings, it logs a warning. Further Super Users are created from the platform area: `POST /platform/super-users`. The last active Super User cannot be deactivated.

## Onboarding a business

`POST /api/v1/businesses` (Super User) takes the business profile plus **`owner: { fullName, email, phoneNumber, password? }`**.

- **Owner email already exists** (a person running two businesses): that account becomes an Admin of the new business.
- **Owner email is new:** an account is created. Without a password, a temporary one is generated and returned **once** in `owner.temporaryPassword`. The owner must change it at first sign-in (`mustChangePassword: true` in the login response).
- The business gets its own `Admin` role (every business permission), default units and expense categories, and a **trial licence** of `Licensing:TrialDays` days.
- The Super User who registered it does **not** join it.

## Licences

Each licence is a period: `StartsOn` to `EndsOn` inclusive, with a kind (`Trial`, `Paid` or `Complimentary`), a plan name, an optional user limit, the amount and the payment reference. Licences are numbered `LIC-000001` and up within each business, and the rows form the business's licence history.

- **Issue or renew** with `POST /platform/businesses/{id}/licenses`, giving `months` or an `endsOn`. With no `startsOn`, a renewal starts the day after the current licence ends, so periods chain without a gap. Overlapping an active licence is refused.
- **Cancel** with `POST /platform/licenses/{id}/cancel` and a reason. Coverage is recalculated.
- **Coverage:** the business's licence end date (`LicenseEndsOn`) is the end of the unbroken run of periods covering today. The user limit is the limit of the period in force today. Both are refreshed whenever a licence changes, and by the hourly sweep, so a licence that starts in the future takes effect on its first day.
- **Alerts:** a business's admins (`Businesses.Update`) and Super Users (`Licenses.Manage`) are notified in-app and by email when a licence is within `ExpiryWarningDays` of ending, and again when it has ended.

## Access rules (`LicensePolicy`)

| Status | Access | What users see |
|---|---|---|
| `Active` | ✓ | — |
| `ExpiringSoon` (≤ 14 days left) | ✓ | Warning banner |
| `GracePeriod` (ended, ≤ 7 days ago) | ✓ | Warning banner with the days left |
| `Expired` | ✗ `LICENSE_EXPIRED` | "Renew it to continue" |
| `Unlicensed` (never licensed) | ✗ `UNLICENSED` | "Contact the Ultimate POS team" |
| `Suspended` | ✗ `BUSINESS_SUSPENDED` | The suspension reason |
| `Closed` | ✗ `BUSINESS_CLOSED` | — |

Where the rules are enforced:
- **At sign-in, token refresh and business switch.** If the user's default business is blocked, they are signed in to another business they can use; if none is usable, the refusal says why.
- **On every request**, by `BusinessAccessMiddleware`. The decision is cached for 60 seconds per business, and suspending, reactivating or changing a licence clears the cache, so these take effect immediately.

Refusals are HTTP 403 with the code above. Warnings travel in the `X-License-Warning` response header (exposed through CORS) and in `licenseWarning` on the login response. `/api/v1/auth/*` is never blocked, so users can always sign out or switch business.

**Suspend vs close:**
- **Suspend** is for non-payment and the like. It takes a reason, is reversed with **Reactivate**, and keeps all data.
- **Close** is for a business that has left the platform. It is reversed with **Reopen**, and also keeps all data.

## Configuration

```jsonc
"Licensing": {
  "Enforce": true,          // false = record and show licences, never block
  "GraceDays": 7,
  "TrialDays": 14,          // 0 = new businesses start unlicensed
  "ExpiryWarningDays": 14
},
"Platform": { "BootstrapSuperUser": { "Email": "", "FullName": "Super User", "Password": "" } }
```

## Migration notes

- `Platform Admin` was renamed to `Super User` and given all 9 platform permissions.
- Platform roles were removed from every business member. On the dev database this was `admin@ultimatepos.local`.
- Every existing business received a 30-day **Transitional** complimentary licence (`LIC-000001`), so enforcement did not lock anyone out on deploy. Replace it with the real licence from the platform area.

## Endpoints (`/api/v1/platform`)

| Area | Endpoints |
|---|---|
| Performance | `GET dashboard?from&to` |
| Businesses | `GET businesses`, `GET businesses/{id}`, `POST businesses/{id}/suspend`, `POST businesses/{id}/reactivate`, `POST businesses/{id}/close`, `POST businesses/{id}/reopen` |
| Licences | `GET businesses/{id}/licenses`, `POST businesses/{id}/licenses`, `POST licenses/{id}/cancel`, `GET licenses/expiring?withinDays` |
| Support | `GET businesses/{id}/users`, `POST businesses/{id}/admins`, `POST businesses/{id}/users/{userId}/reset-password`, `POST businesses/{id}/users/{userId}/unlock`, `GET audit-log?businessId` |
| Super Users | `GET super-users`, `POST super-users`, `POST super-users/{id}/activate`, `POST super-users/{id}/deactivate` |

Business-side additions:
- `POST /businesses` registers a business (Super User, with an owner).
- `GET /businesses/current/license` returns the business's own licence status and history (`Businesses.View`).
