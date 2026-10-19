using UltimatePos.Application.Notifications;
using UltimatePos.Domain.Entities;

namespace Ultimate.Api.Tests;

/// <summary>Retry and give-up rules for email/SMS deliveries, with a provider that fails on demand.</summary>
public class DeliveryProcessorTests
{
    private sealed class FlakyEmail : IEmailSender
    {
        public Queue<ChannelSendResult> Results { get; } = new();
        public string ProviderName => "Flaky";
        public Task<ChannelSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Dequeue());
    }

    private sealed class NoSms : ISmsSender
    {
        public string ProviderName => "None";
        public Task<ChannelSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no SMS in this test");
    }

    /// <summary>Only the two members the processor uses; the rest is never called.</summary>
    private sealed class OneDelivery : INotificationRepository
    {
        public NotificationDelivery Delivery { get; } = new()
        {
            NotificationDeliveryId = Guid.NewGuid(), SystemEventId = Guid.NewGuid(), Channel = DeliveryChannel.Email,
            Recipient = "jane@example.test", Subject = "s", Body = "b",
        };
        public Task<IReadOnlyList<NotificationDelivery>> GetDueDeliveriesAsync(int max, DateTime now) =>
            Task.FromResult<IReadOnlyList<NotificationDelivery>>(
                Delivery.Status == DeliveryStatus.Pending ? new[] { Delivery } : Array.Empty<NotificationDelivery>());
        public Task UpdateDeliveryAsync(NotificationDelivery delivery) => Task.CompletedTask;

        public Task<IReadOnlyList<SystemEvent>> GetUnprocessedEventsAsync(int max, int maxAttempts) => throw new NotImplementedException();
        public Task CompleteEventAsync(SystemEvent e, IEnumerable<Notification> n, IEnumerable<NotificationDelivery> d, string? o, IEnumerable<string> c) => throw new NotImplementedException();
        public Task MarkEventFailedAsync(Guid id, string error) => throw new NotImplementedException();
        public Task<bool> IsAlertOpenAsync(Guid b, string k) => throw new NotImplementedException();
        public Task<IReadOnlyList<NotificationRecipient>> GetRecipientsAsync(Guid? b, IReadOnlyCollection<string> p, Guid? branch) => throw new NotImplementedException();
        public Task<NotificationRecipient?> GetRecipientAsync(Guid u, Guid? b) => throw new NotImplementedException();
        public Task<string?> GetBusinessNameAsync(Guid b) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetOpenAlertKeysAsync() => throw new NotImplementedException();
        public Task CloseAlertsAsync(IEnumerable<string> k) => throw new NotImplementedException();
        public Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetNotificationsAsync(Guid u, bool r, int p, int s) => throw new NotImplementedException();
        public Task<int> GetUnreadCountAsync(Guid u) => throw new NotImplementedException();
        public Task<bool> MarkReadAsync(Guid u, Guid n) => throw new NotImplementedException();
        public Task<int> MarkAllReadAsync(Guid u) => throw new NotImplementedException();
        public Task<(IReadOnlyList<SystemEvent> Items, int TotalCount)> GetEventsAsync(AuditLogQuery q, int p, int s) => throw new NotImplementedException();
        public Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> ids) => throw new NotImplementedException();
    }

    private static (DeliveryProcessor Processor, OneDelivery Repository, FlakyEmail Email) Build(int maxAttempts = 3)
    {
        var repository = new OneDelivery();
        var email = new FlakyEmail();
        return (new DeliveryProcessor(repository, email, new NoSms(), new NotificationSettings { MaxDeliveryAttempts = maxAttempts }), repository, email);
    }

    [Fact]
    public async Task A_temporary_failure_is_retried_later_and_a_success_clears_the_error()
    {
        var (processor, repository, email) = Build();
        email.Results.Enqueue(ChannelSendResult.Failed("mail server busy"));
        email.Results.Enqueue(ChannelSendResult.Sent("msg-42"));

        await processor.ProcessDueAsync();
        var d = repository.Delivery;
        Assert.Equal((DeliveryStatus.Pending, 1, "mail server busy"), (d.Status, d.Attempts, d.LastError));
        Assert.InRange(d.NextAttemptAt!.Value, DateTime.UtcNow.AddSeconds(50), DateTime.UtcNow.AddMinutes(2)); // first backoff: 1 minute

        await processor.ProcessDueAsync();
        Assert.Equal((DeliveryStatus.Sent, 2, "msg-42", (string?)null), (d.Status, d.Attempts, d.ProviderMessageId, d.LastError));
        Assert.NotNull(d.SentAt);
    }

    [Fact]
    public async Task Delivery_gives_up_after_the_last_attempt_or_at_once_on_a_permanent_failure()
    {
        var (processor, repository, email) = Build(maxAttempts: 2);
        email.Results.Enqueue(ChannelSendResult.Failed("timeout"));
        email.Results.Enqueue(ChannelSendResult.Failed("timeout"));
        await processor.ProcessDueAsync();
        await processor.ProcessDueAsync();
        Assert.Equal((DeliveryStatus.Failed, 2), (repository.Delivery.Status, repository.Delivery.Attempts));

        var (processor2, repository2, email2) = Build(maxAttempts: 5);
        email2.Results.Enqueue(ChannelSendResult.Failed("mailbox does not exist", retryable: false));
        await processor2.ProcessDueAsync();
        Assert.Equal((DeliveryStatus.Failed, 1), (repository2.Delivery.Status, repository2.Delivery.Attempts));
    }
}
