using System.Text.Json;
using Yuki.Mcp.SystemCommand.Tools;

namespace Yuki.Mcp.SystemCommand.Tests;

public class SystemCommandToolsTests {
    [Fact]
    public async Task PingHost_RejectsShellMetacharacters() {
        var json = await SystemCommandTools.PingHost("8.8.8.8 & calc", 3);

        AssertError(json, "not_allowed");
    }

    [Fact]
    public async Task Shutdown_RequiresExplicitConfirmation() {
        var json = await SystemCommandTools.Shutdown(confirm: false);

        AssertError(json, "confirmation_required");
    }

    [Fact]
    public async Task Restart_RequiresExplicitConfirmation() {
        var json = await SystemCommandTools.Restart(confirm: false);

        AssertError(json, "confirmation_required");
    }

    [Fact]
    public async Task ClearRecycleBin_RequiresExplicitConfirmation() {
        var json = await SystemCommandTools.ClearRecycleBin(confirm: false);

        AssertError(json, "confirmation_required");
    }

    [Fact]
    public void GetProcessList_RejectsUnsafeName() {
        var json = SystemCommandTools.GetProcessList("a'b");

        AssertError(json, "not_allowed");
    }

    private static void AssertError(string json, string expectedCause) {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("error", root.GetProperty("status").GetString());
        Assert.Equal(expectedCause, root.GetProperty("cause").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
    }
}
