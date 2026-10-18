using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Notifications
{
    /// <summary>
    /// Sends queued email and SMS deliveries through the configured providers. A failure is retried with backoff (1, 5, 15, 60
    /// minutes...) up to MaxDeliveryAttempts; a failure the provider says is permanent stops at once.
    /// </summary>
    public class DeliveryProcessor
    {
        private static readonly TimeSpan[] Backoff =
            { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60) };

        private readonly INotificationRepository _repository;
        private readonly IEmailSender _email;
        private readonly ISmsSender _sms;
        private readonly NotificationSettings _settings;

        public DeliveryProcessor(INotificationRepository repository, IEmailSender email, ISmsSender sms, NotificationSettings settings)
        {
            _repository = repository;
            _email = email;
            _sms = sms;
            _settings = settings;
        }

        /// <returns>How many deliveries were attempted.</returns>
        public async Task<int> ProcessDueAsync(int batchSize = 50, CancellationToken cancellationToken = default)
        {
            var due = await _repository.GetDueDeliveriesAsync(batchSize, DateTime.UtcNow);
            foreach (var delivery in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ChannelSendResult result;
                try
                {
                    result = delivery.Channel switch
                    {
                        DeliveryChannel.Email => await _email.SendAsync(
                            new EmailMessage(delivery.Recipient, null, delivery.Subject ?? "Ultimate POS", delivery.Body, delivery.HtmlBody), cancellationToken),
                        DeliveryChannel.Sms => await _sms.SendAsync(new SmsMessage(delivery.Recipient, delivery.Body), cancellationToken),
                        _ => ChannelSendResult.Failed($"Unknown channel {delivery.Channel}.", retryable: false),
                    };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result = ChannelSendResult.Failed(ex.Message);
                }

                delivery.Attempts++;
                if (result.Success)
                {
                    delivery.Status = DeliveryStatus.Sent;
                    delivery.SentAt = DateTime.UtcNow;
                    delivery.ProviderMessageId = result.ProviderMessageId;
                    delivery.LastError = null;
                    delivery.NextAttemptAt = null;
                }
                else if (!result.Retryable || delivery.Attempts >= _settings.MaxDeliveryAttempts)
                {
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.LastError = Truncate(result.Error);
                    delivery.NextAttemptAt = null;
                }
                else
                {
                    delivery.LastError = Truncate(result.Error);
                    delivery.NextAttemptAt = DateTime.UtcNow + Backoff[Math.Min(delivery.Attempts - 1, Backoff.Length - 1)];
                }

                await _repository.UpdateDeliveryAsync(delivery);
            }
            return due.Count;
        }

        private static string? Truncate(string? value) => value is { Length: > 1000 } ? value[..1000] : value;
    }
}
