using System.Net;
using System.Reflection;
using System.Text;
using Clc.Api.Library;
using Clc.Api.Models;
using Clc.Rest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clc.Api.Library.Tests;

[TestClass]
public class ClcApiClientTests
{
    private const string BaseUrl = "https://example.test";
    private const string ApiKey = "test-api-key";

    [TestMethod]
    public async Task GetIpAsync_SendsGetToIpEndpointAndIncludesApiKey()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("127.0.0.1", Encoding.UTF8, "text/plain")
        });
        var client = CreateClient(handler);

        await client.GetIpAsync();

        AssertRequest(handler, HttpMethod.Get, "https://example.test/ip");
        CollectionAssert.Contains(handler.LastRequest!.Headers.GetValues("x-api-key").ToList(), ApiKey);
    }

    [TestMethod]
    public async Task GetItemAsync_SendsExpectedRequest()
    {
        var handler = JsonHandler("{}");
        var client = CreateClient(handler);

        await client.GetItemAsync("item-123");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/item/item-123");
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithoutBarcodes_SendsExpectedRequest()
    {
        var handler = JsonHandler("[]");
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, includeLibrary: true);

        AssertRequest(handler, HttpMethod.Post, "https://example.test/items/in-transit/42?includeLibrary=True");
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithBarcodes_SendsExpectedRequestAndBody()
    {
        var handler = JsonHandler("[]");
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, ["1001", "1002"], includeLibrary: false);

        AssertRequest(handler, HttpMethod.Post, "https://example.test/items/in-transit/42?includeLibrary=False");
        AssertHasBodyContaining(handler, "1001", "1002");
    }

    [TestMethod]
    public async Task GetPatronHoldsAsync_SendsExpectedRequest()
    {
        var handler = JsonHandler("{\"holds\":[],\"errorCode\":0,\"errorMessage\":null}");
        var client = CreateClient(handler);

        await client.GetPatronHoldsAsync("patron-1");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/patron/patron-1/holds");
    }

    [TestMethod]
    public async Task GetPatronHeldItemsAsync_SendsExpectedRequest()
    {
        var handler = JsonHandler("[]");
        var client = CreateClient(handler);

        await client.GetPatronHeldItemsAsync("patron-1");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/patron/patron-1/held-items");
    }

    [TestMethod]
    public async Task GetHoldingsAsync_SendsRepeatedBibIdQueryParametersInOrder()
    {
        var handler = JsonHandler("[]");
        var client = CreateClient(handler);

        await client.GetHoldingsAsync(1, 2, 3);

        AssertRequest(handler, HttpMethod.Get, "https://example.test/bib/holdings?bibid=1&bibid=2&bibid=3");
    }

    [TestMethod]
    public async Task RemovePatronSmsDetailsAsync_SendsDeleteWithExpectedUrlAndBody()
    {
        var handler = JsonHandler("{\"errorCode\":0,\"errorMessage\":null}");
        var client = CreateClient(handler);

        await client.RemovePatronSmsDetailsAsync("patron-1", "removed sms details");

        AssertRequest(handler, HttpMethod.Delete, "https://example.test/patron/patron-1/sms");
        AssertHasBodyContaining(handler, "removed sms details");
    }

    [TestMethod]
    public async Task GetPatronsForSmsNumberAsync_SendsExpectedRequest()
    {
        var handler = JsonHandler("[]");
        var client = CreateClient(handler);

        await client.GetPatronsForSmsNumberAsync("6145550100");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/sms-numbers/6145550100/patrons");
    }

    [TestMethod]
    public async Task AddNonBlockingNoteAsync_SendsPostWithExpectedUrlAndBody()
    {
        var handler = JsonHandler("{\"errorCode\":0,\"errorMessage\":null}");
        var client = CreateClient(handler);

        await client.AddNonBlockingNoteAsync("patron-1", "test note");

        AssertRequest(handler, HttpMethod.Post, "https://example.test/patron/patron-1/notes/non-blocking");
        AssertHasBodyContaining(handler, "test note");
    }

    [TestMethod]
    public async Task GetIpAsync_ReturnsRawStringBodyForSuccessfulPlainTextResponse()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("203.0.113.9", Encoding.UTF8, "text/plain")
        });
        var client = CreateClient(handler);

        var response = await client.GetIpAsync();

        Assert.AreEqual("203.0.113.9", response.Data);
        Assert.IsNull(response.Exception);
    }

    [TestMethod]
    public async Task TypedEndpoint_DeserializesRepresentativeJsonWithoutException()
    {
        var handler = JsonHandler("{\"itemRecordID\":123,\"title\":\"The Test Item\",\"barcode\":\"item-barcode\"}");
        var client = CreateClient(handler);

        var response = await client.GetItemAsync("item-barcode");

        Assert.IsNull(response.Exception);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(123, response.Data.ItemRecordID);
        Assert.AreEqual("The Test Item", response.Data.Title);
        Assert.AreEqual("item-barcode", response.Data.Barcode);
    }

    [TestMethod]
    public async Task NonSuccessResponse_ReturnsRestResponseWithMetadataAndContent()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "Bad Request From Test",
            Content = new StringContent("bad request details", Encoding.UTF8, "text/plain")
        });
        var client = CreateClient(handler);

        var response = await client.GetIpAsync();

        Assert.IsNotNull(response.Response);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.Response.StatusCode);
        Assert.IsFalse(response.Response.IsSuccessStatusCode);
        Assert.AreEqual("bad request details", response.Response.Content);
        Assert.AreEqual("Bad Request From Test", response.Response.ReasonPhrase);
    }

    [TestMethod]
    public void PublicMethodShape_IsLockedDown()
    {
        AssertPublicMethodReturnType<string>(nameof(ClcApiClient.GetIpAsync));
        AssertPublicMethodReturnType<ItemInfo>(nameof(ClcApiClient.GetItemAsync), typeof(string));
        AssertPublicMethodReturnType<List<InTransitItem>>(nameof(ClcApiClient.GetInTransitItemsAsync), typeof(int), typeof(bool));
        AssertPublicMethodReturnType<List<InTransitItem>>(nameof(ClcApiClient.GetInTransitItemsAsync), typeof(int), typeof(IEnumerable<string>), typeof(bool));
        AssertPublicMethodReturnType<GetPatronHoldsResult>(nameof(ClcApiClient.GetPatronHoldsAsync), typeof(string));
        AssertPublicMethodReturnType<List<PatronHeldItem>>(nameof(ClcApiClient.GetPatronHeldItemsAsync), typeof(string));
        AssertPublicMethodReturnType<IEnumerable<BibInfo>>(nameof(ClcApiClient.GetHoldingsAsync), typeof(int[]));
        AssertPublicMethodReturnType<RemoveSmsDetailsResult>(nameof(ClcApiClient.RemovePatronSmsDetailsAsync), typeof(string), typeof(string));
        AssertPublicMethodReturnType<List<Patron>>(nameof(ClcApiClient.GetPatronsForSmsNumberAsync), typeof(string));
        AssertPublicMethodReturnType<AddNonBlockingNoteResult>(nameof(ClcApiClient.AddNonBlockingNoteAsync), typeof(string), typeof(string));
    }

    private static ClcApiClient CreateClient(CapturingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new ClcApiClient(BaseUrl, ApiKey, httpClient);
    }

    private static CapturingHttpMessageHandler JsonHandler(string json) => new(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    });

    private static void AssertRequest(CapturingHttpMessageHandler handler, HttpMethod expectedMethod, string expectedUrl)
    {
        Assert.IsNotNull(handler.LastRequest);
        Assert.AreEqual(expectedMethod, handler.LastRequest.Method);
        Assert.AreEqual(expectedUrl, handler.LastRequest.RequestUri!.AbsoluteUri);
    }

    private static void AssertHasBodyContaining(CapturingHttpMessageHandler handler, params string[] expectedValues)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        foreach (var expectedValue in expectedValues)
        {
            StringAssert.Contains(handler.LastRequestBody, expectedValue);
        }
    }

    private static void AssertPublicMethodReturnType<T>(string methodName, params Type[] parameterTypes)
    {
        var method = typeof(ClcApiClient).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, parameterTypes);
        Assert.IsNotNull(method, $"Expected public method {methodName}({string.Join(", ", parameterTypes.Select(t => t.Name))}) to exist.");
        Assert.AreEqual(typeof(Task<IRestResponse<T>>), method.ReturnType);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses;

        public CapturingHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            this.responses = new Queue<HttpResponseMessage>(responses);
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return responses.Count > 0
                ? responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }
}
