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

        var response = await client.GetIpAsync().ConfigureAwait(false);
        var request = await server.GetRequestAsync().ConfigureAwait(false);

        Assert.AreEqual("GET", request.Method);
        Assert.AreEqual("/ip", request.Path);
        AssertHeaderContains(request, "x-api-key", ApiKey);
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

        await client.GetInTransitItemsAsync(42).ConfigureAwait(false);
        var request = await server.GetRequestAsync().ConfigureAwait(false);

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
        using var server = new LoopbackHttpServer("[]", "application/json");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.GetInTransitItemsAsync(42, new[] { "1001", "1002" }, includeLibrary: true).ConfigureAwait(false);
        var request = await server.GetRequestAsync().ConfigureAwait(false);

        Assert.AreEqual("POST", request.Method);
        Assert.AreEqual("/items/in-transit/42", request.Path);
        StringAssert.Contains(request.RawQueryString, "includeLibrary=True");
        StringAssert.Contains(request.Body, "1001");
        StringAssert.Contains(request.Body, "1002");
    }

    [TestMethod]
    public async Task RemovePatronSmsDetailsAsync_Loopback_SendsDeleteWithJsonBody()
    {
        using var server = new LoopbackHttpServer("{\"errorCode\":0,\"errorMessage\":null}", "application/json");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.RemovePatronSmsDetailsAsync("patron-1", "removed sms details").ConfigureAwait(false);
        var request = await server.GetRequestAsync().ConfigureAwait(false);

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
        using var server = new LoopbackHttpServer("[]", "application/json");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        await client.GetHoldingsAsync(1, 2, 3).ConfigureAwait(false);
        var request = await server.GetRequestAsync().ConfigureAwait(false);

        Assert.AreEqual("GET", request.Method);
        Assert.AreEqual("/bib/holdings", request.Path);
        Assert.AreEqual("bibid=1&bibid=2&bibid=3", request.RawQueryString);
    }

    [TestMethod]
    public async Task GetItemAsync_Loopback_DeserializesJsonResponse()
    {
        using var server = new LoopbackHttpServer("{\"itemRecordID\":123,\"title\":\"The Test Item\",\"barcode\":\"item-barcode\"}", "application/json");
        var client = new ClcApiClient(server.BaseUrl, ApiKey);

        var response = await client.GetItemAsync("item-barcode").ConfigureAwait(false);
        await server.GetRequestAsync().ConfigureAwait(false);

        Assert.IsNull(response.Exception);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(123, response.Data.ItemRecordID);
        Assert.AreEqual("The Test Item", response.Data.Title);
        Assert.AreEqual("item-barcode", response.Data.Barcode);
    }

    private static void AssertHeaderContains(CapturedRequest request, string name, string expectedValue)
    {
        Assert.IsTrue(request.Headers.TryGetValue(name, out var values), $"Expected header '{name}' to be present.");
        CollectionAssert.Contains(values.ToList(), expectedValue);
    }

    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly TaskCompletionSource<CapturedRequest> capturedRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TcpListener listener;
        private readonly Thread serverThread;
        private readonly int statusCode;
        private readonly string contentType;
        private readonly string responseBody;
        private TcpClient? currentClient;

        public LoopbackHttpServer(string responseBody, string contentType, int statusCode = 200)
        {
            this.responseBody = responseBody;
            this.contentType = contentType;
            this.statusCode = statusCode;

            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}";

            serverThread = new Thread(HandleRequest) { IsBackground = true };
            serverThread.Start();
        }

        public string BaseUrl { get; }

        public Task<CapturedRequest> GetRequestAsync() => capturedRequest.Task;

        public void Dispose()
        {
            currentClient?.Close();
            listener.Stop();
        }

        private void HandleRequest()
        {
            try
            {
                using var client = listener.AcceptTcpClient();
                currentClient = client;
                using var stream = client.GetStream();

                var requestBytes = ReadRequestBytes(stream);
                var captured = CaptureRequest(requestBytes);
                capturedRequest.TrySetResult(captured);

                var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                var headerBytes = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {statusCode} {GetReasonPhrase(statusCode)}\r\n" +
                    $"Content-Type: {contentType}; charset=utf-8\r\n" +
                    $"Content-Length: {responseBytes.Length}\r\n" +
                    "Connection: close\r\n" +
                    "\r\n");

                stream.Write(headerBytes);
                stream.Write(responseBytes);
            }
            catch (Exception exception)
            {
                capturedRequest.TrySetException(exception);
            }
        }

        private static byte[] ReadRequestBytes(NetworkStream stream)
        {
            var buffer = new byte[1024];
            var request = new List<byte>();
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var bytesRead = stream.Read(buffer);
                if (bytesRead == 0)
                {
                    break;
                }

                request.AddRange(buffer.AsSpan(0, bytesRead).ToArray());
                headerEnd = FindHeaderEnd(request);
            }

            if (headerEnd < 0)
            {
                return request.ToArray();
            }

            var contentLength = GetContentLength(request, headerEnd);
            var totalLength = headerEnd + 4 + contentLength;
            while (request.Count < totalLength)
            {
                var bytesRead = stream.Read(buffer, 0, Math.Min(buffer.Length, totalLength - request.Count));
                if (bytesRead == 0)
                {
                    break;
                }

                request.AddRange(buffer.AsSpan(0, bytesRead).ToArray());
            }

            return request.ToArray();
        }

        private static CapturedRequest CaptureRequest(byte[] requestBytes)
        {
            var headerEnd = FindHeaderEnd(requestBytes);
            Assert.IsTrue(headerEnd >= 0, "Expected the loopback server to receive a complete HTTP request header.");

            var headersText = Encoding.ASCII.GetString(requestBytes, 0, headerEnd);
            var headerLines = headersText.Split("\r\n", StringSplitOptions.None);
            var requestLineParts = headerLines[0].Split(' ', 3);
            var target = requestLineParts[1];
            var queryStart = target.IndexOf('?');
            var path = queryStart < 0 ? target : target[..queryStart];
            var rawQueryString = queryStart < 0 ? string.Empty : target[(queryStart + 1)..];

            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var headerLine in headerLines.Skip(1))
            {
                var separatorIndex = headerLine.IndexOf(':');
                if (separatorIndex < 0)
                {
                    continue;
                }

                var headerName = headerLine[..separatorIndex];
                var headerValue = headerLine[(separatorIndex + 1)..].TrimStart();
                headers[headerName] = headers.TryGetValue(headerName, out var existingValues)
                    ? existingValues.Append(headerValue).ToArray()
                    : [headerValue];
            }

            var bodyStart = headerEnd + 4;
            var bodyLength = Math.Max(0, requestBytes.Length - bodyStart);
            var body = Encoding.UTF8.GetString(requestBytes, bodyStart, bodyLength);

            return new CapturedRequest(requestLineParts[0], path, rawQueryString, headers, body);
        }

        private static int FindHeaderEnd(IReadOnlyList<byte> request)
        {
            for (var i = 0; i <= request.Count - 4; i++)
            {
                if (request[i] == '\r' && request[i + 1] == '\n' && request[i + 2] == '\r' && request[i + 3] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int GetContentLength(IReadOnlyList<byte> request, int headerEnd)
        {
            var headersText = Encoding.ASCII.GetString(request.Take(headerEnd).ToArray());
            foreach (var headerLine in headersText.Split("\r\n", StringSplitOptions.None).Skip(1))
            {
                var separatorIndex = headerLine.IndexOf(':');
                if (separatorIndex < 0 || !headerLine[..separatorIndex].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return int.Parse(headerLine[(separatorIndex + 1)..].Trim());
            }

            return 0;
        }

        private static string GetReasonPhrase(int statusCode) => statusCode switch
        {
            200 => "OK",
            201 => "Created",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Loopback Response"
        };
    }

    private sealed record CapturedRequest(
        string Method,
        string Path,
        string RawQueryString,
        Dictionary<string, string[]> Headers,
        string Body);
}
