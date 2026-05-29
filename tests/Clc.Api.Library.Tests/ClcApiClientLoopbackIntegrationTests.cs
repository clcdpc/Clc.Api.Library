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
    private const string ApiKey = "loopback-test-api-key";

    [TestMethod]
    public async Task GetIpAsync_Loopback_SendsApiKeyAndReturnsRawText()
    {
        using var server = new LoopbackHttpServer("203.0.113.10", "text/plain");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetIpAsync();

        Assert.AreEqual("GET", server.Request.Method);
        Assert.AreEqual("/ip", server.Request.Path);
        CollectionAssert.Contains(server.Request.Headers["x-api-key"].ToList(), ApiKey);
        Assert.IsNull(response.Exception);
        Assert.AreEqual("203.0.113.10", response.Data);
        Assert.IsNotNull(response.Response);
        Assert.IsTrue(response.Response.IsSuccessStatusCode);
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_DefaultOverload_Loopback_PostsEmptyBarcodeArray()
    {
        using var server = new LoopbackHttpServer("[]", "application/json");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.GetInTransitItemsAsync(42);

        Assert.AreEqual("POST", server.Request.Method);
        Assert.AreEqual("/items/in-transit/42", server.Request.Path);
        Assert.AreEqual("includeLibrary=False", server.Request.RawQueryString);
        using var body = JsonDocument.Parse(server.Request.Body);
        Assert.AreEqual(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.AreEqual(0, body.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task GetInTransitItemsAsync_WithBarcodes_Loopback_PostsBarcodeArray()
    {
        using var server = new LoopbackHttpServer("[]", "application/json");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.GetInTransitItemsAsync(42, ["1001", "1002"], includeLibrary: true);

        Assert.AreEqual("POST", server.Request.Method);
        Assert.AreEqual("/items/in-transit/42", server.Request.Path);
        Assert.AreEqual("includeLibrary=True", server.Request.RawQueryString);
        StringAssert.Contains(server.Request.Body, "1001");
        StringAssert.Contains(server.Request.Body, "1002");
    }

    [TestMethod]
    public async Task RemovePatronSmsDetailsAsync_Loopback_SendsDeleteWithJsonBody()
    {
        using var server = new LoopbackHttpServer("{\"errorCode\":0,\"errorMessage\":null}", "application/json");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.RemovePatronSmsDetailsAsync("patron-1", "removed sms details");

        Assert.AreEqual("DELETE", server.Request.Method);
        Assert.AreEqual("/patron/patron-1/sms", server.Request.Path);
        Assert.IsFalse(string.IsNullOrWhiteSpace(server.Request.Body));
        using var body = JsonDocument.Parse(server.Request.Body);
        Assert.AreEqual(JsonValueKind.Object, body.RootElement.ValueKind);
        StringAssert.Contains(server.Request.Body, "removed sms details");
    }

    [TestMethod]
    public async Task GetHoldingsAsync_Loopback_PreservesRepeatedBibIdQueryParameters()
    {
        using var server = new LoopbackHttpServer("[]", "application/json");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.GetHoldingsAsync(1, 2, 3);

        Assert.AreEqual("GET", server.Request.Method);
        Assert.AreEqual("/bib/holdings", server.Request.Path);
        Assert.AreEqual("bibid=1&bibid=2&bibid=3", server.Request.RawQueryString);
    }

    [TestMethod]
    public async Task GetItemAsync_Loopback_DeserializesJsonResponse()
    {
        using var server = new LoopbackHttpServer("{\"itemRecordID\":123,\"title\":\"The Test Item\",\"barcode\":\"item-barcode\"}", "application/json");
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
        private readonly TcpListener listener;
        private readonly Task serverTask;
        private readonly string responseBody;
        private readonly string contentType;
        private readonly int statusCode;
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private CapturedRequest? capturedRequest;

        public LoopbackHttpServer(string responseBody, string contentType, int statusCode = 200)
        {
            this.responseBody = responseBody;
            this.contentType = contentType;
            this.statusCode = statusCode;

            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            BaseUrl = $"http://127.0.0.1:{endpoint.Port}";
            serverTask = Task.Run(() => ServeOneRequestAsync(cancellationTokenSource.Token));
        }

        public string BaseUrl { get; }

        public CapturedRequest Request
        {
            get
            {
                Assert.IsTrue(serverTask.Wait(TimeSpan.FromSeconds(5)), "Timed out waiting for loopback server to capture a request.");
                if (serverTask.IsFaulted)
                {
                    throw serverTask.Exception!;
                }

                Assert.IsNotNull(capturedRequest, "Expected the loopback server to capture a request.");
                return capturedRequest;
            }
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            listener.Stop();
            try
            {
                serverTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(IsExpectedShutdownException))
            {
            }

            cancellationTokenSource.Dispose();
        }

        private async Task ServeOneRequestAsync(CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();

            capturedRequest = await ReadRequestAsync(stream, cancellationToken);
            await WriteResponseAsync(stream, cancellationToken);
        }

        private async Task<CapturedRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var headerBytes = await ReadHeadersAsync(stream, cancellationToken);
            var headerText = Encoding.ASCII.GetString(headerBytes);
            var headerLines = headerText.Split("\r\n", StringSplitOptions.None);
            var requestParts = headerLines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var target = requestParts[1];
            var queryIndex = target.IndexOf('?');
            var path = queryIndex >= 0 ? target[..queryIndex] : target;
            var rawQueryString = queryIndex >= 0 ? target[(queryIndex + 1)..] : string.Empty;
            var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var headerLine in headerLines.Skip(1).Where(line => line.Length > 0))
            {
                var separatorIndex = headerLine.IndexOf(':');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var name = headerLine[..separatorIndex].Trim();
                var value = headerLine[(separatorIndex + 1)..].Trim();
                if (!headers.TryGetValue(name, out var values))
                {
                    values = [];
                    headers.Add(name, values);
                }

                values.Add(value);
            }

            var contentLength = headers.TryGetValue("Content-Length", out var contentLengthValues)
                ? int.Parse(contentLengthValues.Single())
                : 0;
            var bodyBytes = new byte[contentLength];
            var offset = 0;
            while (offset < bodyBytes.Length)
            {
                var read = await stream.ReadAsync(bodyBytes.AsMemory(offset, bodyBytes.Length - offset), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            return new CapturedRequest(
                requestParts[0],
                path,
                rawQueryString,
                headers,
                Encoding.UTF8.GetString(bodyBytes, 0, offset));
        }

        private static async Task<byte[]> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            var current = new byte[1];
            while (true)
            {
                var read = await stream.ReadAsync(current, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                buffer.WriteByte(current[0]);
                var bytes = buffer.GetBuffer();
                var length = buffer.Length;
                if (length >= 4 && bytes[length - 4] == '\r' && bytes[length - 3] == '\n' && bytes[length - 2] == '\r' && bytes[length - 1] == '\n')
                {
                    return buffer.ToArray();
                }
            }

            return buffer.ToArray();
        }

        private async Task WriteResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(responseBody);
            var reasonPhrase = statusCode == 200 ? "OK" : "Test Response";
            var responseHeaders = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
                $"Content-Type: {contentType}; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n" +
                "\r\n");

            await stream.WriteAsync(responseHeaders, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static bool IsExpectedShutdownException(Exception exception) =>
            exception is OperationCanceledException or ObjectDisposedException or SocketException;
    }

    private sealed record CapturedRequest(
        string Method,
        string Path,
        string RawQueryString,
        Dictionary<string, List<string>> Headers,
        string Body);
}
