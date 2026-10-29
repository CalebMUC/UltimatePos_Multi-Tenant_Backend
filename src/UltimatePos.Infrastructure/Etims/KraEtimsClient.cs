using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UltimatePos.Application.Etims;

namespace UltimatePos.Infrastructure.Etims
{
    /// <summary>
    /// Calls the KRA eTIMS OSCU API through KRA's Apigee gateway: a client-credentials token (cached until shortly before it expires),
    /// the apigee_app_id header, and after /initialize the device's tin, bhfId and cmcKey headers. KRA wraps some replies in
    /// responseBody; both shapes are read. A 401 refreshes the token and tries once more.
    /// </summary>
    public class KraEtimsClient : IEtimsClient
    {
        private const string TokenCacheKey = "Etims:AccessToken";

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly EtimsOptions _options;

        public KraEtimsClient(HttpClient httpClient, IMemoryCache cache, IOptions<EtimsOptions> options)
        {
            _httpClient = httpClient;
            _cache = cache;
            _options = options.Value;
        }

        public Task<EtimsResult> InitializeAsync(EtimsInitializeRequest request, CancellationToken cancellationToken = default) =>
            SendAsync(EtimsEndpoints.Initialize, null, EtimsJson.Serialize(request), cancellationToken);

        public Task<EtimsResult> PostAsync(string endpoint, EtimsDeviceKey device, string jsonBody, CancellationToken cancellationToken = default) =>
            SendAsync(endpoint, device, jsonBody, cancellationToken);

        private async Task<EtimsResult> SendAsync(string endpoint, EtimsDeviceKey? device, string jsonBody, CancellationToken ct)
        {
            var watch = Stopwatch.StartNew();
            if (!_options.IsConfigured)
                return Failure(endpoint, 0, jsonBody, null, "eTIMS is not configured: set Etims:ConsumerKey, Etims:ConsumerSecret and Etims:ApigeeAppId.", watch);

            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    var token = await GetAccessTokenAsync(ct);
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    request.Headers.TryAddWithoutValidation("apigee_app_id", _options.ApigeeAppId);
                    if (device is not null)
                    {
                        request.Headers.TryAddWithoutValidation("tin", device.Tin);
                        request.Headers.TryAddWithoutValidation("bhfId", device.BhfId);
                        request.Headers.TryAddWithoutValidation("cmcKey", device.CmcKey);
                    }

                    using var response = await _httpClient.SendAsync(request, ct);
                    var body = await response.Content.ReadAsStringAsync(ct);
                    if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                    {
                        _cache.Remove(TokenCacheKey); // expired or revoked early: one fresh token, then give up
                        continue;
                    }
                    return Parse(endpoint, (int)response.StatusCode, jsonBody, body, watch);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Failure(endpoint, 0, jsonBody, null, $"No reply from KRA within {_options.TimeoutSeconds}s.", watch);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
            {
                return Failure(endpoint, 0, jsonBody, null, ex.Message, watch);
            }
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            if (_cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null)
                return cached;

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ConsumerKey}:{_options.ConsumerSecret}"));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.TokenUrl}?grant_type=client_credentials");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            using var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"KRA refused the access token request (HTTP {(int)response.StatusCode}): {Truncate(body)}");

            using var json = JsonDocument.Parse(body);
            var token = json.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("KRA did not return an access token.");
            // expires_in comes back as a string from Apigee; cache a minute short of it so a near-expiry token is never used.
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var e) && int.TryParse(e.ToString(), out var seconds) ? seconds : 3599;
            _cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(60, expiresIn - 60)));
            return token;
        }

        internal static EtimsResult Parse(string endpoint, int httpStatus, string requestJson, string responseJson, Stopwatch watch)
        {
            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseJson) ? "{}" : responseJson);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return Failure(endpoint, httpStatus, requestJson, responseJson, $"HTTP {httpStatus}: the reply is not JSON ({Truncate(responseJson)}).", watch);
            }

            var body = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("responseBody", out var wrapped)
                       && wrapped.ValueKind == JsonValueKind.Object ? wrapped : root;
            var resultCode = Text(body, "resultCd");
            if (resultCode is null)
            {
                // Refused before reaching eTIMS. KRA's gateway answers {"responseHeader":{"customerMessage","debugMessage"},"responseBody":null}
                // (an unknown item, an item code out of sequence ...); Apigee faults and token errors have shapes of their own.
                var customer = Nested(root, "responseHeader", "customerMessage");
                var debug = Nested(root, "responseHeader", "debugMessage");
                var message = (customer is not null ? debug is not null && debug != customer ? $"{customer} ({debug})" : customer : debug)
                              ?? Text(root, "faultstring") ?? Nested(root, "fault", "faultstring") ?? Text(root, "errorMessage")
                              ?? Text(root, "error_description") ?? Text(root, "message") ?? Text(root, "responseDesc")
                              ?? Text(root, "error") ?? Truncate(responseJson);
                return Failure(endpoint, httpStatus, requestJson, responseJson, $"HTTP {httpStatus}: {message}", watch);
            }

            JsonElement? data = body.TryGetProperty("data", out var d) && d.ValueKind is not JsonValueKind.Null ? d : null;
            return new EtimsResult(endpoint, httpStatus, resultCode, Text(body, "resultMsg"), Text(body, "resultDt"), data, requestJson,
                responseJson, null, watch.Elapsed);
        }

        private static EtimsResult Failure(string endpoint, int httpStatus, string requestJson, string? responseJson, string error, Stopwatch watch) =>
            new(endpoint, httpStatus, null, null, null, null, requestJson, responseJson, error, watch.Elapsed);

        private static string? Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => value.GetRawText()
                }
                : null;

        private static string? Nested(JsonElement element, string parent, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(parent, out var child) ? Text(child, name) : null;

        private static string Truncate(string? value) => value is null ? "" : value.Length <= 300 ? value : value[..300] + "...";
    }
}
