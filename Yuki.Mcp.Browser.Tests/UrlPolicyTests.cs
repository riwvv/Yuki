using Yuki.Mcp.Browser.Tools;

namespace Yuki.Mcp.Browser.Tests;

public class UrlPolicyTests {
    [Theory]
    [InlineData("google.com")]
    [InlineData("https://www.youtube.com/watch?v=abc123")]
    public void TrustedHosts_OpenWithoutConfirmation(string url) {
        var ok = UrlPolicy.TryValidate(url, false, out var target, out var cause, out var error);

        Assert.True(ok);
        Assert.Equal(string.Empty, cause);
        Assert.Null(error);
        Assert.StartsWith("https://", target.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(url.Contains("youtube") ? "www.youtube.com" : "google.com", target.Host);
    }

    [Fact]
    public void UnknownHost_RequiresConfirmation() {
        var denied = UrlPolicy.TryValidate("https://example.com/page", false, out _, out var cause, out var error);

        Assert.False(denied);
        Assert.Equal("confirmation_required", cause);
        Assert.False(string.IsNullOrWhiteSpace(error));

        var allowed = UrlPolicy.TryValidate("https://example.com/page", true, out var target, out _, out _);

        Assert.True(allowed);
        Assert.Equal("example.com", target.Host);
    }

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:someone@example.com")]
    public void NonHttpSchemes_AreAlwaysBlocked(string url) {
        var ok = UrlPolicy.TryValidate(url, true, out _, out var cause, out var error);

        Assert.False(ok);
        Assert.Equal("not_allowed", cause);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("https://localhost:8080/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("https://intranet.local/")]
    [InlineData("http://router/")]
    [InlineData("http://[::1]/")]
    public void LocalAndInternalHosts_AreBlockedEvenWithConfirmation(string url) {
        var ok = UrlPolicy.TryValidate(url, true, out _, out var cause, out var error);

        Assert.False(ok);
        Assert.Equal("not_allowed", cause);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void UserInfo_IsBlocked() {
        var ok = UrlPolicy.TryValidate("https://user:pass@google.com/", true, out _, out var cause, out _);

        Assert.False(ok);
        Assert.Equal("not_allowed", cause);
    }

    [Fact]
    public void SensitiveQuery_RequiresConfirmation() {
        var denied = UrlPolicy.TryValidate("https://google.com/?token=SUPERSECRET", false, out _, out var cause, out _);

        Assert.False(denied);
        Assert.Equal("confirmation_required", cause);

        Assert.True(UrlPolicy.TryValidate("https://google.com/?token=SUPERSECRET", true, out _, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]
    [InlineData("https://google.com\ncalc")]
    public void InvalidUrls_AreRejected(string url) {
        var ok = UrlPolicy.TryValidate(url, true, out _, out var cause, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.NotEqual("not_allowed", cause);
    }

    [Fact]
    public void TooLongUrl_IsRejected() {
        var ok = UrlPolicy.TryValidate(new string('a', 3000), true, out _, out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
