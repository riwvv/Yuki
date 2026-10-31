using LLama.Abstractions;
using LLama.Common;
using System.Text;

namespace Yuki.Core.Wrappers.Utils;

public class QwenChatMLTransform : IHistoryTransform {
    public string HistoryToText(ChatHistory history) {
        var sb = new StringBuilder();
        foreach (var msg in history.Messages) {
            // msg.AuthorRole — это LLama.Common.AuthorRole (enum), а не string
            var role = msg.AuthorRole switch {
                AuthorRole.System => "system",
                AuthorRole.User => "user",
                AuthorRole.Assistant => "assistant",
                // AuthorRole.Tool   => "tool", // если в вашей версии есть — раскомментируйте
                _ => "user"   // Tool, Unknown и всё остальное → user
            };

            sb.Append("<|im_start|>").Append(role).Append('\n');
            sb.Append(msg.Content);
            sb.Append("<|im_end|>\n");
        }

        // Затравка для генерации — модель должна начать ответ assistant
        sb.Append("<|im_start|>assistant\n");
        return sb.ToString();
    }

    // 🔑 Правильная сигнатура: два параметра (role + text)
    public ChatHistory TextToHistory(AuthorRole role, string text) {
        return new ChatHistory([new ChatHistory.Message(role, text)]);
    }

    public IHistoryTransform Clone() => new QwenChatMLTransform();
}