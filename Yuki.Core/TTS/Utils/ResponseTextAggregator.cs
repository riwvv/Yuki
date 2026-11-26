using System.Text;

namespace Yuki.Core.TTS.Utils;

/// <summary>
/// Собирает поток токенов ответа в одну строку перед озвучкой.
/// TTS сам разбивает текст на предложения и расставляет паузы, поэтому
/// синтезировать нужно весь ответ целиком, а не по токенам.
/// </summary>
public static class ResponseTextAggregator {
    public static async Task<string> AggregateAsync(IAsyncEnumerable<string> tokens, CancellationToken cancellationToken = default) {
        var text = new StringBuilder();
        await foreach (var token in tokens.WithCancellation(cancellationToken))
            text.Append(token);

        // Токены часто приходят с пробелами на стыке — сжимаем их, иначе
        // в озвучке появляются лишние паузы между словами.
        return string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
