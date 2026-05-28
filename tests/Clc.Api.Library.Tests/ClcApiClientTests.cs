using Xunit;
using System.Net;
using System.Text.Json;

namespace Clc.Api.Library.Tests;

public class ClcApiClientTests
{
    [Fact]
    public async Task GetIpAsync_SendsGetRequestToIpEndpointWithApiKey()
    {
        var handler = new RecordingHandler("\"127.0.0.1\"");
        var client = CreateClient(handler);

        await client.GetIpAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://api.test/ip", request.Uri.ToString());
        Assert.Equal("secret-key", request.ApiKey);
        Assert.Null(request.Body);
    }

    [Theory]
    [MemberData(nameof(GetEndpointCases))]
    public async Task GetMethods_SendExpectedEndpointRequests(Func<ClcApiClient, Task> act, string expectedUri, string responseBody)
    {
        var handler = new RecordingHandler(responseBody);
        var client = CreateClient(handler);

        await act(client);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(expectedUri, request.Uri.ToString());
        Assert.Equal("secret-key", request.ApiKey);
        Assert.Null(request.Body);
    }

    public static TheoryData<Func<ClcApiClient, Task>, string, string> GetEndpointCases() => new()
    {
        { c => c.GetItemAsync("item-123"), "http://api.test/item/item-123", "{}" },
        { c => c.GetPatronHoldsAsync("patron-123"), "http://api.test/patron/patron-123/holds", "{}" },
        { c => c.GetPatronHeldItemsAsync("patron-123"), "http://api.test/patron/patron-123/held-items", "[]" },
        { c => c.GetHoldingsAsync(1001, 1002), "http://api.test/bib/holdings?bibid=1001&bibid=1002", "[]" },
        { c => c.GetPatronsForSmsNumberAsync("6145550100"), "http://api.test/sms-numbers/6145550100/patrons", "[]" },
    };

    [Fact]
    public async Task GetInTransitItemsAsync_PostsBarcodeListAndIncludeLibraryQuery()
    {
        var handler = new RecordingHandler("[]");
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42, ["barcode-1", "barcode-2"], includeLibrary: true);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://api.test/items/in-transit/42?includeLibrary=True", request.Uri.ToString());
        Assert.Equal("secret-key", request.ApiKey);
        Assert.Equal(["barcode-1", "barcode-2"], JsonSerializer.Deserialize<string[]>(request.Body!)!);
    }

    [Fact]
    public async Task GetInTransitItemsAsync_DefaultOverloadPostsEmptyBarcodeList()
    {
        var handler = new RecordingHandler("[]");
        var client = CreateClient(handler);

        await client.GetInTransitItemsAsync(42);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://api.test/items/in-transit/42?includeLibrary=False", request.Uri.ToString());
        Assert.Equal("secret-key", request.ApiKey);
        Assert.Equal([], JsonSerializer.Deserialize<string[]>(request.Body!)!);
    }

    [Fact]
    public async Task RemovePatronSmsDetailsAsync_SendsDeleteWithNoteBody()
    {
        var handler = new RecordingHandler("{}");
        var client = CreateClient(handler);

        await client.RemovePatronSmsDetailsAsync("patron-123", "removed bad sms number");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("http://api.test/patron/patron-123/sms", request.Uri.ToString());
        Assert.Equal("secret-key", request.ApiKey);
        AssertJsonProperty(request.Body!, "Note", "removed bad sms number");
    }

    [Fact]
    public async Task AddNonBlockingNoteAsync_PostsNoteBody()
    {
        var handler = new RecordingHandler("{}");
        var client = CreateClient(handler);

        await client.AddNonBlockingNoteAsync("patron-123", "new non-blocking note");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://api.test/patron/patron-123/notes/non-blocking", request.Uri.ToString());
        Assert.Equal("secret-key", request.ApiKey);
        AssertJsonProperty(request.Body!, "Note", "new non-blocking note");
    }

    private static ClcApiClient CreateClient(RecordingHandler handler) =>
        new("http://api.test", "secret-key", new HttpClient(handler));

    private static void AssertJsonProperty(string json, string propertyName, string expectedValue)
    {
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty(propertyName, out var property), $"Expected JSON body to contain '{propertyName}': {json}");
        Assert.Equal(expectedValue, property.GetString());
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("x-api-key", out var values) ? Assert.Single(values) : null,
                body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ApiKey, string? Body);
}
