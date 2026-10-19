using System.Net;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Notifications
{
    /// <summary>
    /// Turns recorded events into notifications. Runs in the background with nobody signed in: for each unprocessed event it closes
    /// and opens alerts, finds the recipients by permission, writes their in-app notifications and queues email/SMS deliveries —
    /// then marks the event processed, all in one transaction. A failing event is retried a few times, then left for inspection.
    /// </summary>
    public class NotificationDispatcher
    {
        public const int MaxEventAttempts = 5;

        private readonly INotificationRepository _repository;
        private readonly NotificationSettings _settings;

        public NotificationDispatcher(INotificationRepository repository, NotificationSettings settings)
        {
            _repository = repository;
            _settings = settings;
        }

        /// <returns>How many events were handled.</returns>
        public async Task<int> DispatchPendingAsync(int batchSize = 100, CancellationToken cancellationToken = default)
        {
            var events = await _repository.GetUnprocessedEventsAsync(batchSize, MaxEventAttempts);
            foreach (var systemEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await DispatchAsync(systemEvent);
                }
                catch (Exception ex)
                {
                    await _repository.MarkEventFailedAsync(systemEvent.SystemEventId, ex.Message);
                }
            }
            return events.Count;
        }

        private async Task DispatchAsync(SystemEvent systemEvent)
        {
            var closes = (systemEvent.ClearsAlertKeys ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            // A condition that is already announced and still open notifies nobody again.
            string? opens = null;
            if (systemEvent.AlertKey is { } key && systemEvent.BusinessId is { } alertBusiness)
            {
                if (await _repository.IsAlertOpenAsync(alertBusiness, key))
                {
                    await _repository.CompleteEventAsync(systemEvent, Array.Empty<Notification>(), Array.Empty<NotificationDelivery>(), null, closes);
                    return;
                }
                opens = key;
            }

            var rule = NotificationRules.For(systemEvent.EventType);
            if (rule is null) // audit only
            {
                await _repository.CompleteEventAsync(systemEvent, Array.Empty<Notification>(), Array.Empty<NotificationDelivery>(), opens, closes);
                return;
            }

            // Who: permission-holders, then the user the event is about. One entry per user, with the channels they get.
            var targets = new Dictionary<Guid, (NotificationRecipient Recipient, NotificationChannels Channels)>();
            if (rule.RecipientPermissions.Count > 0)
            {
                foreach (var recipient in await _repository.GetRecipientsAsync(systemEvent.BusinessId, rule.RecipientPermissions, systemEvent.BranchId))
                {
                    if (rule.ExcludeActor && recipient.UserId == systemEvent.ActorUserId)
                        continue;
                    targets[recipient.UserId] = (recipient, rule.Channels);
                }
            }
            if (rule.SubjectChannels != NotificationChannels.None && systemEvent.SubjectUserId is { } subjectId)
            {
                var subject = await _repository.GetRecipientAsync(subjectId, systemEvent.BusinessId);
                if (subject is not null)
                    targets[subjectId] = (subject, (targets.TryGetValue(subjectId, out var t) ? t.Channels : NotificationChannels.None) | rule.SubjectChannels);
            }

            var link = rule.Link?.Replace("{entityId}", systemEvent.EntityId?.ToString() ?? string.Empty);
            var businessName = systemEvent.BusinessId is { } b ? await _repository.GetBusinessNameAsync(b) : null;

            var notifications = new List<Notification>();
            var deliveries = new List<NotificationDelivery>();
            foreach (var (recipient, channels) in targets.Values)
            {
                Guid? notificationId = null;
                if (channels.HasFlag(NotificationChannels.InApp))
                {
                    // A super user belongs to no business: their copy is a platform notification, linking to the business in the
                    // platform area, so it shows in their bell and opens somewhere they can go.
                    var platformCopy = recipient.ViaPlatformRole && systemEvent.BusinessId is not null;
                    var notification = new Notification
                    {
                        NotificationId = Guid.NewGuid(),
                        BusinessId = platformCopy ? null : systemEvent.BusinessId,
                        UserId = recipient.UserId,
                        SystemEventId = systemEvent.SystemEventId,
                        EventType = systemEvent.EventType,
                        Severity = systemEvent.Severity,
                        Title = rule.Title,
                        Message = businessName is null || !platformCopy ? systemEvent.Summary : $"{businessName}: {systemEvent.Summary}",
                        Link = platformCopy ? $"/platform/businesses/{systemEvent.BusinessId}" : link,
                    };
                    notifications.Add(notification);
                    notificationId = notification.NotificationId;
                }

                if (channels.HasFlag(NotificationChannels.Email) && _settings.EmailEnabled && !string.IsNullOrWhiteSpace(recipient.Email))
                    deliveries.Add(EmailDelivery(systemEvent, rule, recipient, notificationId, businessName, link));

                if (channels.HasFlag(NotificationChannels.Sms) && _settings.SmsEnabled && !string.IsNullOrWhiteSpace(recipient.PhoneNumber))
                    deliveries.Add(new NotificationDelivery
                    {
                        NotificationDeliveryId = Guid.NewGuid(),
                        BusinessId = systemEvent.BusinessId,
                        NotificationId = notificationId,
                        SystemEventId = systemEvent.SystemEventId,
                        UserId = recipient.UserId,
                        Channel = DeliveryChannel.Sms,
                        Recipient = recipient.PhoneNumber!,
                        Body = SmsText(businessName, rule.Title, systemEvent.Summary),
                    });
            }

            await _repository.CompleteEventAsync(systemEvent, notifications, deliveries, opens, closes);
        }

        private NotificationDelivery EmailDelivery(SystemEvent systemEvent, NotificationRule rule, NotificationRecipient recipient,
            Guid? notificationId, string? businessName, string? link)
        {
            var from = businessName ?? "Ultimate POS";
            var url = link is not null && !string.IsNullOrWhiteSpace(_settings.FrontendBaseUrl)
                ? _settings.FrontendBaseUrl!.TrimEnd('/') + link
                : null;

            var text = $"Hello {recipient.Name},\n\n{systemEvent.Summary}\n" +
                       (url is null ? string.Empty : $"\nOpen it here: {url}\n") +
                       $"\n— {from}, via Ultimate POS\n";
            var html = $"<p>Hello {WebUtility.HtmlEncode(recipient.Name)},</p><p>{WebUtility.HtmlEncode(systemEvent.Summary)}</p>" +
                       (url is null ? string.Empty : $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">Open in Ultimate POS</a></p>") +
                       $"<p style=\"color:#666\">— {WebUtility.HtmlEncode(from)}, via Ultimate POS</p>";

            return new NotificationDelivery
            {
                NotificationDeliveryId = Guid.NewGuid(),
                BusinessId = systemEvent.BusinessId,
                NotificationId = notificationId,
                SystemEventId = systemEvent.SystemEventId,
                UserId = recipient.UserId,
                Channel = DeliveryChannel.Email,
                Recipient = recipient.Email!,
                Subject = $"[{from}] {rule.Title}",
                Body = text,
                HtmlBody = html,
            };
        }

        /// <summary>One SMS segment where possible: 160 characters.</summary>
        private static string SmsText(string? businessName, string title, string summary)
        {
            var text = $"{businessName ?? "Ultimate POS"}: {title}. {summary}";
            return text.Length <= 160 ? text : text[..157] + "...";
        }
    }
}
