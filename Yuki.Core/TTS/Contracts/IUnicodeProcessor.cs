namespace Yuki.Core.TTS.Contracts; 

internal interface IUnicodeProcessor {
    (long[][] textIds, float[][][] textMask) Call(string text);
}
