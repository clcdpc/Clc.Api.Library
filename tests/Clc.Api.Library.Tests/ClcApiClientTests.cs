using System.Net;
using System.Reflection;
using Clc.Api.Library;
using Clc.Api.Models;
using Clc.Rest;

namespace Clc.Api.Library.Tests;

[TestClass]
public sealed class ClcApiClientTests
{
    private const string BaseUrl = "https://example.test";
    private const string ApiKey = "test-api-key";

    [TestMethod]
    public async Task GetIpAsync_SendsGetToIpEndpointAndIncludesApiKeyHeader()
    {
        var handler = new CapturingHttpMessageHandler(TextResponse("127.0.0.1"));
        var client = CreateClient(handler);

        await client.GetIpAsync();

        AssertRequest(handler, HttpMethod.Get, "https://example.test/ip");
        CollectionAssert.Contains(handler.LastRequest!.Headers.GetValues("x-api-key").ToList(), ApiKey);
    }

    [TestMethod]
    [DataRow(nameof(ClcApiClient.GetItemAsync), "GET", "https://example.test/item/i123", new object[] { "i123" })]
    [DataRow(nameof(ClcApiClient.GetPatronHoldsAsync), "GET", "https://example.test/patron/p123/holds", new object[] { "p123" })]
    [DataRow(nameof(ClcApiClient.GetPatronHeldItemsAsync), "GET", "https://example.test/patron/p123/held-items", new object[] { "p123" })]
    [DataRow(nameof(ClcApiClient.GetPatronsForSmsNumberAsync), "GET", "https://example.test/sms-numbers/6145550100/patrons", new object[] { "6145550100" })]
    public async Task PublicGetEndpoints_SendExpectedMethodAndUrl(string methodName, string expectedMethod, string expectedUrl, object[] arguments)
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await InvokeAsync(client, methodName, arguments);

        AssertRequest(handler, new HttpMethod(expectedMethod), expectedUrl);
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithoutBarcodeList_SendsPostToExpectedUrl()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, includeLibrary: true);

        AssertRequest(handler, HttpMethod.Post, "https://example.test/items/in-transit/42?includeLibrary=True");
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithBarcodeList_SendsPostToExpectedUrlAndBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, new[] { "barcode-one", "barcode-two" }, includeLibrary: false);

        AssertRequest(handler, HttpMethod.Post, "https://example.test/items/in-transit/42?includeLibrary=False");
        AssertNonEmptyBodyContains(handler, "barcode-one", "barcode-two");
    }

    [TestMethod]
    public async Task GetHoldingsAsync_PreservesRepeatedBibIdQueryParametersInOrder()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetHoldingsAsync(1, 2, 3);

        AssertRequest(handler, HttpMethod.Get, "https://example.test/bib/holdings?bibid=1&bibid=2&bibid=3");
    }

    [TestMethod]
    public async Task RemovePatronSmsDetailsAsync_SendsDeleteWithExpectedUrlAndNoteBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.RemovePatronSmsDetailsAsync("p123", "remove sms note");

        AssertRequest(handler, HttpMethod.Delete, "https://example.test/patron/p123/sms");
        AssertNonEmptyBodyContains(handler, "remove sms note");
    }

    [TestMethod]
    public async Task AddNonBlockingNoteAsync_SendsPostWithExpectedUrlAndNoteBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.AddNonBlockingNoteAsync("p123", "non-blocking note");

        AssertRequest(handler, HttpMethod.Post, "https://example.test/patron/p123/notes/non-blocking");
        AssertNonEmptyBodyContains(handler, "non-blocking note");
    }

    [TestMethod]
    public async Task GetIpAsync_ReturnsRawStringBodyForSuccessfulTextResponse()
    {
        var handler = new CapturingHttpMessageHandler(TextResponse("203.0.113.10"));
        var client = CreateClient(handler);

        var response = await client.GetIpAsync();

        Assert.IsNull(response.Exception);
        Assert.AreEqual("203.0.113.10", response.Data);
    }

    [TestMethod]
    public async Task TypedEndpoint_DeserializesRepresentativeJsonWithoutException()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("""
            {
              "ItemRecordID": 123,
              "Title": "Regression Test Title",
              "Barcode": "item-barcode"
            }
            """));
        var client = CreateClient(handler);

        var response = await client.GetItemAsync("item-barcode");

        Assert.IsNull(response.Exception);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(123, response.Data.ItemRecordID);
        Assert.AreEqual("Regression Test Title", response.Data.Title);
        Assert.AreEqual("item-barcode", response.Data.Barcode);
    }

    [TestMethod]
    public async Task NonSuccessResponse_ReturnsRestResponseWithMetadataAndContent()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{\"error\":\"not found\"}", HttpStatusCode.NotFound, "Not Found"));
        var client = CreateClient(handler);

        var response = await client.GetItemAsync("missing-item");

        Assert.IsFalse(response.Response.IsSuccessStatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, response.Response.StatusCode);
        Assert.AreEqual("Not Found", response.Response.ReasonPhrase);
        StringAssert.Contains(response.Response.Content, "not found");
    }

    [TestMethod]
    public void PublicMethodShape_RemainsStable()
    {
        AssertReturns<IRestResponse<string>>(nameof(ClcApiClient.GetIpAsync));
        AssertReturns<IRestResponse<ItemInfo>>(nameof(ClcApiClient.GetItemAsync), typeof(string));
        AssertReturns<IRestResponse<List<InTransitItem>>>(nameof(ClcApiClient.GetInTransitItemsAsync), typeof(int), typeof(bool));
        AssertReturns<IRestResponse<List<InTransitItem>>>(nameof(ClcApiClient.GetInTransitItemsAsync), typeof(int), typeof(IEnumerable<string>), typeof(bool));
        AssertReturns<IRestResponse<GetPatronHoldsResult>>(nameof(ClcApiClient.GetPatronHoldsAsync), typeof(string));
        AssertReturns<IRestResponse<List<PatronHeldItem>>>(nameof(ClcApiClient.GetPatronHeldItemsAsync), typeof(string));
        AssertReturns<IRestResponse<IEnumerable<BibInfo>>>(nameof(ClcApiClient.GetHoldingsAsync), typeof(int[]));
        AssertReturns<IRestResponse<RemoveSmsDetailsResult>>(nameof(ClcApiClient.RemovePatronSmsDetailsAsync), typeof(string), typeof(string));
        AssertReturns<IRestResponse<List<Patron>>>(nameof(ClcApiClient.GetPatronsForSmsNumberAsync), typeof(string));
        AssertReturns<IRestResponse<AddNonBlockingNoteResult>>(nameof(ClcApiClient.AddNonBlockingNoteAsync), typeof(string), typeof(string));
    }

    private static ClcApiClient CreateClient(CapturingHttpMessageHandler handler) =>
        new(BaseUrl, ApiKey, new HttpClient(handler));

    private static void AssertRequest(CapturingHttpMessageHandler handler, HttpMethod expectedMethod, string expectedUrl)
    {
        Assert.IsNotNull(handler.LastRequest);
        Assert.AreEqual(expectedMethod, handler.LastRequest.Method);
        Assert.AreEqual(expectedUrl, handler.LastRequest.RequestUri!.AbsoluteUri);
    }

    private static void AssertNonEmptyBodyContains(CapturingHttpMessageHandler handler, params string[] expectedValues)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        foreach (var expectedValue in expectedValues)
        {
            StringAssert.Contains(handler.LastRequestBody, expectedValue);
        }
    }

    private static async Task InvokeAsync(ClcApiClient client, string methodName, object[] arguments)
    {
        var method = typeof(ClcApiClient).GetMethod(methodName, arguments.Select(a => a.GetType()).ToArray());
        Assert.IsNotNull(method);
        var task = (Task)method.Invoke(client, arguments)!;
        await task;
    }

    private static void AssertReturns<TResponse>(string methodName, params Type[] parameterTypes)
    {
        var method = typeof(ClcApiClient).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, parameterTypes);
        Assert.IsNotNull(method, $"Missing public method {methodName}({string.Join(", ", parameterTypes.Select(t => t.Name))}).");
        Assert.AreEqual(typeof(Task<TResponse>), method.ReturnType);
    }

    private static HttpResponseMessage JsonResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK, string? reasonPhrase = null) =>
        new(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json"),
            ReasonPhrase = reasonPhrase
        };

    private static HttpResponseMessage TextResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK, string? reasonPhrase = null) =>
        new(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "text/plain"),
            ReasonPhrase = reasonPhrase
        };

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
                : JsonResponse("{}");
        }
    }
}
