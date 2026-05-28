using System.Net.Http;
using Clc.Api.Models;
using Clc.Rest.Auth;
using Clc.Rest.Models;

namespace Clc.Api.Library.Tests;

public class ClcApiClientTests
{
    [Fact]
    public void Constructor_ConfiguresBaseUrlAndApiKeyAuthenticator()
    {
        var client = new ClcApiClient("https://api.example.test", "test-api-key");

        var authenticator = Assert.IsType<HeaderApiKeyAuthenticator>(client.Authenticator);
        Assert.Equal("https://api.example.test", client.BaseUrl);
        Assert.Equal("test-api-key", authenticator.ApiKey);
        Assert.Equal("x-api-key", authenticator.HeaderName);
    }

    [Fact]
    public void CreateGetIpRequest_UsesExpectedRoute()
    {
        var request = ClcApiClient.CreateGetIpRequest();

        AssertRequest(request, HttpMethod.Get, "/ip");
        Assert.Null(request.Body);
    }

    [Fact]
    public void CreateGetItemRequest_UsesExpectedRoute()
    {
        var request = ClcApiClient.CreateGetItemRequest("i123");

        AssertRequest(request, HttpMethod.Get, "/item/i123");
        Assert.Null(request.Body);
    }

    [Fact]
    public void CreateGetInTransitItemsRequest_UsesExpectedRouteAndBody()
    {
        var barcodes = new[] { "item-1", "item-2" };
        var request = ClcApiClient.CreateGetInTransitItemsRequest(42, barcodes, includeLibrary: true);

        AssertRequest(request, HttpMethod.Post, "/items/in-transit/42?includeLibrary=True");
        Assert.Same(barcodes, request.Body);
    }

    [Fact]
    public void CreateGetInTransitItemsRequest_DefaultIncludeLibraryFlagIsFalse()
    {
        var barcodes = Array.Empty<string>();
        var request = ClcApiClient.CreateGetInTransitItemsRequest(42, barcodes);

        AssertRequest(request, HttpMethod.Post, "/items/in-transit/42?includeLibrary=False");
        Assert.Same(barcodes, request.Body);
    }

    [Fact]
    public void CreateGetPatronHoldsRequest_UsesExpectedRoute()
    {
        var request = ClcApiClient.CreateGetPatronHoldsRequest("p456");

        AssertRequest(request, HttpMethod.Get, "/patron/p456/holds");
        Assert.Null(request.Body);
    }

    [Fact]
    public void CreateGetPatronHeldItemsRequest_UsesExpectedRoute()
    {
        var request = ClcApiClient.CreateGetPatronHeldItemsRequest("p456");

        AssertRequest(request, HttpMethod.Get, "/patron/p456/held-items");
        Assert.Null(request.Body);
    }

    [Fact]
    public void CreateGetHoldingsRequest_UsesRepeatedBibIdQueryParameters()
    {
        var request = ClcApiClient.CreateGetHoldingsRequest(1001, 1002);

        AssertRequest(request, HttpMethod.Get, "/bib/holdings?bibid=1001&bibid=1002");
        Assert.Null(request.Body);
    }

    [Fact]
    public void CreateRemovePatronSmsDetailsRequest_UsesExpectedRouteAndBody()
    {
        var request = ClcApiClient.CreateRemovePatronSmsDetailsRequest("p456", "remove sms note");

        AssertRequest(request, HttpMethod.Delete, "/patron/p456/sms");
        var body = Assert.IsType<RemoveSmsDetailsData>(request.Body);
        Assert.Equal("remove sms note", body.Note);
    }

    [Fact]
    public void CreateGetPatronsForSmsNumberRequest_UsesExpectedRoute()
    {
        var request = ClcApiClient.CreateGetPatronsForSmsNumberRequest("6145550199");

        AssertRequest(request, HttpMethod.Get, "/sms-numbers/6145550199/patrons");
        Assert.Null(request.Body);
    }

    [Fact]
    public void CreateAddNonBlockingNoteRequest_UsesExpectedRouteAndBody()
    {
        var request = ClcApiClient.CreateAddNonBlockingNoteRequest("p456", "non-blocking note");

        AssertRequest(request, HttpMethod.Post, "/patron/p456/notes/non-blocking");
        var body = Assert.IsType<AddNonBlockingNoteData>(request.Body);
        Assert.Equal("non-blocking note", body.Note);
    }

    private static void AssertRequest(RestRequest request, HttpMethod method, string path)
    {
        Assert.Equal(method, request.Method);
        Assert.Equal(path, request.Path);
    }
}
