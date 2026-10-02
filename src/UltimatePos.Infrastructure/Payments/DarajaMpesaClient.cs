using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UltimatePos.Application.Payments;

namespace UltimatePos.Infrastructure.Payments
{
    public class DarajaMpesaClient : IMpesaClient
    {
        private const string TokenCacheKey = "Mpesa:AccessToken";

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly MpesaOptions _options;

        public DarajaMpesaClient(HttpClient httpClient, IMemoryCache cache, IOptions<MpesaOptions> options)
        {
            _httpClient = httpClient;
            _cache = cache;
            _options = options.Value;
        }

        public async Task<StkPushResult> InitiateStkPushAsync(string phoneNumber, decimal amount, string accountReference, string transactionDesc)
        {
            try
            {
                var token = await GetAccessTokenAsync();
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var password = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ShortCode}{_options.Passkey}{timestamp}"));

                var request = new HttpRequestMessage(HttpMethod.Post, "/mpesa/stkpush/v1/processrequest")
                {
                    Content = JsonContent.Create(new
                    {
                        BusinessShortCode = _options.ShortCode,
                        Password = password,
                        Timestamp = timestamp,
                        TransactionType = "CustomerPayBillOnline",
                        Amount = (long)Math.Ceiling(amount), // Daraja expects a whole-number shilling amount
                        PartyA = phoneNumber,
                        PartyB = _options.ShortCode,
                        PhoneNumber = phoneNumber,
                        CallBackURL = _options.StkCallbackUrl,
                        AccountReference = accountReference.Length > 12 ? accountReference[..12] : accountReference,
                        TransactionDesc = transactionDesc
                    })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadFromJsonAsync<StkPushResponse>();
                var responseCode = ElementToString(body?.ResponseCode);

                if (!response.IsSuccessStatusCode || body is null || responseCode != "0")
                    return new StkPushResult(false, null, null, body?.ResponseDescription ?? body?.errorMessage ?? "STK push request failed.");

                return new StkPushResult(true, body.MerchantRequestID, body.CheckoutRequestID, null);
            }
            catch (Exception ex)
            {
                return new StkPushResult(false, null, null, ex.Message);
            }
        }

        public async Task<StkQueryResult> QueryStkStatusAsync(string checkoutRequestId)
        {
            var token = await GetAccessTokenAsync();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var password = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ShortCode}{_options.Passkey}{timestamp}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "/mpesa/stkpushquery/v1/query")
            {
                Content = JsonContent.Create(new
                {
                    BusinessShortCode = _options.ShortCode,
                    Password = password,
                    Timestamp = timestamp,
                    CheckoutRequestID = checkoutRequestId
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new StkQueryResult(false, null, null);

            var body = await response.Content.ReadFromJsonAsync<StkQueryResponse>();
            var resultCode = ElementToString(body?.ResultCode);
            return new StkQueryResult(true, resultCode is null ? null : int.Parse(resultCode), body?.ResultDesc);
        }

        public async Task RegisterC2BUrlsAsync(string confirmationUrl, string validationUrl)
        {
            var token = await GetAccessTokenAsync();
            var request = new HttpRequestMessage(HttpMethod.Post, "/mpesa/c2b/v1/registerurl")
            {
                Content = JsonContent.Create(new
                {
                    ShortCode = _options.ShortCode,
                    ResponseType = "Completed",
                    ConfirmationURL = confirmationUrl,
                    ValidationURL = validationUrl
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        private async Task<string> GetAccessTokenAsync()
        {
            if (_cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null)
                return cached;

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ConsumerKey}:{_options.ConsumerSecret}"));
            var request = new HttpRequestMessage(HttpMethod.Get, "/oauth/v1/generate?grant_type=client_credentials");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>()
                ?? throw new InvalidOperationException("Daraja did not return an access token.");

            // Cached for slightly less than the stated 3600s lifetime so a near-expiry token is never handed out.
            _cache.Set(TokenCacheKey, payload.AccessToken, TimeSpan.FromSeconds(Math.Max(60, payload.ExpiresIn - 60)));
            return payload.AccessToken;
        }

        // Daraja's exact numeric-vs-string typing on ResponseCode/ResultCode has been inconsistent across community
        // reports — JsonElement reads whichever shape actually comes back rather than assuming one and throwing.
        private static string? ElementToString(JsonElement? element) =>
            element?.ValueKind switch
            {
                JsonValueKind.String => element.Value.GetString(),
                JsonValueKind.Number => element.Value.GetRawText(),
                _ => null
            };

        private record OAuthTokenResponse(
            [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
            [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
        private record StkPushResponse(string? MerchantRequestID, string? CheckoutRequestID, JsonElement? ResponseCode, string? ResponseDescription, string? errorMessage);
        private record StkQueryResponse(JsonElement? ResultCode, string? ResultDesc);
    }
}
