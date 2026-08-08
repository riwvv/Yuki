using LLama;
using LLama.Abstractions;
using LLama.Common;

namespace Yuki.Core.Wrappers.Services;

public class ModelChatTemplateTransform(LLamaWeights weights) : IHistoryTransform {
    public string HistoryToText(ChatHistory history) {
        var template = new LLamaTemplate(weights) { AddAssistant = true };
        foreach (var message in history.Messages)
            template.Add(ToTemplateRole(message.AuthorRole), message.Content);

        var bytes = template.Apply();
        return LLamaTemplate.Encoding.GetString(bytes);
    }

    public ChatHistory TextToHistory(AuthorRole role, string text) {
        var history = new ChatHistory();
        history.AddMessage(role, text);
        return history;
    }

    public IHistoryTransform Clone() => new ModelChatTemplateTransform(weights);

    private static string ToTemplateRole(AuthorRole role) => role switch {
        AuthorRole.System => "system",
        AuthorRole.User => "user",
        AuthorRole.Assistant => "assistant",
        _ => "user"
    };
}
