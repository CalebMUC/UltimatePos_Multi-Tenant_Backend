namespace UltimatePos.Infrastructure.Etims
{
    /// <summary>
    /// The "Etims" section. One Apigee app (developer.go.ke) serves every business: each branch device adds its own PIN, branch id and
    /// cmcKey to the calls. Set ConsumerKey / ConsumerSecret / ApigeeAppId as user-secrets or environment variables (Etims__ConsumerKey).
    /// </summary>
    public class EtimsOptions
    {
        public string BaseUrl { get; set; } = "https://sbx.kra.go.ke/etims-oscu/api/v1";
        public string TokenUrl { get; set; } = "https://sbx.kra.go.ke/v1/token/generate";
        public string ConsumerKey { get; set; } = string.Empty;
        public string ConsumerSecret { get; set; } = string.Empty;
        public string ApigeeAppId { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ConsumerKey) && !string.IsNullOrWhiteSpace(ConsumerSecret) && !string.IsNullOrWhiteSpace(ApigeeAppId);
    }
}
