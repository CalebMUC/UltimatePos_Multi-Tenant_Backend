using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UltimatePos.Application.Etims;
using UltimatePos.Infrastructure.Etims;

namespace Ultimate.Api.Tests;

/// <summary>The HTTP side of eTIMS, against a stub of KRA's gateway: token, headers, both reply shapes, and gateway failures.</summary>
public class KraEtimsClientTests
{
    private sealed class StubGateway : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
        public Queue<HttpResponseMessage> Replies { get; } = new();
        public int TokenCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token/generate"))
            {
                TokenCalls++;
                return Json(HttpStatusCode.OK, $$"""{"access_token":"token-{{TokenCalls}}","expires_in":"3599"}""");
            }
            Calls.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return Replies.Count > 0 ? Replies.Dequeue() : Json(HttpStatusCode.OK, """{"resultCd":"000","resultMsg":"It is succeeded","resultDt":"20261027120000","data":null}""");
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (KraEtimsClient Client, StubGateway Gateway) Create(bool configured = true)
    {
        var gateway = new StubGateway();
        var options = new EtimsOptions
        {
            BaseUrl = "https://sbx.kra.go.ke/etims-oscu/api/v1",
            TokenUrl = "https://sbx.kra.go.ke/v1/token/generate",
            ConsumerKey = configured ? "key" : "",
            ConsumerSecret = "secret",
            ApigeeAppId = "app-id",
        };
        var http = new HttpClient(gateway) { BaseAddress = new Uri(options.BaseUrl + "/") };
        return (new KraEtimsClient(http, new MemoryCache(new MemoryCacheOptions()), Options.Create(options)), gateway);
    }

    private static readonly EtimsDeviceKey Device = new("P052446551J", "00", "CMC-KEY");

    [Fact]
    public async Task Calls_carry_the_token_the_app_id_and_the_device_headers()
    {
        var (client, gateway) = Create();
        var result = await client.PostAsync(EtimsEndpoints.StockIO, Device, """{"sarNo":1}""");

        Assert.True(result.IsSuccess);
        var (request, body) = Assert.Single(gateway.Calls);
        Assert.Equal("https://sbx.kra.go.ke/etims-oscu/api/v1/insert/stockIO", request.RequestUri!.ToString());
        Assert.Equal("Bearer token-1", request.Headers.Authorization!.ToString());
        Assert.Equal("app-id", request.Headers.GetValues("apigee_app_id").Single());
        Assert.Equal("P052446551J", request.Headers.GetValues("tin").Single());
        Assert.Equal("00", request.Headers.GetValues("bhfId").Single());
        Assert.Equal("CMC-KEY", request.Headers.GetValues("cmcKey").Single());
        Assert.Equal("""{"sarNo":1}""", body);
        Assert.Equal("""{"sarNo":1}""", result.RequestJson);
    }

    [Fact]
    public async Task The_token_is_fetched_once_and_reused()
    {
        var (client, gateway) = Create();
        await client.PostAsync(EtimsEndpoints.SelectCodeList, Device, "{}");
        await client.PostAsync(EtimsEndpoints.SelectItemClass, Device, "{}");
        Assert.Equal(1, gateway.TokenCalls);
    }

    [Fact]
    public async Task Initialize_sends_no_device_headers_and_finds_the_cmc_key_inside_the_reply()
    {
        var (client, gateway) = Create();
        gateway.Replies.Enqueue(Json(HttpStatusCode.OK,
            """{"responseCode":"200","responseBody":{"resultCd":"000","resultMsg":"Successful","resultDt":"20261027","data":{"info":{"tin":"P052446551J","cmcKey":"ABC123","sdcId":"KRACU0100001","mrcNo":"WIS00000001"}}}}"""));

        var result = await client.InitializeAsync(new EtimsInitializeRequest("P052446551J", "00", "UPOSP052446551J001"));

        Assert.True(result.IsSuccess);
        Assert.Equal("ABC123", result.Find("cmcKey"));
        Assert.Equal("KRACU0100001", result.Find("sdcId"));
        var request = Assert.Single(gateway.Calls).Request;
        Assert.False(request.Headers.Contains("cmcKey"));
        Assert.Equal("""{"tin":"P052446551J","bhfId":"00","dvcSrlNo":"UPOSP052446551J001"}""", gateway.Calls[0].Body);
    }

    [Fact]
    public async Task A_refusal_is_KRA_s_verdict_not_a_transport_error()
    {
        var (client, gateway) = Create();
        gateway.Replies.Enqueue(Json(HttpStatusCode.OK, """{"resultCd":"910","resultMsg":"Request parameter error","resultDt":"20261027","data":null}"""));
        var result = await client.PostAsync(EtimsEndpoints.SaveItem, Device, "{}");
        Assert.False(result.IsSuccess);
        Assert.True(result.HasVerdict);
        Assert.Equal("910: Request parameter error", result.Describe());
    }

    [Fact]
    public async Task No_search_result_is_not_an_error()
    {
        var (client, gateway) = Create();
        gateway.Replies.Enqueue(Json(HttpStatusCode.OK, """{"resultCd":"001","resultMsg":"There is no search result","data":null}"""));
        var result = await client.PostAsync(EtimsEndpoints.SelectNoticeList, Device, "{}");
        Assert.True(result.IsNoData);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task A_gateway_fault_has_no_verdict_and_says_why()
    {
        var (client, gateway) = Create();
        gateway.Replies.Enqueue(Json(HttpStatusCode.BadGateway, """{"fault":{"faultstring":"Body buffer overflow","detail":{"errorcode":"protocol.http.TooBigBody"}}}"""));
        var result = await client.PostAsync(EtimsEndpoints.SendSalesTransaction, Device, "{}");
        Assert.False(result.HasVerdict);
        Assert.Contains("Body buffer overflow", result.Error);
        Assert.Contains("502", result.Error);
    }

    [Fact]
    public async Task A_refusal_by_KRA_s_gateway_carries_its_messages()
    {
        // As the sandbox answers an item code out of sequence.
        var (client, gateway) = Create();
        gateway.Replies.Enqueue(Json(HttpStatusCode.BadRequest,
            """{"responseHeader":{"responseCode":400,"responseRefID":"7cb2","customerMessage":"Invalid itemCd Sequence. Expected sequence ending with: ********5","debugMessage":"The itemCd is either reused or not incremented properly. Expected sequence ending with ********5"},"responseBody":null}"""));
        var result = await client.PostAsync(EtimsEndpoints.SaveItem, Device, "{}");
        Assert.False(result.HasVerdict);
        Assert.Equal("HTTP 400: Invalid itemCd Sequence. Expected sequence ending with: ********5 (The itemCd is either reused or not incremented properly. Expected sequence ending with ********5)", result.Error);
    }

    [Fact]
    public async Task A_no_result_inside_an_HTTP_400_is_still_KRA_s_verdict()
    {
        var (client, gateway) = Create();
        gateway.Replies.Enqueue(Json(HttpStatusCode.BadRequest,
            """{"responseHeader":{"responseCode":200,"customerMessage":"There is no search result"},"responseBody":{"resultCd":"001","resultMsg":"There is no search result","resultDt":"20261027120240","data":null}}"""));
        var result = await client.PostAsync(EtimsEndpoints.ImportedItemInfo, Device, "{}");
        Assert.True(result.IsNoData);
    }

    [Fact]
    public async Task An_expired_token_is_replaced_once()
    {
        var (client, gateway) = Create();
        gateway.Replies.Enqueue(Json(HttpStatusCode.Unauthorized, """{"fault":{"faultstring":"Invalid access token"}}"""));
        var result = await client.PostAsync(EtimsEndpoints.SelectCodeList, Device, "{}");
        Assert.True(result.IsSuccess);
        Assert.Equal(2, gateway.TokenCalls);
        Assert.Equal("Bearer token-2", gateway.Calls[1].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Without_credentials_nothing_is_sent()
    {
        var (client, gateway) = Create(configured: false);
        var result = await client.PostAsync(EtimsEndpoints.SelectCodeList, Device, "{}");
        Assert.False(result.HasVerdict);
        Assert.Contains("not configured", result.Error);
        Assert.Empty(gateway.Calls);
        Assert.Equal(0, gateway.TokenCalls);
    }
}
