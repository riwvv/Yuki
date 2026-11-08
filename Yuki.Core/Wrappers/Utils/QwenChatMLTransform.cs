using LLama.Abstractions;
using LLama.Common;
using System.Text;

namespace Yuki.Core.Wrappers.Utils;

public class QwenChatMLTransform : IHistoryTransform {
    public string HistoryToText(ChatHistory history) {
        var sb = new StringBuilder();
        foreach (var msg in history.Messages) {
            var role = msg.AuthorRole switch {
                AuthorRole.System => "system",
                AuthorRole.User => "user",
                AuthorRole.Assistant => "assistant",
                _ => "user"
            };

            sb.Append("<|im_start|>").Append(role).Append('\n');
            sb.Append(msg.Content);
            sb.Append("<|im_end|>\n");
        }

        sb.Append("<|im_start|>assistant\n");
        return sb.ToString();
    }

    public ChatHistory TextToHistory(AuthorRole role, string text) {
        return new ChatHistory([new ChatHistory.Message(role, text)]);
    }

    public IHistoryTransform Clone() => new QwenChatMLTransform();
}