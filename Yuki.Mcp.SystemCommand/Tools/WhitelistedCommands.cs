using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;

namespace Yuki.Mcp.SystemCommand.Tools;

/// <summary>
/// Полностью фиксированная спецификация процесса. Ни одно поле не строится
/// из произвольной строки, пришедшей от модели.
/// </summary>
internal sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    string Display);

/// <summary>
/// Whitelist операций, которые разрешено выполнять от лица Юки.
/// Если операции нет здесь — её не существует для MCP-клиента.
/// </summary>
internal static class WhitelistedCommands {
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private static readonly Regex DnsNameRegex = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProcessNameRegex = new(
        @"^[A-Za-z0-9_.-]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ProcessSpec IpConfig(bool all) => new(
        "ipconfig.exe",
        all ? new[] { "/all" } : Array.Empty<string>(),
        DefaultTimeout,
        all ? "ipconfig /all" : "ipconfig");

    public static ProcessSpec TaskList() => new(
        "tasklist.exe",
        Array.Empty<string>(),
        DefaultTimeout,
        "tasklist");

    public static bool TryPing(
        string? host,
        int count,
        [NotNullWhen(true)] out ProcessSpec? spec,
        [NotNullWhen(false)] out string? error) {
        if (!TryValidateHost(host, out var validatedHost, out error)) {
            spec = null;
            return false;
        }

        if (count < 1 || count > 10) {
            spec = null;
            error = "Количество пакетов ping должно быть от 1 до 10";
            return false;
        }

        spec = new ProcessSpec(
            "ping.exe",
            new[] { "-n", count.ToString(), validatedHost },
            TimeSpan.FromSeconds(20),
            $"ping -n {count} {validatedHost}");
        error = null;
        return true;
    }

    public static bool TryValidateProcessName(
        string? name,
        out string? validated,
        [NotNullWhen(false)] out string? error) {
        if (string.IsNullOrWhiteSpace(name)) {
            validated = null;
            error = null;
            return true;
        }

        var trimmed = name.Trim();
        if (!ProcessNameRegex.IsMatch(trimmed)) {
            validated = null;
            error = "Имя процесса может содержать только латинские буквы, цифры, точку, дефис и подчёркивание (до 64 символов)";
            return false;
        }

        validated = trimmed;
        error = null;
        return true;
    }

    public static ProcessSpec ClearRecycleBin() => PowerShell(
        "Clear-RecycleBin -Force -ErrorAction SilentlyContinue",
        "Clear-RecycleBin -Force");

    public static ProcessSpec MinimizeAllWindows() => PowerShell(
        "(New-Object -ComObject Shell.Application).MinimizeAll()",
        "MinimizeAll()");

    public static ProcessSpec LockScreen() => new(
        "rundll32.exe",
        new[] { "user32.dll,LockWorkStation" },
        TimeSpan.FromSeconds(10),
        "rundll32 user32.dll,LockWorkStation");

    public static ProcessSpec OpenTaskManager() => new(
        "taskmgr.exe",
        Array.Empty<string>(),
        TimeSpan.FromSeconds(10),
        "taskmgr.exe");

    public static ProcessSpec Shutdown(bool restart) => new(
        "shutdown.exe",
        restart ? new[] { "/r", "/t", "15" } : new[] { "/s", "/t", "15" },
        TimeSpan.FromSeconds(10),
        restart ? "shutdown /r /t 15" : "shutdown /s /t 15");

    public static ProcessSpec CancelShutdown() => new(
        "shutdown.exe",
        new[] { "/a" },
        TimeSpan.FromSeconds(10),
        "shutdown /a");

    private static ProcessSpec PowerShell(string script, string display) => new(
        "powershell.exe",
        new[] { "-NoProfile", "-NonInteractive", "-Command", script },
        DefaultTimeout,
        display);

    private static bool TryValidateHost(
        string? host,
        [NotNullWhen(true)] out string? validated,
        [NotNullWhen(false)] out string? error) {
        if (string.IsNullOrWhiteSpace(host)) {
            validated = null;
            error = "Укажите хост для ping (например, google.com или 8.8.8.8)";
            return false;
        }

        var trimmed = host.Trim();
        if (trimmed.Length > 253 || trimmed.StartsWith('-') || trimmed.Any(char.IsWhiteSpace)) {
            validated = null;
            error = "Некорректный хост: запрещены пробелы, длина более 253 символов и ведущий '-'";
            return false;
        }

        if (IPAddress.TryParse(trimmed, out _)) {
            validated = trimmed;
            error = null;
            return true;
        }

        if (Uri.CheckHostName(trimmed) != UriHostNameType.Dns || !DnsNameRegex.IsMatch(trimmed)) {
            validated = null;
            error = "Некорректный хост: разрешены только DNS-имена и IP-адреса";
            return false;
        }

        validated = trimmed;
        error = null;
        return true;
    }
}
