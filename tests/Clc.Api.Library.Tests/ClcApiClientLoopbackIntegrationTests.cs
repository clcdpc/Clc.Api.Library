using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Clc.Api.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Clc.Api.Library.Tests;

[TestClass]
[DoNotParallelize]
public class ClcApiClientLoopbackIntegrationTests
{
    private const string ApiKey = "loopback-api-key";

    [TestMethod]
    public async Task GetIpAsync_Loopback_SendsApiKeyAndReturnsRawText()
    {
        using var server = new LoopbackHttpServer(HttpStatusCode.OK, "text/plain", "203.0.113.10");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetIpAsync();

        var request = AssertHasRequest(server);
        Assert.AreEqual("GET", request.Method);
        Assert.AreEqual("/ip", request.Path);
        CollectionAssert.Contains(request.Headers["x-api-key"].ToList(), ApiKey);
        Assert.IsNull(response.Exception);
        Assert.AreEqual("203.0.113.10", response.Data);
        Assert.IsNotNull(response.Response);
        Assert.IsTrue(response.Response.IsSuccessStatusCode);
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_DefaultOverload_Loopback_PostsEmptyBarcodeArray()
    {
        using var server = new LoopbackHttpServer(HttpStatusCode.OK, "application/json", "[]");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetInTransitItemsAsync(42);

        AssertResponseSucceeded(response);

        var request = AssertHasRequest(server);
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
        using var server = new LoopbackHttpServer(HttpStatusCode.OK, "application/json", "[]");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetInTransitItemsAsync(42, new[] { "1001", "1002" }, includeLibrary: true);

        AssertResponseSucceeded(response);

        var request = AssertHasRequest(server);
        Assert.AreEqual("POST", request.Method);
        Assert.AreEqual("/items/in-transit/42", request.Path);
        StringAssert.Contains(request.RawQueryString, "includeLibrary=True");
        StringAssert.Contains(request.Body, "1001");
        StringAssert.Contains(request.Body, "1002");
    }

    [TestMethod]
    public async Task RemovePatronSmsDetailsAsync_Loopback_SendsDeleteWithJsonBody()
    {
        using var server = new LoopbackHttpServer(HttpStatusCode.OK, "application/json", "{\"errorCode\":0,\"errorMessage\":null}");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.RemovePatronSmsDetailsAsync("patron-1", "removed sms details");

        AssertResponseSucceeded(response);

        var request = AssertHasRequest(server);
        Assert.AreEqual("DELETE", request.Method);
        Assert.AreEqual("/patron/patron-1/sms", request.Path);
        Assert.IsFalse(string.IsNullOrWhiteSpace(request.Body));
        using var body = JsonDocument.Parse(request.Body);
        Assert.AreEqual(JsonValueKind.Object, body.RootElement.ValueKind);
        StringAssert.Contains(request.Body, "removed sms details");
    }

    [TestMethod]
    public async Task GetHoldingsAsync_Loopback_PreservesRepeatedBibIdQueryParameters()
    {
        using var server = new LoopbackHttpServer(HttpStatusCode.OK, "application/json", "[]");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetHoldingsAsync(1, 2, 3);

        AssertResponseSucceeded(response);

        var request = AssertHasRequest(server);
        Assert.AreEqual("GET", request.Method);
        Assert.AreEqual("/bib/holdings", request.Path);
        Assert.AreEqual("bibid=1&bibid=2&bibid=3", request.RawQueryString);
    }

    [TestMethod]
    public async Task GetItemAsync_Loopback_DeserializesJsonResponse()
    {
        using var server = new LoopbackHttpServer(
            HttpStatusCode.OK,
            "application/json",
            "{\"itemRecordID\":123,\"title\":\"The Test Item\",\"barcode\":\"item-barcode\"}");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetItemAsync("item-barcode");

        AssertResponseSucceeded(response);
        Assert.IsNull(response.Exception);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(123, response.Data.ItemRecordID);
        Assert.AreEqual("The Test Item", response.Data.Title);
        Assert.AreEqual("item-barcode", response.Data.Barcode);
    }

    private static void AssertResponseSucceeded<T>(Clc.Rest.IRestResponse<T> response)
    {
        Assert.IsNull(response.Exception, response.Exception?.ToString());
        Assert.IsNotNull(response.Response);
        Assert.IsTrue(response.Response.IsSuccessStatusCode);
    }

    private static CapturedRequest AssertHasRequest(LoopbackHttpServer server)
    {
        if (server.LastRequest is null && server.ServerException is not null)
        {
            Assert.Fail(server.ServerException.ToString());
        }

        Assert.IsNotNull(server.LastRequest);
        return server.LastRequest;
    }

    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly TcpListener listener;
        private readonly Task serverTask;
        private readonly HttpStatusCode statusCode;
        private readonly string contentType;
        private readonly string responseBody;
        private readonly string? originalNoProxy;
        private readonly IWebProxy originalProxy;

        public LoopbackHttpServer(HttpStatusCode statusCode, string contentType, string responseBody)
        {
            this.statusCode = statusCode;
            this.contentType = contentType;
            this.responseBody = responseBody;
            originalNoProxy = Environment.GetEnvironmentVariable("NO_PROXY");
            originalProxy = HttpClient.DefaultProxy;
            Environment.SetEnvironmentVariable("NO_PROXY", AppendNoProxyLoopback(originalNoProxy));
            HttpClient.DefaultProxy = new LoopbackBypassProxy(originalProxy);
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}";
            serverTask = RunAsync(cancellationTokenSource.Token);
        }

        public string BaseUrl { get; }

        public CapturedRequest? LastRequest { get; private set; }

        public Exception? ServerException => serverTask.IsFaulted ? serverTask.Exception : null;

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            listener.Stop();
            try
            {
                serverTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            HttpClient.DefaultProxy = originalProxy;
            Environment.SetEnvironmentVariable("NO_PROXY", originalNoProxy);
            cancellationTokenSource.Dispose();
        }

        private static string AppendNoProxyLoopback(string? noProxy)
        {
            const string loopbackBypasses = "127.0.0.1,localhost,::1";
            return string.IsNullOrWhiteSpace(noProxy) ? loopbackBypasses : $"{noProxy},{loopbackBypasses}";
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            LastRequest = await ReadRequestAsync(stream, cancellationToken);
            await WriteResponseAsync(stream, cancellationToken);
        }

        private static async Task<CapturedRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken);
            Assert.IsFalse(string.IsNullOrWhiteSpace(requestLine));

            var requestParts = requestLine.Split(' ');
            Assert.IsTrue(requestParts.Length >= 2, $"Unexpected HTTP request line: {requestLine}");
            var target = requestParts[1];
            var queryStart = target.IndexOf('?');
            var path = queryStart < 0 ? target : target[..queryStart];
            var rawQueryString = queryStart < 0 ? string.Empty : target[(queryStart + 1)..];

            var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
            {
                var separator = line.IndexOf(':');
                Assert.IsTrue(separator > 0, $"Unexpected HTTP header line: {line}");
                var name = line[..separator];
                var value = line[(separator + 1)..].Trim();
                if (!headers.TryGetValue(name, out var values))
                {
                    values = [];
                    headers.Add(name, values);
                }

                values.Add(value);
            }

            var body = string.Empty;
            if (headers.TryGetValue("Content-Length", out var contentLengthValues)
                && int.TryParse(contentLengthValues.Last(), out var contentLength)
                && contentLength > 0)
            {
                var bodyBuffer = new char[contentLength];
                var totalRead = 0;
                while (totalRead < contentLength)
                {
                    var read = await reader.ReadAsync(bodyBuffer.AsMemory(totalRead, contentLength - totalRead), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    totalRead += read;
                }

                body = new string(bodyBuffer, 0, totalRead);
            }

            return new CapturedRequest(requestParts[0], path, rawQueryString, headers, body);
        }

        private async Task WriteResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(responseBody);
            var reasonPhrase = statusCode == HttpStatusCode.OK ? "OK" : statusCode.ToString();
            var header = $"HTTP/1.1 {(int)statusCode} {reasonPhrase}\r\n"
                + $"Content-Type: {contentType}; charset=utf-8\r\n"
                + $"Content-Length: {bodyBytes.Length}\r\n"
                + "Connection: close\r\n"
                + "\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
    }

    private sealed class LoopbackBypassProxy(IWebProxy innerProxy) : IWebProxy
    {
        public ICredentials? Credentials
        {
            get => innerProxy.Credentials;
            set => innerProxy.Credentials = value;
        }

        public Uri GetProxy(Uri destination) => IsLoopback(destination) ? destination : innerProxy.GetProxy(destination) ?? destination;

        public bool IsBypassed(Uri host) => IsLoopback(host) || innerProxy.IsBypassed(host);

        private static bool IsLoopback(Uri uri) => uri.IsLoopback || uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "::1";
    }

    private sealed record CapturedRequest(
        string Method,
        string Path,
        string RawQueryString,
        Dictionary<string, List<string>> Headers,
        string Body);
}
