using System.Text.Json;
using Yuki.Mcp.Browser.Tools;

namespace Yuki.Mcp.Browser.Tests;

public class BrowserToolsTests {
    [Fact]
    public void EmptyUrl_ReturnsError() {
        var json = BrowserTools.OpenUrl("");

        AssertError(json, "empty_url");
    }

    [Fact]
    public void FileScheme_ReturnsNotAllowed() {
        var json = BrowserTools.OpenUrl("file:///C:/Windows/win.ini");

        AssertError(json, "not_allowed");
    }

    [Fact]
    public void LocalAddress_IsBlockedEvenWithConfirmation() {
        var json = BrowserTools.OpenUrl("http://127.0.0.1/admin", confirm: true);

        AssertError(json, "not_allowed");
    }

    [Fact]
    public void UnknownSite_RequiresConfirmation() {
        var json = BrowserTools.OpenUrl("https://example.com/page");

        AssertError(json, "confirmation_required");
    }

    [Fact]
    public void Error_DoesNotLeakSensitiveQuery() {
        var json = BrowserTools.OpenUrl("https://google.com/?token=SUPERSECRET", confirm: false);

        AssertError(json, "confirmation_required");
        Assert.DoesNotContain("SUPERSECRET", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_UseUnifiedFormatWithoutFillUrl() {
        var responses = new[] {
            BrowserTools.OpenUrl(""),
            BrowserTools.OpenUrl("file:///C:/Windows/win.ini"),
            BrowserTools.OpenUrl("https://example.com/page")
        };

        foreach (var json in responses) {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal("error", root.GetProperty("status").GetString());
            Assert.False(root.TryGetProperty("fillUrl", out _));
            Assert.False(root.TryGetProperty("message", out _));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("cause").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
        }
    }

    private static void AssertError(string json, string expectedCause) {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("error", root.GetProperty("status").GetString());
        Assert.Equal(expectedCause, root.GetProperty("cause").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
    }
}
