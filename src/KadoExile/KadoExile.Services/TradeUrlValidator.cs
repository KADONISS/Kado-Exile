using System;

namespace KadoExile.Services;

public static class TradeUrlValidator
{
	public const int MaximumLength = 16_384;

	private static readonly string[] AllowedHosts = new string[4] { "pathofexile.com", "www.pathofexile.com", "pathofexile2.com", "www.pathofexile2.com" };

	public static bool TryValidate(string? value, out Uri? uri, out string error)
	{
		uri = null;
		error = string.Empty;
		if ((value?.Length ?? 0) > MaximumLength)
		{
			error = $"Link Trade vượt quá giới hạn {MaximumLength} ký tự.";
			return false;
		}
		string normalized = Normalize(value);
		if (normalized.Length > MaximumLength)
		{
			error = $"Link Trade vượt quá giới hạn {MaximumLength} ký tự.";
			return false;
		}
		if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri result))
		{
			error = "Link Trade không hợp lệ.";
			return false;
		}
		if (result.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains<string>(result.Host, StringComparer.OrdinalIgnoreCase))
		{
			error = "Chỉ chấp nhận link HTTPS của Path of Exile.";
			return false;
		}
		if (!result.AbsolutePath.Contains("/trade", StringComparison.OrdinalIgnoreCase))
		{
			error = "Link phải trỏ tới trang Trade.";
			return false;
		}
		uri = result;
		return true;
	}

	public static string Normalize(string? value)
	{
		string text = (value ?? string.Empty).Replace("\u200b", string.Empty, StringComparison.Ordinal).Replace("\ufeff", string.Empty, StringComparison.Ordinal).Trim()
			.Trim(new char[4] { '"', '\'', '<', '>' });
		if (text.StartsWith("www.pathofexile.com/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("pathofexile.com/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("www.pathofexile2.com/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("pathofexile2.com/", StringComparison.OrdinalIgnoreCase))
		{
			text = "https://" + text;
		}
		return text;
	}
}
