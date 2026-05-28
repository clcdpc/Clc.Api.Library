using Clc.Api.Library;
using Clc.Api.Models;
using Clc.Rest;
using Clc.Rest.Auth;
using Clc.Rest.Models;

namespace Clc.Api.Library.Tests;

public class ClcApiClientTests
{
    [Fact]
    public void Constructor_ConfiguresBaseUrlAndHeaderApiKeyAuthenticator()
    {
        var client = new RecordingClcApiClient("https://api.example.test", "test-api-key");

        Assert.Equal("https://api.example.test", client.BaseUrl);
        var authenticator = Assert.IsType<HeaderApiKeyAuthenticator>(client.Authenticator);
        Assert.Equal("test-api-key", authenticator.ApiKey);
        Assert.Equal("x-api-key", authenticator.HeaderName);
    }

    [Theory]
    [MemberData(nameof(GetRequestCases))]
    public async Task GetMethods_CreateExpectedRequests(
        Func<RecordingClcApiClient, Task> act,
        HttpMethod expectedMethod,
        string expectedPath)
    {
        var client = new RecordingClcApiClient();

        await act(client);

        var request = Assert.Single(client.Requests);
        Assert.Equal(expectedMethod, request.Method);
        Assert.Equal(expectedPath, request.Path);
        Assert.Null(request.Body);
    }

    public static IEnumerable<object[]> GetRequestCases()
    {
        yield return [(Func<RecordingClcApiClient, Task>)(client => client.GetIpAsync()), HttpMethod.Get, "/ip"];
        yield return [(Func<RecordingClcApiClient, Task>)(client => client.GetItemAsync("item-123")), HttpMethod.Get, "/item/item-123"];
        yield return [(Func<RecordingClcApiClient, Task>)(client => client.GetPatronHoldsAsync("patron-123")), HttpMethod.Get, "/patron/patron-123/holds"];
        yield return [(Func<RecordingClcApiClient, Task>)(client => client.GetPatronHeldItemsAsync("patron-123")), HttpMethod.Get, "/patron/patron-123/held-items"];
        yield return [(Func<RecordingClcApiClient, Task>)(client => client.GetHoldingsAsync(101, 202)), HttpMethod.Get, "/bib/holdings?bibid=101&bibid=202"];
        yield return [(Func<RecordingClcApiClient, Task>)(client => client.GetPatronsForSmsNumberAsync("6145550100")), HttpMethod.Get, "/sms-numbers/6145550100/patrons"];
    }

    [Fact]
    public async Task GetInTransitItemsAsync_WithoutBarcodes_PostsEmptyBarcodeListAndDefaultIncludeLibraryFlag()
    {
        var client = new RecordingClcApiClient();

        await client.GetInTransitItemsAsync(42);

        var request = Assert.Single(client.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/items/in-transit/42?includeLibrary=False", request.Path);
        var body = Assert.IsAssignableFrom<IEnumerable<string>>(request.Body);
        Assert.Empty(body);
    }

    [Fact]
    public async Task GetInTransitItemsAsync_WithBarcodes_PostsProvidedBarcodeListAndIncludeLibraryFlag()
    {
        var client = new RecordingClcApiClient();
        var barcodes = new[] { "barcode-1", "barcode-2" };

        await client.GetInTransitItemsAsync(42, barcodes, includeLibrary: true);

        var request = Assert.Single(client.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/items/in-transit/42?includeLibrary=True", request.Path);
        Assert.Same(barcodes, request.Body);
    }

    [Fact]
    public async Task RemovePatronSmsDetailsAsync_CreatesDeleteRequestWithNoteBody()
    {
        var client = new RecordingClcApiClient();

        await client.RemovePatronSmsDetailsAsync("patron-123", "Removed stale SMS details");

        var request = Assert.Single(client.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/patron/patron-123/sms", request.Path);
        var body = Assert.IsType<RemoveSmsDetailsData>(request.Body);
        Assert.Equal("Removed stale SMS details", body.Note);
    }

    [Fact]
    public async Task AddNonBlockingNoteAsync_CreatesPostRequestWithNoteBody()
    {
        var client = new RecordingClcApiClient();

        await client.AddNonBlockingNoteAsync("patron-123", "Added a non-blocking note");

        var request = Assert.Single(client.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/patron/patron-123/notes/non-blocking", request.Path);
        var body = Assert.IsType<AddNonBlockingNoteData>(request.Body);
        Assert.Equal("Added a non-blocking note", body.Note);
    }

    public sealed class RecordingClcApiClient(string baseUrl = "https://api.example.test", string apiKey = "test-api-key")
        : ClcApiClient(baseUrl, apiKey)
    {
        public List<RestRequest> Requests { get; } = [];

        protected override Task<IRestResponse<T>> SendAsync<T>(RestRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult<IRestResponse<T>>(new RestResponse<T>());
        }
    }
}
