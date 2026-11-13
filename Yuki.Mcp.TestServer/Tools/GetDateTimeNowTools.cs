using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Yuki.Mcp.TestServer.Tools;

internal class GetDateTimeNowTools {
    [McpServerTool]
    [Description("Get string with current date and time")]
    public string GetDateTime() => DateTime.Now.ToString("G");
}
