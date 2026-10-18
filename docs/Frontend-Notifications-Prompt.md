# Frontend Task: Notifications Bell and Audit Log

> **For:** the coding agent working on the Ultimate POS frontend.
> **Depends on:** the API's `AddNotificationsAndAuditLog` release. The main build prompt (`docs/Ultimate-Pos-Frontend_Prompt.md`, §14.8) has the reference version of this spec.

## Goal

Add an in-app **notification bell**, a **Notifications page**, an **Audit Log page** for administrators, and an **Activity tab** on the main detail pages. Before you start, read `src/UltimatePos.Api/Controllers/NotificationsController.cs` and `src/UltimatePos.Application/Notifications/NotificationService.cs`. If the code and this prompt disagree, the code wins.

## How the backend behaves (do not re-implement it)

- The API records significant actions as **events**. A background worker turns relevant events into **notifications** for the users responsible, chosen by permission, within about **5 seconds**. A notification therefore appears shortly *after* an action, never in the action's response.
- Notifications belong to **one user in one business**, plus a few platform notifications (`businessId: null`). Lists already come filtered by the signed-in user and the current business, so the frontend never filters by business.
- Condition alerts (low stock, expiry, overdue order, unmatched till payment) are sent **once**, until the condition clears.
- There is **no push channel yet**. Poll for now, and keep polling behind one hook so SignalR can replace it later.

## Endpoints

All use the standard envelope `{ success, data, error, meta }`. Paged lists put `{ page, pageSize, totalCount, totalPages }` in `meta`.

| Method and path | Auth | Returns |
|---|---|---|
| `GET /api/v1/notifications/unread-count` | Signed in | `{ unreadCount: number }` |
| `GET /api/v1/notifications?unreadOnly=false&page=1&pageSize=20` | Signed in | `NotificationDto[]`, paged (`pageSize` ≤ 100) |
| `POST /api/v1/notifications/{id}/read` | Signed in | `{}`; 404 if the notification isn't the user's |
| `POST /api/v1/notifications/read-all` | Signed in | `{ marked: number }` |
| `GET /api/v1/audit-log?…` | `AuditLog.View` | `AuditLogEntryDto[]`, paged (`pageSize` ≤ 200) |
| `GET /api/v1/audit-log/event-types` | `AuditLog.View` | `string[]`, for the filter dropdown |

Audit-log query parameters:
- `page`, `pageSize`;
- `from`, `to`: `yyyy-MM-dd` local days, inclusive, together with `utcOffsetMinutes` (send `-new Date().getTimezoneOffset()`);
- `eventType`: an exact type such as `Sale.Voided`, or a whole area such as `Sale`;
- `actorUserId`, `entityType`, `entityId`;
- `minimumSeverity`: `Info`, `Warning` or `Critical`;
- `search`: up to 100 characters, matched against the summary.

```ts
export enum EventSeverity { Info = 0, Warning = 1, Critical = 2 }   // arrives as a NUMBER, like every body enum

interface NotificationDto {
  notificationId: string; eventType: string; severity: EventSeverity;
  title: string; message: string; link: string | null;   // link = frontend path, e.g. "/sales/{id}"
  isRead: boolean; readAt: string | null; createdAt: string; businessId: string | null;
}

interface AuditLogEntryDto {
  systemEventId: string; eventType: string; severity: EventSeverity; summary: string;
  actorUserId: string | null; actorName: string | null;     // null actor = the system (sweep, webhook)
  subjectUserId: string | null; subjectName: string | null; // the user the event is about
  entityType: string | null; entityId: string | null;       // "Product" | "Sale" | "User" | "StockBatch" | "PurchaseOrder" | ...
  data: string | null;                                      // JSON text: parse it for display
  occurredAt: string;                                       // UTC
}
```

`severity` is a number in the JSON (`0` Info, `1` Warning, `2` Critical). Query strings accept the name (`minimumSeverity=Warning`) or the number.

## What to build

### 1. `useNotifications()` hook (`features/notifications/`)

- Query `['notifications', businessId, 'unread-count']`. Poll **every 30 s** while the tab is visible, refetch on window focus, and stop polling when the tab is hidden.
- Query `['notifications', businessId, 'list', { unreadOnly, page }]`.
- Mutations: `markRead(id)` and `markAllRead()`. Update optimistically (decrement the badge, set `isRead`), then invalidate the `['notifications', businessId]` prefix.
- **New-Critical toast:** remember the newest `createdAt` seen. When a poll brings Critical notifications newer than that, show one toast per notification (at most 3), with its title and message and an "Open" action.
- On a business switch the whole query cache is already cleared, so make sure every key includes `businessId`.
- Keep all API calls in `api/endpoints/notifications.ts` and `api/endpoints/auditLog.ts`, with DTO types in `api/dto/`.

### 2. Header bell

- A bell icon with an unread badge, shown as `99+` above 99 and hidden at 0.
- Clicking it opens a popover with the **latest 10 notifications**, both read and unread:
  - each row shows a severity icon and colour (Critical red, Warning amber, Info neutral), the title, a one-line message, and relative time ("5 min ago");
  - unread rows are bold and carry a dot.
- Clicking a row marks it read, closes the popover and navigates to `link` (if it has one).
- The popover footer has **Mark all as read** and **View all** (which goes to `/notifications`).
- **Empty state:** "You're all caught up."
- **Loading:** a skeleton. **Error:** a small inline "Couldn't load notifications · Retry".
- **Accessibility:** a button with `aria-label="Notifications, N unread"`, and an `aria-live="polite"` region announcing new unread counts.

### 3. `/notifications` page (every signed-in user)

- Tabs: **All** and **Unread**. A paged list with the same row design, plus the full message and an absolute timestamp on hover.
- Row actions: open (marks it read and follows the link), mark read.
- Page action: **Mark all as read**.
- Add the route to the sidebar config with no permission requirement.

### 4. `/admin/audit-log` page (`AuditLog.View`)

- **Filter bar:** date range (default: last 7 days), event type (a combobox from `/audit-log/event-types` grouped by area, the part before the dot; picking an area header sends just the area), user (a `GET /users` combobox that sends `actorUserId`), minimum severity, and search (debounced 300 ms).
- **Table, server-paged, newest first.** Columns: Time (local), Severity badge, Event (readable label: `Sale.Voided` → "Sale voided"), Who (`actorName`, or "System"), Summary.
- **Expand a row** to show the subject user, the entity with a link built from `entityType`/`entityId`, and `data` pretty-printed as JSON in a monospace block.
- Export the current page to CSV on the client.
- Add a sidebar item under Administration, gated by `AuditLog.View`.

### 5. Activity tab (reusable `<ActivityTimeline entityType entityId />`)

- Uses `GET /audit-log?entityType=…&entityId=…&pageSize=20`, with a "Load more" button. Render a vertical timeline: time, who, summary, and a severity dot.
- Add it to the **Product**, **Sale**, **User**, **Supplier** and **Purchase Order** detail pages, but only when the user has `AuditLog.View`. Otherwise hide the tab.

Entity-to-route map, used for audit links. Notification links come ready-made from the API.

| `entityType` | Route |
|---|---|
| `Product` | `/products/{id}` |
| `Sale` | `/sales/{id}` |
| `User` | `/users/{id}` |
| `Supplier` | `/suppliers/{id}` |
| `PurchaseOrder` | `/purchase-orders/{id}` |
| `StockBatch` | `/inventory/batches?batchId={id}` |
| `Customer` | `/customers/{id}` |
| `Expense` | `/expenses/{id}` |
| `Role` | `/roles/{id}` |
| `MpesaTransaction` | `/mpesa` |

### 6. Make sure every notification link resolves

The API sends these `link` values. Each route must exist, and must apply its query parameter as a filter or selection:

- `/inventory?productId=…`
- `/inventory/batches?batchId=…`
- `/inventory/valuation`
- `/purchase-orders/{id}`
- `/sales/{id}`
- `/mpesa`
- `/expenses/{id}`
- `/users/{id}`
- `/roles/{id}`

A link to a record the user can't open (404 or 403) must show the normal not-found or forbidden state, never a crash.

## What users will receive (for copy and empty states)

| Event | Who receives it |
|---|---|
| Low stock / Out of stock | Users with `PurchaseOrders.Create` or `Stock.Adjust` (out of stock also by email) |
| Batch expiring (≤ 30 days) / Batch expired | `Stock.Adjust` (expired also by email) |
| Purchase order overdue | `PurchaseOrders.Receive` / `PurchaseOrders.Create` |
| Unmatched M-Pesa till payment (> 30 min) | `Mpesa.View` |
| M-Pesa STK payment failed | The cashier who made the sale |
| Sale voided, credit limit overridden, expense voided, stock revalued | The matching manager permission, never the person who did it |
| New user / user deactivated / role permissions changed | `Users.View` / `Roles.View` |
| Account locked | `Users.Lock` (the locked user is emailed and texted) |
| Your access changed | The user whose roles changed |

Email and SMS are sent by the backend. **No email or SMS UI is needed in this task.**

## Not in scope

- Real-time push (SignalR). Polling is the design for now; just keep it inside `useNotifications`.
- Per-user notification preferences, such as muting types or choosing channels. There is no API for these yet.
- Any email, SMS or provider configuration screens.

## Tests

- **Unit:** the severity mapper, the event-type label formatter (`Stock.BelowReorderLevel` → "Stock below reorder level"), the area grouping for the filter, and the link-building map.
- **Component, with MSW:**
  - the bell badge (0, 5, 120 → `99+`);
  - opening the popover and clicking a row marks it read and navigates;
  - "Mark all as read" updates the badge optimistically and rolls back if the request fails;
  - a new Critical notification arriving in a poll shows a toast once, not again on the next poll;
  - the audit-log filters produce the right query string;
  - expanding a row pretty-prints `data`.
- **E2E**, against a dev API:
  - as a user with `PurchaseOrders.Create`, sell a product below its reorder level. Within about 10 s the badge increments, and the notification opens `/inventory?productId=…`.
  - Open the audit log and find the sale. Switch business and confirm the notification and log entry are gone.

## Done when

- The bell, `/notifications`, `/admin/audit-log` and the Activity tabs work against the real API, with loading, empty and error states.
- The badge stays correct across mark-read, mark-all and business switches.
- Polling pauses while the tab is hidden.
- Every notification link lands on a working screen.
- Everything is permission-gated (`AuditLog.View`). The bell and the notifications page are open to every signed-in user.
- The tests above pass.
