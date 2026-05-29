using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Clc.Api.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clc.Api.Library.Tests;

[TestClass]
public class ClcApiClientLoopbackIntegrationTests
{
    private const string ApiKey = "loopback-api-key";

    [TestMethod]
    public async Task GetIpAsync_Loopback_SendsApiKeyAndReturnsRawText()
    {
        using var server = LoopbackHttpServer.Start(HttpStatusCode.OK, "text/plain", "203.0.113.10");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetIpAsync();
        var request = await server.GetRequestAsync();

        Assert.AreEqual("GET", request.Method);
        Assert.AreEqual("/ip", request.Path);
        CollectionAssert.Contains(request.Headers.GetValues("x-api-key")?.ToList(), ApiKey);
        Assert.IsNull(response.Exception);
        Assert.AreEqual("203.0.113.10", response.Data);
        Assert.IsNotNull(response.Response);
        Assert.IsTrue(response.Response.IsSuccessStatusCode);
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_DefaultOverload_Loopback_PostsEmptyBarcodeArray()
    {
        using var server = LoopbackHttpServer.Start(HttpStatusCode.OK, "application/json", "[]");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.GetInTransitItemsAsync(42);
        var request = await server.GetRequestAsync();

        Assert.AreEqual("POST", request.Method);
        Assert.AreEqual("/items/in-transit/42", request.Path);
        StringAssert.Contains(request.RawQueryString, "includeLibrary=False");
        using var body = JsonDocument.Parse(request.Body);
        Assert.AreEqual(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.AreEqual(0, body.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithBarcodes_Loopback_PostsBarcodeArray()
    {
        using var server = LoopbackHttpServer.Start(HttpStatusCode.OK, "application/json", "[]");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.GetInTransitItemsAsync(42, new[] { "1001", "1002" }, includeLibrary: true);
        var request = await server.GetRequestAsync();

        Assert.AreEqual("POST", request.Method);
        Assert.AreEqual("/items/in-transit/42", request.Path);
        StringAssert.Contains(request.RawQueryString, "includeLibrary=True");
        StringAssert.Contains(request.Body, "1001");
        StringAssert.Contains(request.Body, "1002");
    }

    [TestMethod]
    public async Task RemovePatronSmsDetailsAsync_Loopback_SendsDeleteWithJsonBody()
    {
        using var server = LoopbackHttpServer.Start(HttpStatusCode.OK, "application/json", "{\"errorCode\":0,\"errorMessage\":null}");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.RemovePatronSmsDetailsAsync("patron-1", "removed sms details");
        var request = await server.GetRequestAsync();

        Assert.AreEqual("DELETE", request.Method);
        Assert.AreEqual("/patron/patron-1/sms", request.Path);
        Assert.IsFalse(string.IsNullOrWhiteSpace(request.Body));
        StringAssert.Contains(request.Body, "removed sms details");
    }

    [TestMethod]
    public async Task GetHoldingsAsync_Loopback_PreservesRepeatedBibIdQueryParameters()
    {
        using var server = LoopbackHttpServer.Start(HttpStatusCode.OK, "application/json", "[]");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.GetHoldingsAsync(1, 2, 3);
        var request = await server.GetRequestAsync();

        Assert.AreEqual("GET", request.Method);
        Assert.AreEqual("/bib/holdings", request.Path);
        Assert.AreEqual("bibid=1&bibid=2&bibid=3", request.RawQueryString);
    }

    [TestMethod]
    public async Task GetItemAsync_Loopback_DeserializesJsonResponse()
    {
        using var server = LoopbackHttpServer.Start(
            HttpStatusCode.OK,
            "application/json",
            "{\"itemRecordID\":123,\"title\":\"The Test Item\",\"barcode\":\"item-barcode\"}");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetItemAsync("item-barcode");

        Assert.IsNull(response.Exception);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(123, response.Data.ItemRecordID);
        Assert.AreEqual("The Test Item", response.Data.Title);
        Assert.AreEqual("item-barcode", response.Data.Barcode);
    }

    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly HttpStatusCode statusCode;
        private readonly string contentType;
        private readonly string responseBody;
        private readonly Task listenTask;
        private readonly TaskCompletionSource<LoopbackRequest> requestCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private LoopbackHttpServer(HttpStatusCode statusCode, string contentType, string responseBody)
        {
            this.statusCode = statusCode;
            this.contentType = contentType;
            this.responseBody = responseBody;

            BaseUrl = $"http://127.0.0.1:{GetFreeTcpPort()}";
            listener.Prefixes.Add($"{BaseUrl}/");
            listener.Start();
            listenTask = Task.Run(HandleSingleRequestAsync);
        }

        public string BaseUrl { get; }

        public static LoopbackHttpServer Start(HttpStatusCode statusCode, string contentType, string responseBody) =>
            new(statusCode, contentType, responseBody);

        public async Task<LoopbackRequest> GetRequestAsync() =>
            await requestCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Dispose()
        {
            listener.Close();

            try
            {
                listenTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(IsExpectedDisposeException))
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task HandleSingleRequestAsync()
        {
            try
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;
                var body = await ReadRequestBodyAsync(request);

                requestCompletion.SetResult(new LoopbackRequest(
                    request.HttpMethod,
                    request.Url?.AbsolutePath ?? string.Empty,
                    request.Url?.Query.TrimStart('?') ?? string.Empty,
                    request.Headers,
                    body));

                var buffer = Encoding.UTF8.GetBytes(responseBody);
                context.Response.StatusCode = (int)statusCode;
                context.Response.ContentType = contentType;
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
            }
            catch (Exception ex) when (IsExpectedDisposeException(ex))
            {
                requestCompletion.TrySetCanceled();
            }
            catch (Exception ex)
            {
                requestCompletion.TrySetException(ex);
                throw;
            }
        }

        private static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(
                request.InputStream,
                request.ContentEncoding ?? Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: false);

            return await reader.ReadToEndAsync();
        }

        private static int GetFreeTcpPort()
        {
            using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        }

        private static bool IsExpectedDisposeException(Exception ex) =>
            ex is ObjectDisposedException or HttpListenerException or InvalidOperationException;
    }

    private sealed record LoopbackRequest(
        string Method,
        string Path,
        string RawQueryString,
        NameValueCollection Headers,
        string Body);
}
