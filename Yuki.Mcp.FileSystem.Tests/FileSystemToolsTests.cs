using System.Text.Json;
using Yuki.Mcp.FileSystem.Resources;
using Yuki.Mcp.FileSystem.Tools;

namespace Yuki.Mcp.FileSystem.Tests;

public class FileSystemToolsTests {
    private readonly FileSystemTools _tools = new(new FileSystemResources());

    [Theory]
    [InlineData("")]
    [InlineData("../desktop")]
    [InlineData("documents;calc")]
    public void GetInfoFolder_RejectsInvalidFolder(string folderName) {
        var json = _tools.GetInfoFolder(folderName);

        AssertError(json, "invalid_folder");
    }

    [Fact]
    public void OpenFileInFolder_RejectsTraversalInFileName() {
        var json = _tools.OpenFileInFolder("desktop", "../secret.txt");

        AssertError(json, "invalid_file_name");
    }

    [Fact]
    public void OpenFileInFolder_RejectsUnknownFileType() {
        var json = _tools.OpenFileInFolder("desktop", "report", "exe");

        AssertError(json, "unsupported_file_type");
    }

    [Fact]
    public void CreateNewFile_RejectsExecutableType() {
        var json = _tools.CreateNewFileInSpecialFolder("desktop", "virus", "exe");

        AssertError(json, "unsupported_file_type");
    }

    [Fact]
    public void CreateNewFile_RejectsTraversalInName() {
        var json = _tools.CreateNewFileInSpecialFolder("desktop", "../virus.txt");

        AssertError(json, "invalid_file_name");
    }

    [Fact]
    public void MoveFile_RejectsUnsafeDestinationFolder() {
        var json = _tools.MoveFileFromFolderToFolder("desktop", "../documents", "report.txt");

        AssertError(json, "invalid_folder");
    }

    [Fact]
    public void RenameFile_RejectsTraversalInNewName() {
        var json = _tools.RenameExistsFile("desktop", "report.txt", "../evil.txt");

        AssertError(json, "invalid_file_name");
    }

    [Fact]
    public void ReadFileContent_RejectsNonTextExtension() {
        var json = _tools.ReadFileContent("desktop", "evil.exe");

        AssertError(json, "read_error");

        using var document = JsonDocument.Parse(json);
        var description = document.RootElement.GetProperty("description").GetString();
        Assert.Contains("текстов", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationErrors_UseUnifiedFormat() {
        var responses = new[] {
            _tools.GetInfoFolder("../desktop"),
            _tools.OpenFileInFolder("desktop", "../secret.txt"),
            _tools.CreateNewFileInSpecialFolder("desktop", "virus", "exe"),
            _tools.MoveFileFromFolderToFolder("desktop", "../documents", "report.txt"),
            _tools.ReadFileContent("desktop", "evil.exe")
        };

        foreach (var json in responses) {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal("error", root.GetProperty("status").GetString());
            Assert.False(root.TryGetProperty("message", out _));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("cause").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
            Assert.DoesNotContain("DONE", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WARNING", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SuccessResponse_UsesLowerCaseDoneAndData() {
        var json = _tools.GetInfoFolder("desktop");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var status = root.GetProperty("status").GetString();

        if (status == "error")
            return; // папка недоступна в этом окружении

        Assert.Equal("done", status);
        Assert.True(root.TryGetProperty("message", out _));
        Assert.True(root.TryGetProperty("data", out var data));
        Assert.True(data.TryGetProperty("files", out _));
        Assert.True(data.TryGetProperty("directories", out _));
    }

    private static void AssertError(string json, string expectedCause) {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("error", root.GetProperty("status").GetString());
        Assert.Equal(expectedCause, root.GetProperty("cause").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
    }
}
