using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using KadoExile.Models;

namespace KadoExile.Services;

public sealed class PoeTradeClient : IDisposable
{
	private const int MaximumCompressedQueryBytes = 8 * 1024;
	private const int MaximumExpandedQueryBytes = 1024 * 1024;
	private const int EmbeddedQuerySegmentThreshold = 16;
	private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
	private static readonly string[] QueryProperties = new string[6] { "status", "stats", "filters", "name", "type", "term" };

	private readonly HttpClient _httpClient;

	public static int NormalizeListingLimit(int requested)
	{
		return Math.Clamp(requested, 1, 100);
	}

	public PoeTradeClient(string poeSessionId)
		: this(poeSessionId, null)
	{
	}

	internal PoeTradeClient(string poeSessionId, HttpMessageHandler? handler)
	{
		if (string.IsNullOrWhiteSpace(poeSessionId))
		{
			throw new ArgumentException("POESESSID chưa được cấu hình.", "poeSessionId");
		}
		if (handler == null)
		{
			HttpClientHandler httpClientHandler = new HttpClientHandler
			{
				AutomaticDecompression = DecompressionMethods.All,
				CookieContainer = new CookieContainer()
			};
			httpClientHandler.CookieContainer.Add(new Uri("https://www.pathofexile.com"), new Cookie("POESESSID", poeSessionId.Trim(), "/", ".pathofexile.com")
			{
				HttpOnly = true,
				Secure = true
			});
			handler = httpClientHandler;
		}
		_httpClient = new HttpClient(handler)
		{
			BaseAddress = new Uri("https://www.pathofexile.com"),
			Timeout = TimeSpan.FromSeconds(30L)
		};
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Kado-Exile/0.6.0 (ExileAPI plugin)");
		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_httpClient.DefaultRequestHeaders.Referrer = new Uri("https://www.pathofexile.com/trade");
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.pathofexile.com");
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
	}

	public static bool TryParseTradeUrl(string value, out TradeSearchContext? context, out string error)
	{
		context = null;
		error = string.Empty;
		if (!TradeUrlValidator.TryValidate(value, out Uri uri, out error))
		{
			return false;
		}
		string[] array = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 4 && array[0].Equals("trade", StringComparison.OrdinalIgnoreCase) && array[1].Equals("search", StringComparison.OrdinalIgnoreCase))
		{
			string league = Uri.UnescapeDataString(array[2]);
			string segment = Uri.UnescapeDataString(array[3]);
			if (LooksLikeEmbeddedQuery(segment))
			{
				if (!TryDecodeEmbeddedQuery(segment, out string? queryJson, out error))
				{
					return false;
				}
				context = new TradeSearchContext("trade", string.Empty, league, null, queryJson);
				return true;
			}
			context = new TradeSearchContext("trade", string.Empty, league, segment);
			return true;
		}
		if (array.Length == 5 && array[0].Equals("trade2", StringComparison.OrdinalIgnoreCase) && array[1].Equals("search", StringComparison.OrdinalIgnoreCase))
		{
			context = new TradeSearchContext("trade2", Uri.UnescapeDataString(array[2]), Uri.UnescapeDataString(array[3]), Uri.UnescapeDataString(array[4]));
			return true;
		}
		error = "Link phải là link kết quả tìm kiếm có league và query hợp lệ.";
		return false;
	}

	public async Task<IReadOnlyList<TradeListing>> LoadListingsAsync(TradeSearchContext context, int maximum, CancellationToken cancellationToken)
	{
		JsonObject jsonObject;
		if (context.EmbeddedQueryJson != null)
		{
			jsonObject = JsonNode.Parse(context.EmbeddedQueryJson) as JsonObject ?? throw new InvalidOperationException("Query Trade nhúng không phải JSON object.");
		}
		else
		{
			string path = BuildSavedSearchPath(context);
			using JsonDocument definitionDocument = JsonDocument.Parse(await GetWithRateLimitRetryAsync(path, "đọc định nghĩa tìm kiếm", cancellationToken));
			if (!definitionDocument.RootElement.TryGetProperty("query", out var value) || value.ValueKind != JsonValueKind.Object)
			{
				throw new InvalidOperationException("Link Trade không trả về query hợp lệ. Hãy dùng link sau khi đã bấm Search.");
			}
			jsonObject = (JsonNode.Parse(value.GetRawText()) as JsonObject) ?? throw new InvalidOperationException("Không thể parse query đã lưu.");
		}
		jsonObject.Remove<string, JsonNode>("pricetype", out var value2);
		JsonObject jsonObject2 = new JsonObject
		{
			["query"] = jsonObject,
			["sort"] = new JsonObject { ["price"] = "asc" }
		};
		if (value2 != null)
		{
			jsonObject2["pricetype"] = value2;
		}
		string content = jsonObject2.ToJsonString();
		using StringContent executeContent = new StringContent(content, Encoding.UTF8, "application/json");
		using HttpResponseMessage executeResponse = await _httpClient.PostAsync(BuildExecuteSearchPath(context), executeContent, cancellationToken);
		using JsonDocument executeDocument = JsonDocument.Parse(await ReadOrThrowAsync(executeResponse, "thực thi tìm kiếm", cancellationToken));
		if (!executeDocument.RootElement.TryGetProperty("result", out var value4) || value4.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidOperationException("GGG không trả về mảng result sau khi thực thi query.");
		}
		string[] array = (from element in value4.EnumerateArray()
			select element.GetString() into id
			where !string.IsNullOrWhiteSpace(id)
			select id).Take(NormalizeListingLimit(maximum)).Cast<string>().ToArray();
		if (array.Length == 0)
		{
			return Array.Empty<TradeListing>();
		}
		string? queryId = GetString(executeDocument.RootElement, "id");
		queryId = string.IsNullOrWhiteSpace(queryId) ? context.QueryId : queryId;
		if (string.IsNullOrWhiteSpace(queryId))
		{
			throw new InvalidOperationException("GGG không trả về search id để tải chi tiết listing.");
		}
		List<TradeListing> listings = new List<TradeListing>();
		string[][] chunks = array.Chunk(10).ToArray();
		for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
		{
			string[] ids = chunks[chunkIndex];
			string path2 = BuildFetchPath(context, ids, queryId);
			using JsonDocument fetchDocument = JsonDocument.Parse(await GetWithRateLimitRetryAsync(path2, "tải chi tiết listing", cancellationToken));
			if (!fetchDocument.RootElement.TryGetProperty("result", out var value5) || value5.ValueKind != JsonValueKind.Array)
			{
				continue;
			}
			foreach (JsonElement item in value5.EnumerateArray())
			{
				listings.Add(ParseListing(item));
			}
			if (chunkIndex < chunks.Length - 1)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(300L), cancellationToken);
			}
		}
		return listings;
	}

	public async Task SendTravelAsync(string token, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException("Listing không có hideout_token nên không thể Travel.");
		}
		string content = JsonSerializer.Serialize(new { token });
		using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _httpClient.PostAsync("/api/trade/whisper", content2, cancellationToken);
		if (response.StatusCode == HttpStatusCode.Forbidden)
		{
			await response.Content.ReadAsStringAsync(cancellationToken);
			throw new TradeSessionException("GGG từ chối Travel (HTTP 403). POESESSID không được xác thực cho website Trade, token đã hết hạn, hoặc phiên cần thêm cookie trình duyệt. Đăng nhập lại pathofexile.com và tải lại listing trước khi thử lại.");
		}
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			await response.Content.ReadAsStringAsync(cancellationToken);
			throw new ListingUnavailableException("Item không còn tồn tại hoặc đã được người khác mua.");
		}
		await ReadOrThrowAsync(response, "gửi Travel", cancellationToken);
	}

	private static string BuildSavedSearchPath(TradeSearchContext context)
	{
		string value = (string.IsNullOrEmpty(context.RealmPrefix) ? string.Empty : ("/" + Uri.EscapeDataString(context.RealmPrefix)));
		return $"/api/{context.ApiPrefix}/search{value}/{Uri.EscapeDataString(context.League)}/{Uri.EscapeDataString(context.QueryId ?? throw new InvalidOperationException("Saved-search id is missing."))}";
	}

	private static bool LooksLikeEmbeddedQuery(string segment)
	{
		return segment.Length > EmbeddedQuerySegmentThreshold || segment.StartsWith("H4sI", StringComparison.Ordinal);
	}

	private static bool TryDecodeEmbeddedQuery(string segment, out string? queryJson, out string error)
	{
		queryJson = null;
		error = "Query Trade nhúng không hợp lệ.";
		if (segment.Any((char character) => !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_'))
		{
			error = "Query Trade nhúng phải là Base64URL hợp lệ.";
			return false;
		}
		int remainder = segment.Length % 4;
		if (remainder == 1 || (long)segment.Length * 3 / 4 > MaximumCompressedQueryBytes)
		{
			error = "Query Trade nhúng vượt giới hạn kích thước hoặc Base64URL không hợp lệ.";
			return false;
		}
		string base64 = segment.Replace('-', '+').Replace('_', '/');
		if (remainder != 0)
		{
			base64 = base64.PadRight(base64.Length + 4 - remainder, '=');
		}
		byte[] compressed;
		try
		{
			compressed = Convert.FromBase64String(base64);
		}
		catch (FormatException)
		{
			error = "Query Trade nhúng phải là Base64URL hợp lệ.";
			return false;
		}
		if (compressed.Length > MaximumCompressedQueryBytes)
		{
			error = "Query Trade nhúng vượt giới hạn kích thước.";
			return false;
		}
		if (compressed.Length < 18 || compressed[0] != 0x1f || compressed[1] != 0x8b || compressed[2] != 8)
		{
			error = "Query Trade nhúng không phải gzip hợp lệ.";
			return false;
		}
		try
		{
			using MemoryStream input = new MemoryStream(compressed);
			using GZipStream gzip = new GZipStream(input, CompressionMode.Decompress);
			using MemoryStream output = new MemoryStream();
			byte[] buffer = new byte[8192];
			int count;
			while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0)
			{
				if (output.Length + count > MaximumExpandedQueryBytes)
				{
					error = "Query Trade nhúng vượt giới hạn kích thước sau giải nén.";
					return false;
				}
				output.Write(buffer, 0, count);
			}
			byte[] payload = output.ToArray();
			uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length - 8, 4));
			uint expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length - 4, 4));
			if (expectedCrc != ComputeCrc32(payload) || expectedSize != unchecked((uint)payload.Length))
			{
				error = "Query Trade nhúng gzip bị cắt hoặc checksum không hợp lệ.";
				return false;
			}
			queryJson = StrictUtf8.GetString(payload);
			using JsonDocument document = JsonDocument.Parse(queryJson, new JsonDocumentOptions { MaxDepth = 64 });
			if (document.RootElement.ValueKind != JsonValueKind.Object || !QueryProperties.Any((string property) => document.RootElement.TryGetProperty(property, out _)))
			{
				queryJson = null;
				error = "Query Trade nhúng không có cấu trúc tìm kiếm hợp lệ.";
				return false;
			}
			return true;
		}
		catch (InvalidDataException)
		{
			queryJson = null;
			error = "Query Trade nhúng không phải gzip hợp lệ hoặc dữ liệu vượt giới hạn.";
			return false;
		}
		catch (DecoderFallbackException)
		{
			queryJson = null;
			error = "Query Trade nhúng không phải UTF-8 hợp lệ.";
			return false;
		}
		catch (JsonException)
		{
			queryJson = null;
			error = "Query Trade nhúng không phải JSON hợp lệ.";
			return false;
		}
	}

	private static uint ComputeCrc32(byte[] bytes)
	{
		uint crc = uint.MaxValue;
		foreach (byte value in bytes)
		{
			crc ^= value;
			for (int bit = 0; bit < 8; bit++)
			{
				crc = (((crc & 1) == 0) ? (crc >> 1) : ((crc >> 1) ^ 0xEDB88320u));
			}
		}
		return ~crc;
	}

	private static string BuildExecuteSearchPath(TradeSearchContext context)
	{
		string value = (string.IsNullOrEmpty(context.RealmPrefix) ? string.Empty : ("/" + Uri.EscapeDataString(context.RealmPrefix)));
		return $"/api/{context.ApiPrefix}/search{value}/{Uri.EscapeDataString(context.League)}";
	}

	private static string BuildFetchPath(TradeSearchContext context, IEnumerable<string> ids, string queryId)
	{
		string value = string.Join(",", ids.Select(Uri.EscapeDataString));
		string value2 = ((context.ApiPrefix == "trade2") ? "trade2" : "trade");
		return $"/api/{value2}/fetch/{value}?query={Uri.EscapeDataString(queryId)}";
	}

	private static TradeListing ParseListing(JsonElement result)
	{
		string? resultId = GetString(result, "id") ?? string.Empty;
		JsonElement value;
		JsonElement element = (result.TryGetProperty("listing", out value) ? value : default(JsonElement));
		JsonElement value2;
		JsonElement element2 = (result.TryGetProperty("item", out value2) ? value2 : default(JsonElement));
		JsonElement value3;
		JsonElement element3 = ((element.ValueKind == JsonValueKind.Object && element.TryGetProperty("account", out value3)) ? value3 : default(JsonElement));
		JsonElement value4;
		JsonElement element4 = ((element.ValueKind == JsonValueKind.Object && element.TryGetProperty("price", out value4)) ? value4 : default(JsonElement));
		return new TradeListing(resultId, FindHideoutToken(result), GetString(element3, "name"), GetString(element2, "name") ?? GetString(element2, "typeLine"), GetDecimal(element4, "amount"), GetString(element4, "currency"), GetString(element, "method"));
	}

	public static string? FindHideoutToken(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in element.EnumerateObject())
			{
				if (item.Name.Equals("hideout_token", StringComparison.OrdinalIgnoreCase) && item.Value.ValueKind == JsonValueKind.String)
				{
					return item.Value.GetString();
				}
			}
			foreach (JsonProperty item2 in element.EnumerateObject())
			{
				string text = FindHideoutToken(item2.Value);
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text;
				}
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item3 in element.EnumerateArray())
			{
				string text2 = FindHideoutToken(item3);
				if (!string.IsNullOrWhiteSpace(text2))
				{
					return text2;
				}
			}
		}
		return null;
	}

	private static string? GetString(JsonElement element, string propertyName)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static decimal? GetDecimal(JsonElement element, string propertyName)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
		{
			return null;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var value2))
		{
			return value2;
		}
		if (value.ValueKind != JsonValueKind.String || !decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value2))
		{
			return null;
		}
		return value2;
	}

	private static async Task<string> ReadOrThrowAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
	{
		string text = await response.Content.ReadAsStringAsync(cancellationToken);
		if (response.IsSuccessStatusCode)
		{
			return text;
		}
		TimeSpan? timeSpan = response.Headers.RetryAfter?.Delta;
		object obj;
		if (timeSpan.HasValue)
		{
			TimeSpan valueOrDefault = timeSpan.GetValueOrDefault();
			obj = $" Thử lại sau {Math.Ceiling(valueOrDefault.TotalSeconds)} giây.";
		}
		else
		{
			obj = string.Empty;
		}
		string value = (string)obj;
		string value2 = ((text.Length > 300) ? text.Substring(0, 300) : text);
		throw new HttpRequestException($"Không thể {operation}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.{value} {value2}");
	}

	private async Task<string> GetWithRateLimitRetryAsync(string path, string operation, CancellationToken cancellationToken)
	{
		for (int attempt = 1; attempt <= 4; attempt++)
		{
			using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
			if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == 4)
			{
				return await ReadOrThrowAsync(response, operation, cancellationToken);
			}
			TimeSpan? timeSpan = response.Headers.RetryAfter?.Delta;
			await Task.Delay((!timeSpan.HasValue) ? TimeSpan.FromSeconds(Math.Pow(2.0, attempt - 1)) : TimeSpan.FromSeconds(Math.Clamp(timeSpan.GetValueOrDefault().TotalSeconds, 1.0, 30.0)), cancellationToken);
		}
		throw new InvalidOperationException("Không thể " + operation + " sau nhiều lần thử.");
	}

	public void Dispose()
	{
		_httpClient.Dispose();
	}
}
