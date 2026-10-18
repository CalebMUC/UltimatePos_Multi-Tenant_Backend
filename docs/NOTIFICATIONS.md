# Notifications, Audit Log, Email and SMS

How the API records system events, turns them into notifications, and sends email and SMS. This guide is for developers working on the backend.

## How it works

```text
Service (e.g. SalesService.VoidSaleAsync)
   └─ ISystemEventRecorder.Record(...)       → SystemEvents row, saved in the SAME transaction as the change (outbox)
                                               = the audit log
NotificationDispatchWorker (every 5 s)
   └─ NotificationDispatcher                 → reads unprocessed events, applies NotificationRules:
         • alert de-duplication (AlertStates)    one open alert per condition, e.g. "stock-low:{productId}"
         • recipients by permission              in the event's business only (or through a platform role)
         • Notifications rows                    the in-app bell
         • NotificationDeliveries rows           email / SMS, queued
   └─ DeliveryProcessor                      → sends due deliveries through IEmailSender / ISmsSender,
                                               retrying after 1, 5, 15 and 60 minutes, up to MaxDeliveryAttempts
AlertSweepWorker (every 60 min, per business)
   └─ AlertSweepService                      → state alerts: low stock, batch expiring or expired,
                                               PO overdue, till payment unmatched; closes alerts that have cleared
```

- **Code:** `src/UltimatePos.Application/Notifications/` holds the contracts, rules, dispatcher, sweep and services. `src/UltimatePos.Infrastructure/Notifications/` holds the recorder, repositories and senders. `src/UltimatePos.Api/Background/` holds the two workers.
- **Low stock is raised twice over, on purpose.** `StockLedger` records an event the moment a movement crosses the reorder level, and the sweep catches everything else. Alert de-duplication means people are told only once.

## Adding an event

1. Add a constant to `SystemEventTypes`.
2. Call `_events.Record(new SystemEventDraft(...))` in the service, **before** the repository saves, so the event commits with the change. Use `RecordNowAsync` only when the change has already been saved or there is no change to save (a sign-out).
3. To notify people, add a `NotificationRule` in `NotificationRules`: a title, the permissions that receive it, channels, and a link. With no rule, the event goes to the audit log only.

## Configuration

```jsonc
"Notifications": {
  "EmailEnabled": false,          // queue email deliveries at all
  "SmsEnabled": false,            // queue SMS deliveries at all
  "FrontendBaseUrl": "",          // for links in emails, e.g. https://pos.example.co.ke
  "ExpiryWarningDays": 30,
  "TillUnmatchedMinutes": 30,
  "MaxDeliveryAttempts": 5,
  "WorkersEnabled": true,         // run the dispatcher and sweep in this process (one instance only)
  "DispatchIntervalSeconds": 5,
  "SweepIntervalMinutes": 60
},
"Email": { "Provider": "Log", ... },   // "Log" or "Smtp"
"Sms":   { "Provider": "Log", ... }    // "Log" only, for now
```

- **Development** turns both email and SMS on, with the `Log` providers. Messages are written to the API log instead of being sent: search the log for `EMAIL (not sent` and `SMS (not sent`.
- **Production** ships with email and SMS off until a provider is configured.

## Sending real email (SMTP)

Any SMTP server or relay works, for example Office 365, Google Workspace, SendGrid, Mailgun or Amazon SES. Set:

```jsonc
"Notifications": { "EmailEnabled": true, "FrontendBaseUrl": "https://pos.example.co.ke" },
"Email": {
  "Provider": "Smtp",
  "FromAddress": "alerts@example.co.ke",
  "FromName": "Ultimate POS",
  "Smtp": { "Host": "smtp.office365.com", "Port": 587, "Security": "StartTls", "Username": "alerts@example.co.ke", "Password": "" }
}
```

`Security` can be `StartTls` (the default, used with port 587), `SslOnConnect` (port 465) or `None`. Use `None` only for a local relay.

**Never put the password in appsettings.** Use user-secrets locally:

```sh
dotnet user-secrets set "Email:Smtp:Password" "..." --project src/UltimatePos.Api
```

In production, use the `Email__Smtp__Password` environment variable or a vault.

The sender is `SmtpEmailSender` (MailKit). A rejected recipient fails at once; any other error is retried.

## Adding Africa's Talking (SMS)

The configuration shape is already in place, under `Sms:AfricasTalking`: `Username`, `ApiKey` and `BaseUrl`, plus `Sms:SenderId`. To add the integration:

1. Implement `ISmsSender` as `AfricasTalkingSmsSender` in `Infrastructure/Notifications/`:
   - `POST {BaseUrl}/version1/messaging`, form-encoded with `username`, `to=+{PhoneNumber}`, `message`, and `from={SenderId}` if set;
   - headers `apiKey: {ApiKey}` and `Accept: application/json`;
   - success is a recipient status of `Success`; return its `messageId` as the provider message id;
   - treat an invalid phone number as `retryable: false`, and anything else as retryable.
2. In `NotificationChannelRegistration`, map `"AfricasTalking"` to it. Register it with `AddHttpClient<ISmsSender, AfricasTalkingSmsSender>()`, which is how the M-Pesa client is registered.
3. Test against the sandbox first (username `sandbox`, `https://api.sandbox.africastalking.com`), then switch `BaseUrl` to `https://api.africastalking.com`.
4. Set `Notifications:SmsEnabled` to `true`. Keep the API key in user-secrets or an environment variable.

SMS bodies are kept to one 160-character segment. Phone numbers are stored in `2547XXXXXXXX` form, from user profiles.

Today the only messages sent by SMS are to the user concerned, when their account is locked or their password is reset. To add more, set `NotificationChannels.Sms` on a rule.

## Endpoints

| Endpoint | Who |
|---|---|
| `GET /api/v1/notifications`, `GET /notifications/unread-count`, `POST /notifications/{id}/read`, `POST /notifications/read-all` | Any signed-in user, for their own notifications |
| `GET /api/v1/audit-log`, `GET /audit-log/event-types` | `AuditLog.View` |

## Not built yet (phases 2 and 3)

- **Real-time push** with SignalR. The frontend polls the unread count until then.
- **Per-user preferences**, such as muting an event type or choosing email over in-app.
- **SMS through a real provider**, Africa's Talking as described above.
- **A dashboard for failed deliveries.** Until then, query `NotificationDeliveries` where `Status = 'Failed'`.
- **Running several API instances.** Set `Notifications:WorkersEnabled` to `false` on all but one instance until the workers coordinate with a lock.
