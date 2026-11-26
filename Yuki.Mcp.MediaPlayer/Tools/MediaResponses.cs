using System.Text.Json;

namespace Yuki.Mcp.MediaPlayer.Tools;

/// <summary>
/// Единый формат ответов: только done/error, без DONE/ERROR/WARNING.
/// </summary>
internal static class MediaResponses {
    internal static string Ok(string message, object? data = null)
        => JsonSerializer.Serialize(new { status = "done", message, data });

    internal static string Error(string cause, string description)
        => JsonSerializer.Serialize(new { status = "error", cause, description });
}
