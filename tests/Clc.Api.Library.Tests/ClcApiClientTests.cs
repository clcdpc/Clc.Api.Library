using System.Net;
using System.Reflection;
using Clc.Api.Models;
using Clc.Rest;

namespace Clc.Api.Library.Tests;

[TestClass]
public sealed class ClcApiClientTests
{
    private const string BaseUrl = "https://example.test";
    private const string ApiKey = "test-api-key";

    [TestMethod]
    public async Task GetIpAsync_SendsGetToIpEndpointAndApiKeyHeader()
    {
        var handler = new CapturingHttpMessageHandler(TextResponse("127.0.0.1"));
        var client = CreateClient(handler);

        await client.GetIpAsync();

        AssertRequest(handler, HttpMethod.Get, "https://example.test/ip");
        CollectionAssert.Contains(handler.LastRequest!.Headers.GetValues("x-api-key").ToList(), ApiKey);
    }

    [TestMethod]
    public async Task GetItemAsync_SendsExpectedRequest()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.GetItemAsync("item-123");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/item/item-123");
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithoutBarcodes_SendsExpectedRequest()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, includeLibrary: true);

        AssertRequest(handler, HttpMethod.Post, "https://example.test/items/in-transit/42?includeLibrary=True");
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithBarcodes_SendsExpectedRequestAndJsonBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, ["barcode-1", "barcode-2"], includeLibrary: false);

        AssertRequest(handler, HttpMethod.Post, "https://example.test/items/in-transit/42?includeLibrary=False");
        Assert.IsFalse(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        StringAssert.Contains(handler.LastRequestBody!, "barcode-1");
        StringAssert.Contains(handler.LastRequestBody!, "barcode-2");
    }

    [TestMethod]
    public async Task GetPatronHoldsAsync_SendsExpectedRequest()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{\"holds\":[]}"));
        var client = CreateClient(handler);

        await client.GetPatronHoldsAsync("patron-123");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/patron/patron-123/holds");
    }

    [TestMethod]
    public async Task GetPatronHeldItemsAsync_SendsExpectedRequest()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetPatronHeldItemsAsync("patron-123");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/patron/patron-123/held-items");
    }

    [TestMethod]
    public async Task GetHoldingsAsync_SendsExpectedRequestWithRepeatedBibIdsInOrder()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetHoldingsAsync(1, 2, 3);

        AssertRequest(handler, HttpMethod.Get, "https://example.test/bib/holdings?bibid=1&bibid=2&bibid=3");
    }

    [TestMethod]
    public async Task RemovePatronSmsDetailsAsync_SendsExpectedDeleteRequestAndJsonBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.RemovePatronSmsDetailsAsync("patron-123", "remove sms note");

        AssertRequest(handler, HttpMethod.Delete, "https://example.test/patron/patron-123/sms");
        Assert.IsFalse(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        StringAssert.Contains(handler.LastRequestBody!, "remove sms note");
    }

    [TestMethod]
    public async Task GetPatronsForSmsNumberAsync_SendsExpectedRequest()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetPatronsForSmsNumberAsync("6145550100");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/sms-numbers/6145550100/patrons");
    }

    [TestMethod]
    public async Task AddNonBlockingNoteAsync_SendsExpectedPostRequestAndJsonBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.AddNonBlockingNoteAsync("patron-123", "non-blocking note");

        AssertRequest(handler, HttpMethod.Post, "https://example.test/patron/patron-123/notes/non-blocking");
        Assert.IsFalse(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        StringAssert.Contains(handler.LastRequestBody!, "non-blocking note");
    }

    [TestMethod]
    public async Task GetIpAsync_ReturnsRawStringBodyForSuccessfulPlainTextResponse()
    {
        var handler = new CapturingHttpMessageHandler(TextResponse("203.0.113.10"));
        var client = CreateClient(handler);

        var response = await client.GetIpAsync();

        Assert.AreEqual("203.0.113.10", response.Data);
        Assert.IsNull(response.Exception);
    }

    [TestMethod]
    public async Task TypedEndpoint_DeserializesRepresentativeJsonWithoutException()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{\"itemRecordID\":123,\"title\":\"Test title\",\"barcode\":\"item-barcode\"}"));
        var client = CreateClient(handler);

        var response = await client.GetItemAsync("item-barcode");

        Assert.IsNull(response.Exception);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(123, response.Data.ItemRecordID);
        Assert.AreEqual("Test title", response.Data.Title);
        Assert.AreEqual("item-barcode", response.Data.Barcode);
    }

    [TestMethod]
    public async Task NonSuccessResponse_ReturnsResponseMetadataAndContent()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{\"error\":\"not found\"}", HttpStatusCode.NotFound, "Not Found"));
        var client = CreateClient(handler);

        var response = await client.GetItemAsync("missing-item");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Response);
        Assert.AreEqual(HttpStatusCode.NotFound, response.Response.StatusCode);
        Assert.IsFalse(response.Response.IsSuccessStatusCode);
        Assert.AreEqual("Not Found", response.Response.ReasonPhrase);
        StringAssert.Contains(response.Response.Content, "not found");
    }

    [TestMethod]
    public void PublicMethodShape_MatchesWrapperContract()
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
        return new ClcApiClient(BaseUrl, ApiKey, new HttpClient(handler));
    }

    private static void AssertRequest(CapturingHttpMessageHandler handler, HttpMethod expectedMethod, string expectedUrl)
    {
        Assert.IsNotNull(handler.LastRequest);
        Assert.AreEqual(expectedMethod, handler.LastRequest.Method);
        Assert.AreEqual(expectedUrl, handler.LastRequest.RequestUri!.AbsoluteUri);
    }

    private static HttpResponseMessage TextResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK, string? reasonPhrase = null)
    {
        return Response(content, "text/plain", statusCode, reasonPhrase);
    }

    private static HttpResponseMessage JsonResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK, string? reasonPhrase = null)
    {
        return Response(content, "application/json", statusCode, reasonPhrase);
    }

    private static HttpResponseMessage Response(string content, string mediaType, HttpStatusCode statusCode, string? reasonPhrase)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType),
            ReasonPhrase = reasonPhrase
        };
    }

    private static void AssertPublicMethodReturnType<T>(string methodName, params Type[] parameterTypes)
    {
        var method = typeof(ClcApiClient).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.IsNotNull(method, $"Expected public method {methodName}({string.Join(", ", parameterTypes.Select(t => t.Name))}) to exist.");
        Assert.AreEqual(typeof(Task<IRestResponse<T>>), method.ReturnType);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new();

        public CapturingHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            foreach (var response in responses)
            {
                this.responses.Enqueue(response);
            }
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
