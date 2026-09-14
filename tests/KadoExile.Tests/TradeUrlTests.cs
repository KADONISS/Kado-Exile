using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KadoExile.Services;
using Xunit;

namespace KadoExile.Tests;

public sealed class TradeUrlTests
{
    public const string SampleTradeUrl = "https://www.pathofexile.com/trade/search/Allflame/H4sIAAAAAAAACnWNwQrDIBBE_2XO3kpJ8VdKkG2yJYIxopvSIvvvwVCKl85phmHeVBQh2QtsxZbEbxEWhac90yMw1Jx9gb1XyCcxLCjOMHj6IJxbMerYxYqVkutiZ6eFsriyUON0f9f2Q5nJBX5xOCE-wt4uBiu9m1FVA8k08x92yn7i33L4DgdtOgAv71BE5QAAAA";

    [Fact]
    public void ParsesEmbeddedQueryFromPoeTradeUrl()
    {
        Assert.True(PoeTradeClient.TryParseTradeUrl(SampleTradeUrl, out var context, out var error), error);
        Assert.Equal("Allflame", context!.League);
        Assert.Null(context.QueryId);

        using var query = JsonDocument.Parse(context.EmbeddedQueryJson!);
        var root = query.RootElement;
        Assert.Equal("securable", root.GetProperty("status").GetProperty("option").GetString());
        var filters = root.GetProperty("filters");
        Assert.Equal("5", filters.GetProperty("map_filters").GetProperty("filters")
            .GetProperty("chart_shape").GetProperty("option").GetString());
        Assert.Equal(83, filters.GetProperty("map_filters").GetProperty("filters")
            .GetProperty("area_level").GetProperty("min").GetInt32());
        Assert.Equal(7, filters.GetProperty("trade_filters").GetProperty("filters")
            .GetProperty("price").GetProperty("min").GetInt32());
    }

    [Fact]
    public async Task EmbeddedQueryPostsDirectlyAndFetchesWithReturnedSearchId()
    {
        Assert.True(PoeTradeClient.TryParseTradeUrl(SampleTradeUrl, out var context, out var error), error);
        var requests = new List<(HttpMethod Method, string Path, string? Body)>();
        using var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            if (request.Method == HttpMethod.Post)
                return JsonResponse("{\"id\":\"generated-search-id\",\"result\":[\"listing-id\"]}");
            return JsonResponse("{\"result\":[]}");
        });
        using var client = new PoeTradeClient("test-session", handler);

        Assert.Empty(await client.LoadListingsAsync(context!, 10, CancellationToken.None));

        Assert.Collection(requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/api/trade/search/Allflame", request.Path);
                using var body = JsonDocument.Parse(request.Body!);
                var query = body.RootElement.GetProperty("query");
                Assert.Equal("securable", query.GetProperty("status").GetProperty("option").GetString());
                Assert.Equal("asc", body.RootElement.GetProperty("sort").GetProperty("price").GetString());
                var filters = query.GetProperty("filters");
                Assert.Equal("5", filters.GetProperty("map_filters").GetProperty("filters")
                    .GetProperty("chart_shape").GetProperty("option").GetString());
                Assert.Equal(83, filters.GetProperty("map_filters").GetProperty("filters")
                    .GetProperty("area_level").GetProperty("min").GetInt32());
                Assert.Equal(7, filters.GetProperty("trade_filters").GetProperty("filters")
                    .GetProperty("price").GetProperty("min").GetInt32());
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/api/trade/fetch/listing-id?query=generated-search-id", request.Path);
            });
    }

    [Fact]
    public async Task LegacySavedSearchStillLoadsDefinitionBeforeExecuting()
    {
        Assert.True(PoeTradeClient.TryParseTradeUrl(
            "https://www.pathofexile.com/trade/search/Allflame/abc123", out var context, out var error), error);
        var requests = new List<(HttpMethod Method, string Path, string? Body)>();
        using var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return request.Method == HttpMethod.Post
                ? JsonResponse("{\"id\":\"rerun-search-id\",\"result\":[\"listing-id\"]}")
                : request.RequestUri!.AbsolutePath == "/api/trade/search/Allflame/abc123"
                    ? JsonResponse("{\"query\":{\"status\":{\"option\":\"securable\"},\"stats\":[]}}")
                    : JsonResponse("{\"result\":[]}");
        });
        using var client = new PoeTradeClient("test-session", handler);

        await client.LoadListingsAsync(context!, 10, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Equal("/api/trade/search/Allflame/abc123", requests[0].Path);
        Assert.Equal(HttpMethod.Post, requests[1].Method);
        Assert.Equal("/api/trade/search/Allflame", requests[1].Path);
        Assert.Equal("/api/trade/fetch/listing-id?query=rerun-search-id", requests[2].Path);
    }

    [Fact]
    public void RejectsMalformedEmbeddedQueries()
    {
        var invalidLinks = new[]
        {
            TradeSearchUrl + "H4sIinvalid_payload!",
            SampleTradeUrl[..^8],
            RawLink(Encoding.UTF8.GetBytes("not gzip data, but long enough to be treated as a query")),
            EmbeddedLink(Encoding.UTF8.GetBytes("not json")),
            EmbeddedLink([0xff, 0xfe, 0xfd]),
            EmbeddedLink(Encoding.UTF8.GetBytes("{\"unrecognized\":true}")),
            EmbeddedLink(Encoding.UTF8.GetBytes("[]"))
        };

        foreach (var link in invalidLinks)
        {
            Assert.False(PoeTradeClient.TryParseTradeUrl(link, out _, out var error), link);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void RejectsOversizedTradeLinksAndInflatedQueries()
    {
        var overlongUrl = TradeSearchUrl + new string('a', TradeUrlValidator.MaximumLength);
        Assert.False(PoeTradeClient.TryParseTradeUrl(overlongUrl, out _, out var lengthError));
        Assert.Contains("16384", lengthError);

        Assert.False(PoeTradeClient.TryParseTradeUrl(RawLink(new byte[8 * 1024 + 1]), out _, out var compressedError));
        Assert.Contains("kích thước", compressedError);

        var oversizedQuery = "{\"status\":{\"option\":\"securable\"},\"term\":\"" +
            new string('x', 1_048_577) + "\"}";
        Assert.False(PoeTradeClient.TryParseTradeUrl(EmbeddedLink(Encoding.UTF8.GetBytes(oversizedQuery)), out _, out var queryError));
        Assert.Contains("giải nén", queryError);
    }

    [Fact]
    public void RejectsNonPoeHost()
    {
        Assert.False(PoeTradeClient.TryParseTradeUrl(
            "https://example.com/trade/search/Keepers/abc123", out _, out _));
    }

    [Fact]
    public void ParsesPoeTwoSavedSearch()
    {
        Assert.True(PoeTradeClient.TryParseTradeUrl(
            "https://www.pathofexile.com/trade2/search/poe2/Standard/abc123", out var context, out var error), error);
        Assert.Equal("trade2", context!.ApiPrefix);
        Assert.Equal("poe2", context.RealmPrefix);
        Assert.Equal("Standard", context.League);
        Assert.Equal("abc123", context.QueryId);
    }

    [Theory]
    [InlineData("www.pathofexile.com/trade/search/Allflame/abc123")]
    [InlineData("\"https://www.pathofexile.com/trade/search/Allflame/abc123\"")]
    [InlineData("\u200Bhttps://www.pathofexile.com/trade/search/Allflame/abc123")]
    public void NormalizesCommonCopiedTradeLinks(string value)
    {
        Assert.True(PoeTradeClient.TryParseTradeUrl(value, out var context, out var error), error);
        Assert.Equal("Allflame", context!.League);
        Assert.Equal("abc123", context.QueryId);
    }

    private const string TradeSearchUrl = "https://www.pathofexile.com/trade/search/Allflame/";

    private static string EmbeddedLink(byte[] payload) => TradeSearchUrl + Base64Url(Gzip(payload));

    private static string RawLink(byte[] payload) => TradeSearchUrl + Base64Url(payload);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
