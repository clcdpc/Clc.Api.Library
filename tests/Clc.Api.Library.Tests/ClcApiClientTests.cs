using System.Net;
using System.Reflection;
using System.Text;
using Clc.Api.Models;
using Clc.Rest;

namespace Clc.Api.Library.Tests;

[TestClass]
public class ClcApiClientTests
{
    private const string BaseUrl = "https://example.test";
    private const string ApiKey = "test-api-key";

    [TestMethod]
    public async Task GetIpAsync_SendsGetToIpEndpointWithApiKeyHeader()
    {
        var handler = new CapturingHttpMessageHandler(TextResponse("127.0.0.1"));
        var client = CreateClient(handler);

        await client.GetIpAsync();

        AssertRequest(handler, HttpMethod.Get, "https://example.test/ip");
        CollectionAssert.Contains(handler.LastRequest!.Headers.GetValues("x-api-key").ToList(), ApiKey);
    }

    [TestMethod]
    public async Task GetItemAsync_SendsGetToItemEndpoint()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.GetItemAsync("i123");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/item/i123");
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithoutBarcodeList_SendsPostToInTransitEndpoint()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, includeLibrary: true);

        AssertRequest(handler, HttpMethod.Post, "https://example.test/items/in-transit/42?includeLibrary=True");
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithBarcodeList_SendsPostToInTransitEndpointAndSerializesBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, new[] { "barcode-1", "barcode-2" }, includeLibrary: false);

        AssertRequest(handler, HttpMethod.Post, "https://example.test/items/in-transit/42?includeLibrary=False");
        AssertNonEmptyBodyContains(handler, "barcode-1", "barcode-2");
    }

    [TestMethod]
    public async Task GetPatronHoldsAsync_SendsGetToPatronHoldsEndpoint()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.GetPatronHoldsAsync("p123");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/patron/p123/holds");
    }

    [TestMethod]
    public async Task GetPatronHeldItemsAsync_SendsGetToPatronHeldItemsEndpoint()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetPatronHeldItemsAsync("p123");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/patron/p123/held-items");
    }

    [TestMethod]
    public async Task GetHoldingsAsync_SendsGetWithRepeatedBibIdQueryParametersInOrder()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetHoldingsAsync(1, 2, 3);

        AssertRequest(handler, HttpMethod.Get, "https://example.test/bib/holdings?bibid=1&bibid=2&bibid=3");
    }

    [TestMethod]
    public async Task RemovePatronSmsDetailsAsync_SendsDeleteToSmsEndpointAndSerializesNoteBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.RemovePatronSmsDetailsAsync("p123", "remove sms note");

        AssertRequest(handler, HttpMethod.Delete, "https://example.test/patron/p123/sms");
        AssertNonEmptyBodyContains(handler, "remove sms note");
    }

    [TestMethod]
    public async Task GetPatronsForSmsNumberAsync_SendsGetToSmsNumberPatronsEndpoint()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("[]"));
        var client = CreateClient(handler);

        await client.GetPatronsForSmsNumberAsync("6145550100");

        AssertRequest(handler, HttpMethod.Get, "https://example.test/sms-numbers/6145550100/patrons");
    }

    [TestMethod]
    public async Task AddNonBlockingNoteAsync_SendsPostToNonBlockingNoteEndpointAndSerializesNoteBody()
    {
        var handler = new CapturingHttpMessageHandler(JsonResponse("{}"));
        var client = CreateClient(handler);

        await client.AddNonBlockingNoteAsync("p123", "non-blocking note");

        AssertRequest(handler, HttpMethod.Post, "https://example.test/patron/p123/notes/non-blocking");
        AssertNonEmptyBodyContains(handler, "non-blocking note");
    }

    [TestMethod]
    public async Task GetIpAsync_ReturnsRawStringBodyForSuccessfulTextPlainResponse()
    {
        var handler = new CapturingHttpMessageHandler(TextResponse("203.0.113.10"));
        var client = CreateClient(handler);

        var response = await client.GetIpAsync();

        Assert.IsNull(response.Exception);
        Assert.AreEqual("203.0.113.10", response.Data);
        Assert.AreEqual(HttpStatusCode.OK, response.Response.StatusCode);
    }

    [TestMethod]
    public async Task GetItemAsync_DeserializesRepresentativeJsonWithoutResponseException()
    {
        var json = """
            {
              "itemRecordID": 123,
              "title": "Regression Test Title",
              "author": "Regression Author",
              "barcode": "item-barcode"
            }
            """;
        var handler = new CapturingHttpMessageHandler(JsonResponse(json));
        var client = CreateClient(handler);

        var response = await client.GetItemAsync("item-barcode");

        Assert.IsNull(response.Exception);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(123, response.Data.ItemRecordID);
        Assert.AreEqual("Regression Test Title", response.Data.Title);
        Assert.AreEqual("item-barcode", response.Data.Barcode);
    }

    [TestMethod]
    public async Task NonSuccessResponse_ReturnsIRestResponseWithResponseMetadataAndContent()
    {
        var handler = new CapturingHttpMessageHandler(TextResponse("bad request body", HttpStatusCode.BadRequest, "Bad Request"));
        var client = CreateClient(handler);

        var response = await client.GetIpAsync();

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Response);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.Response.StatusCode);
        Assert.IsFalse(response.Response.IsSuccessStatusCode);
        Assert.AreEqual("bad request body", response.Response.Content);
    }

    [TestMethod]
    public void PublicMethods_MaintainExpectedReturnShapes()
    {
        AssertMethodReturns<string>(nameof(ClcApiClient.GetIpAsync));
        AssertMethodReturns<ItemInfo>(nameof(ClcApiClient.GetItemAsync), typeof(string));
        AssertMethodReturns<List<InTransitItem>>(nameof(ClcApiClient.GetInTransitItemsAsync), typeof(int), typeof(bool));
        AssertMethodReturns<List<InTransitItem>>(nameof(ClcApiClient.GetInTransitItemsAsync), typeof(int), typeof(IEnumerable<string>), typeof(bool));
        AssertMethodReturns<GetPatronHoldsResult>(nameof(ClcApiClient.GetPatronHoldsAsync), typeof(string));
        AssertMethodReturns<List<PatronHeldItem>>(nameof(ClcApiClient.GetPatronHeldItemsAsync), typeof(string));
        AssertMethodReturns<IEnumerable<BibInfo>>(nameof(ClcApiClient.GetHoldingsAsync), typeof(int[]));
        AssertMethodReturns<RemoveSmsDetailsResult>(nameof(ClcApiClient.RemovePatronSmsDetailsAsync), typeof(string), typeof(string));
        AssertMethodReturns<List<Patron>>(nameof(ClcApiClient.GetPatronsForSmsNumberAsync), typeof(string));
        AssertMethodReturns<AddNonBlockingNoteResult>(nameof(ClcApiClient.AddNonBlockingNoteAsync), typeof(string), typeof(string));
    }

    private static ClcApiClient CreateClient(CapturingHttpMessageHandler handler) =>
        new(BaseUrl, ApiKey, new HttpClient(handler));

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK, string? reasonPhrase = null) =>
        new(statusCode)
        {
            ReasonPhrase = reasonPhrase,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage TextResponse(string text, HttpStatusCode statusCode = HttpStatusCode.OK, string? reasonPhrase = null) =>
        new(statusCode)
        {
            ReasonPhrase = reasonPhrase,
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        };

    private static void AssertRequest(CapturingHttpMessageHandler handler, HttpMethod method, string absoluteUrl)
    {
        Assert.IsNotNull(handler.LastRequest);
        Assert.AreEqual(method, handler.LastRequest.Method);
        Assert.AreEqual(absoluteUrl, handler.LastRequest.RequestUri!.AbsoluteUri);
    }

    private static void AssertNonEmptyBodyContains(CapturingHttpMessageHandler handler, params string[] expectedValues)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(handler.LastRequestBody));
        foreach (var expectedValue in expectedValues)
        {
            StringAssert.Contains(handler.LastRequestBody, expectedValue);
        }
    }

    private static void AssertMethodReturns<T>(string methodName, params Type[] parameterTypes)
    {
        var method = typeof(ClcApiClient).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, parameterTypes);
        Assert.IsNotNull(method, $"Expected public method {methodName}({string.Join(", ", parameterTypes.Select(t => t.Name))}) to exist.");

        var expectedReturnType = typeof(Task<IRestResponse<T>>);
        Assert.AreEqual(expectedReturnType, method.ReturnType, $"Unexpected return type for {methodName}.");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public CapturingHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastCancellationToken = cancellationToken;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responses.Count > 0
                ? _responses.Dequeue()
                : JsonResponse("{}");
        }
    }
}
