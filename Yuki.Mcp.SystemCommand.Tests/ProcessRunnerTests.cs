using System.Diagnostics;
using System.Globalization;
using System.Text;
using Yuki.Mcp.SystemCommand.Tools;

namespace Yuki.Mcp.SystemCommand.Tests;

public class ProcessRunnerTests {
    [Fact]
    public void CreateStartInfo_UsesArgumentListAndNeverFlattensIt() {
        var spec = new ProcessSpec(
            "ping.exe",
            new[] { "-n", "1", "127.0.0.1" },
            TimeSpan.FromSeconds(1),
            "ping -n 1 127.0.0.1");

        var startInfo = ProcessRunner.CreateStartInfo(spec);

        Assert.Equal("ping.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.Equal(new[] { "-n", "1", "127.0.0.1" }, startInfo.ArgumentList);
    }

    [Fact]
    public void CreateStartInfo_UsesOemConsoleEncodingForOutputAndErrors() {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var expected = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var spec = new ProcessSpec("ipconfig.exe", Array.Empty<string>(), TimeSpan.FromSeconds(1), "ipconfig");

        var startInfo = ProcessRunner.CreateStartInfo(spec);

        Assert.NotNull(startInfo.StandardOutputEncoding);
        Assert.NotNull(startInfo.StandardErrorEncoding);
        Assert.Equal(expected.WebName, startInfo.StandardOutputEncoding!.WebName);
        Assert.Equal(expected.WebName, startInfo.StandardErrorEncoding!.WebName);
    }
}
