using Yuki.Mcp.SystemCommand.Tools;

namespace Yuki.Mcp.SystemCommand.Tests;

public class WhitelistedCommandsTests {
    [Fact]
    public void Ping_AcceptsDnsName() {
        var ok = WhitelistedCommands.TryPing("google.com", 3, out var spec, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(spec);
        Assert.Equal("ping.exe", spec.FileName);
        Assert.Equal(new[] { "-n", "3", "google.com" }, spec.Arguments);
        Assert.Equal("ping -n 3 google.com", spec.Display);
        Assert.True(spec.Timeout > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Ping_AcceptsIpAddresses(string host) {
        var ok = WhitelistedCommands.TryPing(host, 1, out var spec, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new[] { "-n", "1", host }, spec!.Arguments);
    }

    [Theory]
    [InlineData("8.8.8.8 & calc")]
    [InlineData("8.8.8.8; calc")]
    [InlineData("8.8.8.8|calc")]
    [InlineData("-n 1 127.0.0.1")]
    [InlineData("google.com\ncalc")]
    [InlineData("$(calc)")]
    public void Ping_RejectsShellMetacharacters(string host) {
        var ok = WhitelistedCommands.TryPing(host, 3, out var spec, out var error);

        Assert.False(ok);
        Assert.Null(spec);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ping_RejectsEmptyHost(string? host) {
        var ok = WhitelistedCommands.TryPing(host, 3, out var spec, out var error);

        Assert.False(ok);
        Assert.Null(spec);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Ping_RejectsCountOutOfRange(int count) {
        var ok = WhitelistedCommands.TryPing("127.0.0.1", count, out var spec, out var error);

        Assert.False(ok);
        Assert.Null(spec);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Ping_AcceptsMaximumAllowedCount() {
        var ok = WhitelistedCommands.TryPing("127.0.0.1", 10, out var spec, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new[] { "-n", "10", "127.0.0.1" }, spec!.Arguments);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("my_proc-1.2")]
    [InlineData("Yuki.Mcp.SystemCommand")]
    public void ProcessName_AcceptsSafeNames(string name) {
        var ok = WhitelistedCommands.TryValidateProcessName(name, out var validated, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(name, validated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ProcessName_TreatsEmptyAsNoFilter(string? name) {
        var ok = WhitelistedCommands.TryValidateProcessName(name, out var validated, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(validated);
    }

    [Theory]
    [InlineData("chrome; calc")]
    [InlineData("a'b")]
    [InlineData("a\"b")]
    [InlineData("../evil")]
    [InlineData("a b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ProcessName_RejectsUnsafeNames(string name) {
        var ok = WhitelistedCommands.TryValidateProcessName(name, out var validated, out var error);

        Assert.False(ok);
        Assert.Null(validated);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void IpConfig_UsesOnlyAllowedFlags() {
        Assert.Empty(WhitelistedCommands.IpConfig(false).Arguments);
        Assert.Equal(new[] { "/all" }, WhitelistedCommands.IpConfig(true).Arguments);
        Assert.Equal("ipconfig.exe", WhitelistedCommands.IpConfig(false).FileName);
    }

    [Fact]
    public void TaskList_HasNoArguments() {
        var spec = WhitelistedCommands.TaskList();

        Assert.Equal("tasklist.exe", spec.FileName);
        Assert.Empty(spec.Arguments);
    }

    [Fact]
    public void FixedCommands_NeverUseShell() {
        var specs = new[] {
            WhitelistedCommands.IpConfig(false),
            WhitelistedCommands.IpConfig(true),
            WhitelistedCommands.TaskList(),
            WhitelistedCommands.LockScreen(),
            WhitelistedCommands.OpenTaskManager(),
            WhitelistedCommands.Shutdown(false),
            WhitelistedCommands.Shutdown(true),
            WhitelistedCommands.CancelShutdown()
        };

        Assert.All(specs, spec => {
            Assert.NotEqual("cmd.exe", spec.FileName);
            Assert.NotEqual("powershell.exe", spec.FileName);
            Assert.True(spec.Timeout > TimeSpan.Zero);
        });
    }

    [Fact]
    public void PowerShellCommands_UseOnlyFixedScripts() {
        var clear = WhitelistedCommands.ClearRecycleBin();
        var minimize = WhitelistedCommands.MinimizeAllWindows();

        Assert.Equal("powershell.exe", clear.FileName);
        Assert.Equal(
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Clear-RecycleBin -Force -ErrorAction SilentlyContinue" },
            clear.Arguments);

        Assert.Equal("powershell.exe", minimize.FileName);
        Assert.Equal(
            new[] { "-NoProfile", "-NonInteractive", "-Command", "(New-Object -ComObject Shell.Application).MinimizeAll()" },
            minimize.Arguments);
    }

    [Fact]
    public void Shutdown_UsesFixedArguments() {
        Assert.Equal(new[] { "/s", "/t", "15" }, WhitelistedCommands.Shutdown(false).Arguments);
        Assert.Equal(new[] { "/r", "/t", "15" }, WhitelistedCommands.Shutdown(true).Arguments);
        Assert.Equal(new[] { "/a" }, WhitelistedCommands.CancelShutdown().Arguments);
    }

    [Fact]
    public void LockScreen_UsesFixedRundll32Argument() {
        var spec = WhitelistedCommands.LockScreen();

        Assert.Equal("rundll32.exe", spec.FileName);
        Assert.Equal(new[] { "user32.dll,LockWorkStation" }, spec.Arguments);
    }
}
