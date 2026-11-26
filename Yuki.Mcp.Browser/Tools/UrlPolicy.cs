using System.Net;
using System.Net.Sockets;

namespace Yuki.Mcp.Browser.Tools;

internal sealed record UrlTarget(string Url, string Host);

/// <summary>
/// Политика адресов. Локальные, внутренние и не-HTTP адреса запрещены
/// всегда; неизвестные сайты требуют явного подтверждения.
/// </summary>
internal static class UrlPolicy {
    internal static readonly string[] TrustedHosts = [
        "google.com", "youtube.com", "yandex.ru", "github.com", "stackoverflow.com",
        "wikipedia.org", "microsoft.com", "learn.microsoft.com", "habr.com", "vk.com"
    ];

    private const int MaxLength = 2048;

    private static readonly string[] SensitiveQueryKeys = [
        "token", "access_token", "password", "passwd", "secret", "apikey", "api_key", "auth", "key"
    ];

    internal static bool TryValidate(
        string? input,
        bool confirm,
        out UrlTarget target,
        out string cause,
        out string? error) {
        target = new UrlTarget(string.Empty, string.Empty);
        cause = "invalid_url";

        if (string.IsNullOrWhiteSpace(input)) {
            cause = "empty_url";
            error = "URL не может быть пустым";
            return false;
        }

        var value = input.Trim();
        if (value.Length > MaxLength || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))) {
            error = "Некорректный URL: слишком длинный адрес или запрещённые символы";
            return false;
        }

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0) {
            var scheme = value[..colon];
            if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                !scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) {
                cause = "not_allowed";
                error = "Разрешены только адреса http:// и https://";
                return false;
            }
        }
        else {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host)) {
            error = "URL имеет неверный формат";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)) {
            cause = "not_allowed";
            error = "Адрес с логином и паролем запрещён";
            return false;
        }

        var host = uri.Host.Trim('.').ToLowerInvariant();
        if (IsBlockedHost(host)) {
            cause = "not_allowed";
            error = "Локальные, внутренние и не-DNS адреса открывать запрещено";
            return false;
        }

        if (!confirm && (!IsTrustedHost(host) || HasSensitiveQuery(uri))) {
            cause = "confirmation_required";
            error = $"Для открытия сайта {host} требуется confirm = true";
            return false;
        }

        target = new UrlTarget(uri.AbsoluteUri, host);
        cause = string.Empty;
        error = null;
        return true;
    }

    private static bool IsBlockedHost(string host) {
        if (host is "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal) ||
            host.EndsWith(".local", StringComparison.Ordinal) ||
            host.EndsWith(".internal", StringComparison.Ordinal) ||
            host.EndsWith(".home.arpa", StringComparison.Ordinal))
            return true;

        if (IPAddress.TryParse(host, out var address))
            return IsPrivateOrLocalAddress(address);

        // Односегментные имена (intranet, router, mail) не являются обычными сайтами.
        return !host.Contains('.') || Uri.CheckHostName(host) != UriHostNameType.Dns;
    }

    private static bool IsPrivateOrLocalAddress(IPAddress address) {
        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork) {
            return bytes[0] == 0 ||
                   bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
        }

        return IPAddress.IsLoopback(address) ||
               address.IsIPv6LinkLocal ||
               address.IsIPv6SiteLocal ||
               (bytes.Length > 0 && bytes[0] >= 0xFC && bytes[0] <= 0xFD);
    }

    private static bool IsTrustedHost(string host)
        => TrustedHosts.Any(trusted =>
            host.Equals(trusted, StringComparison.Ordinal) ||
            host.EndsWith("." + trusted, StringComparison.Ordinal));

    private static bool HasSensitiveQuery(Uri uri) {
        if (string.IsNullOrWhiteSpace(uri.Query))
            return false;

        return uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0].ToLowerInvariant())
            .Any(key => SensitiveQueryKeys.Contains(key));
    }
}
