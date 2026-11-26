using Yuki.Mcp.FileSystem.Tools;

namespace Yuki.Mcp.FileSystem.Tests;

public class SafePathsTests {
    [Theory]
    [InlineData("downloads")]
    [InlineData("documents")]
    [InlineData("pictures")]
    [InlineData("videos")]
    [InlineData("music")]
    [InlineData("desktop")]
    [InlineData("Desktop")]
    public void IsAllowedFolder_AcceptsOnlyWhitelist(string folder) {
        Assert.True(SafePaths.IsAllowedFolder(folder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../desktop")]
    [InlineData("desktop/..")]
    [InlineData("documents;calc")]
    [InlineData("C:\\Windows")]
    public void IsAllowedFolder_RejectsUnsafeValues(string folder) {
        Assert.False(SafePaths.IsAllowedFolder(folder));
    }

    [Theory]
    [InlineData("report.txt")]
    [InlineData("my report.final.docx")]
    public void TryGetFileName_AcceptsSimpleNames(string name) {
        var ok = SafePaths.TryGetFileName(name, out var validated, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(name, validated);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("sub/file.txt")]
    [InlineData("sub\\file.txt")]
    [InlineData("C:\\secret.txt")]
    public void TryGetFileName_RejectsTraversalAndSeparators(string name) {
        var ok = SafePaths.TryGetFileName(name, out var validated, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, validated);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void IsUnder_RejectsSiblingDirectoryWithCommonPrefix() {
        var temp = Path.GetTempPath();
        var basePath = Path.Combine(temp, "yuki-safe-base");
        var evilSibling = Path.Combine(temp, "yuki-safe-base-evil", "secret.txt");

        Assert.False(SafePaths.IsUnder(basePath, evilSibling));
        Assert.True(SafePaths.IsUnder(basePath, Path.Combine(basePath, "report.txt")));
        Assert.False(SafePaths.IsUnder(basePath, Path.Combine(basePath, "..", "secret.txt")));
    }

    [Fact]
    public void TryResolveFileInBase_RejectsTraversal() {
        var basePath = Path.Combine(Path.GetTempPath(), "yuki-safe-base");

        var ok = SafePaths.TryResolveFileInBase(basePath, "../secret.txt", out var fullPath, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, fullPath);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryGetExtension_UsesOnlyWhitelistedTypes() {
        Assert.True(SafePaths.TryGetExtension(null, out var defaultExtension, out _));
        Assert.Equal(".txt", defaultExtension);

        Assert.True(SafePaths.TryGetExtension("текстовик", out var text, out _));
        Assert.Equal(".txt", text);

        Assert.True(SafePaths.TryGetExtension("видео", out var video, out _));
        Assert.Equal(".mp4", video);
    }

    [Theory]
    [InlineData("exe")]
    [InlineData(".exe")]
    [InlineData("cmd")]
    [InlineData(".bat")]
    [InlineData("lnk")]
    [InlineData("scr")]
    public void TryGetExtension_RejectsExecutableTypes(string fileType) {
        var ok = SafePaths.TryGetExtension(fileType, out var extension, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, extension);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryGetExtensions_NeverReturnsExecutableExtensions() {
        Assert.True(SafePaths.TryGetExtensions(null, out var all, out _));
        Assert.DoesNotContain(".exe", all, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".cmd", all, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".bat", all, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".lnk", all, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(".scr", all, StringComparer.OrdinalIgnoreCase);

        Assert.True(SafePaths.TryGetExtensions("фото", out var images, out _));
        Assert.Equal(new[] { ".png", ".jpg", ".jpeg" }, images);
    }

    [Fact]
    public void IsReadableExtension_AllowsOnlyTextFormats() {
        Assert.True(SafePaths.IsReadableExtension(".txt"));
        Assert.True(SafePaths.IsReadableExtension(".csv"));
        Assert.False(SafePaths.IsReadableExtension(".exe"));
        Assert.False(SafePaths.IsReadableExtension(".png"));
        Assert.False(SafePaths.IsReadableExtension(".docx"));
    }

    [Fact]
    public void TryFindFile_IgnoresExecutables() {
        var directory = Path.Combine(Path.GetTempPath(), "yuki-fs-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try {
            File.WriteAllText(Path.Combine(directory, "report.txt"), "ok");
            File.WriteAllText(Path.Combine(directory, "report.exe"), "MZ");

            var ok = SafePaths.TryFindFile(directory, "report", null, out var found, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(".txt", Path.GetExtension(found), ignoreCase: true);
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}
