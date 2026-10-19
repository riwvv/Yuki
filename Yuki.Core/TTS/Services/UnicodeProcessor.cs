using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yuki.Core.TTS.Contracts;

namespace Yuki.Core.TTS.Services;

internal class UnicodeProcessor : IUnicodeProcessor {
    private readonly Dictionary<int, long> _indexer;

    public UnicodeProcessor(string unicodeIndexerPath) {
        var json = File.ReadAllText(unicodeIndexerPath);
        var indexerArray = JsonSerializer.Deserialize<long[]>(json) ?? throw new Exception("Failed to load indexer");
        _indexer = [];
        for (int i = 0; i < indexerArray.Length; i++) _indexer[i] = indexerArray[i];
    }

    private static string PreprocessText(string text) {
        text = text.Normalize(NormalizationForm.FormKD);

        // Упрощенная очистка от эмодзи и спецсимволов
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++) {
            int codePoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                ? char.ConvertToUtf32(text[i++], text[i])
                : text[i];

            bool isEmoji = (codePoint >= 0x1F600 && codePoint <= 0x1F64F) || (codePoint >= 0x1F300 && codePoint <= 0x1F5FF) ||
                           (codePoint >= 0x1F680 && codePoint <= 0x1F6FF) || (codePoint >= 0x2600 && codePoint <= 0x26FF) ||
                           (codePoint >= 0x2700 && codePoint <= 0x27BF);

            if (!isEmoji) sb.Append(codePoint > 0xFFFF ? char.ConvertFromUtf32(codePoint) : ((char)codePoint).ToString());
        }
        text = sb.ToString();

        text = text.Replace("–", "-").Replace("—", "-").Replace("_", " ")
                   .Replace("\u201C", "\"").Replace("\u201D", "\"").Replace("\u2018", "'").Replace("\u2019", "'")
                   .Replace("[", " ").Replace("]", " ").Replace("|", " ").Replace("/", " ").Replace("#", " ");

        text = Regex.Replace(text, @"[♥☆♡©\\]", "");
        text = text.Replace("@", " at ");

        text = Regex.Replace(text, @"\s+", " ").Trim();

        if (!Regex.IsMatch(text, @"[.!?;:,'\u0022\u201C\u201D\u2018\u2019)\]}…]$")) text += ".";

        return $"<ru>{text}</ru>";
    }

    public (long[][] textIds, float[][][] textMask) Call(string text) {
        var processedText = PreprocessText(text);
        var textIds = new long[1][];
        textIds[0] = new long[processedText.Length];

        for (int j = 0; j < processedText.Length; j++) {
            if (_indexer.TryGetValue(processedText[j], out long val)) textIds[0][j] = val;
        }

        var mask = new float[1][][];
        mask[0] = new float[1][];
        mask[0][0] = [.. Enumerable.Repeat(1.0f, processedText.Length)];

        return (textIds, mask);
    }
}
