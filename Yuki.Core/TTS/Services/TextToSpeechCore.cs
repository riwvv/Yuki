using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.RegularExpressions;
using Yuki.Core.TTS.Contracts;
using Yuki.Core.TTS.Models;

namespace Yuki.Core.TTS.Services;

public class TextToSpeechCore : ITextToSpeechCore {
    public int SampleRate { get; set; }

    private readonly UnicodeProcessor _textProcessor;
    private readonly InferenceSession _dpOrt;
    private readonly InferenceSession _textEncOrt;
    private readonly InferenceSession _vectorEstOrt;
    private readonly InferenceSession _vocoderOrt;

    private readonly int _baseChunkSize;
    private readonly int _chunkCompressFactor;
    private readonly int _ldim;

    public TextToSpeechCore(Config cfgs, string onnxDir) {
        _textProcessor = new UnicodeProcessor(Path.Combine(onnxDir, "unicode_indexer.json"));
        SampleRate = cfgs.AE.SampleRate;
        _baseChunkSize = cfgs.AE.BaseChunkSize;
        _chunkCompressFactor = cfgs.TTL.ChunkCompressFactor;
        _ldim = cfgs.TTL.LatentDim;

        var opts = new SessionOptions();
        _dpOrt = new InferenceSession(Path.Combine(onnxDir, "duration_predictor.onnx"), opts);
        _textEncOrt = new InferenceSession(Path.Combine(onnxDir, "text_encoder.onnx"), opts);
        _vectorEstOrt = new InferenceSession(Path.Combine(onnxDir, "vector_estimator.onnx"), opts);
        _vocoderOrt = new InferenceSession(Path.Combine(onnxDir, "vocoder.onnx"), opts);
    }

    public (float[] wav, float[] duration) Call(string text, Style style, int totalStep = 6, float speed = 1.05f) {
        int maxLen = 300;
        var textList = ChunkText(text, maxLen);
        var wavCat = new List<float>();
        float durCat = 0.0f;

        foreach (var chunk in textList) {
            var (wav, duration) = Infer(chunk, style, totalStep, speed);
            if (wavCat.Count == 0) { wavCat.AddRange(wav); durCat = duration[0]; }
            else {
                wavCat.AddRange(new float[(int)(0.3f * SampleRate)]); // Пауза 0.3с
                wavCat.AddRange(wav);
                durCat += duration[0] + 0.3f;
            }
        }
        return (wavCat.ToArray(), new float[] { durCat });
    }

    private (float[] wav, float[] duration) Infer(string text, Style style, int totalStep, float speed) {
        var (textIds, textMask) = _textProcessor.Call(text);

        // ОПТИМИЗАЦИЯ: Использование Array.Copy вместо List.AddRange
        var textIdsTensor = new DenseTensor<long>(textIds[0], [1, textIds[0].Length]);
        var textMaskTensor = new DenseTensor<float>(textMask[0][0], [1, 1, textMask[0][0].Length]);
        var styleTtlTensor = new DenseTensor<float>(style.Ttl, style.TtlShape.Select(x => (int)x).ToArray());
        var styleDpTensor = new DenseTensor<float>(style.Dp, style.DpShape.Select(x => (int)x).ToArray());

        using var dpOutputs = _dpOrt.Run([
            NamedOnnxValue.CreateFromTensor("text_ids", textIdsTensor),
            NamedOnnxValue.CreateFromTensor("style_dp", styleDpTensor),
            NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor)
        ]);
        var durOnnx = dpOutputs.First(o => o.Name == "duration").AsTensor<float>().ToArray();
        for (int i = 0; i < durOnnx.Length; i++) durOnnx[i] /= speed;

        using var textEncOutputs = _textEncOrt.Run([
            NamedOnnxValue.CreateFromTensor("text_ids", textIdsTensor),
            NamedOnnxValue.CreateFromTensor("style_ttl", styleTtlTensor),
            NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor)
        ]);
        var textEmbTensor = textEncOutputs.First(o => o.Name == "text_emb").AsTensor<float>();

        var (xt, latentMask) = SampleNoisyLatent(durOnnx);
        var latentShape = new[] { 1, xt.Length, xt[0].Length };
        var latentMaskShape = new[] { 1, 1, latentMask.Length };

        var totalStepArray = new[] { (float)totalStep };

        for (int step = 0; step < totalStep; step++) {
            var currentStepArray = new[] { (float)step };

            using var vectorEstOutputs = _vectorEstOrt.Run([
                NamedOnnxValue.CreateFromTensor("noisy_latent", FlatArrayToTensor(xt, latentShape)),
                NamedOnnxValue.CreateFromTensor("text_emb", textEmbTensor),
                NamedOnnxValue.CreateFromTensor("style_ttl", styleTtlTensor),
                NamedOnnxValue.CreateFromTensor("text_mask", textMaskTensor),
                NamedOnnxValue.CreateFromTensor("latent_mask", new DenseTensor<float>(latentMask, latentMaskShape)),
                NamedOnnxValue.CreateFromTensor("total_step", new DenseTensor<float>(totalStepArray, [1])),
                NamedOnnxValue.CreateFromTensor("current_step", new DenseTensor<float>(currentStepArray, [1]))
            ]);

            var denoisedLatent = vectorEstOutputs.First(o => o.Name == "denoised_latent").AsTensor<float>();

            int idx = 0;
            for (int d = 0; d < xt.Length; d++)
                for (int t = 0; t < xt[d].Length; t++)
                    xt[d][t] = denoisedLatent.GetValue(idx++);
        }

        using var vocoderOutputs = _vocoderOrt.Run([
            NamedOnnxValue.CreateFromTensor("latent", FlatArrayToTensor(xt, latentShape))
        ]);
        var wavTensor = vocoderOutputs.First(o => o.Name == "wav_tts").AsTensor<float>();

        return (wavTensor.ToArray(), durOnnx);
    }

    private (float[][] noisyLatent, float[] latentMask) SampleNoisyLatent(float[] duration) {
        float wavLenMax = duration.Max() * SampleRate;
        int chunkSize = _baseChunkSize * _chunkCompressFactor;
        int latentLen = (int)((wavLenMax + chunkSize - 1) / chunkSize);
        int latentDim = _ldim * _chunkCompressFactor;

        var noisyLatent = new float[latentDim][];
        for (int d = 0; d < latentDim; d++) {
            noisyLatent[d] = new float[latentLen];
            for (int t = 0; t < latentLen; t++) {
                // ОПТИМИЗАЦИЯ: Используем Random.Shared
                double u1 = 1.0 - Random.Shared.NextDouble();
                double u2 = 1.0 - Random.Shared.NextDouble();
                noisyLatent[d][t] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
            }
        }

        long wavLength = (long)(duration[0] * SampleRate);
        int latentLength = (int)((wavLength + chunkSize - 1) / chunkSize);
        var latentMask = new float[latentLen];
        for (int t = 0; t < latentLen; t++) latentMask[t] = t < latentLength ? 1.0f : 0.0f;

        for (int d = 0; d < latentDim; d++)
            for (int t = 0; t < latentLen; t++)
                noisyLatent[d][t] *= latentMask[t];

        return (noisyLatent, latentMask);
    }

    private static DenseTensor<float> FlatArrayToTensor(float[][] array, int[] dims) {
        var flat = new float[dims[1] * dims[2]];
        int offset = 0;
        foreach (var row in array) { Array.Copy(row, 0, flat, offset, row.Length); offset += row.Length; }
        return new DenseTensor<float>(flat, dims);
    }

    private static List<string> ChunkText(string text, int maxLen) {
        var chunks = new List<string>();
        var sentences = Regex.Split(text.Trim(), @"(?<=[.!?])\s+");
        string current = "";

        foreach (var s in sentences) {
            if (current.Length + s.Length + 1 <= maxLen) current += (current.Length > 0 ? " " : "") + s;
            else { if (current.Length > 0) chunks.Add(current.Trim()); current = s; }
        }
        if (current.Length > 0) chunks.Add(current.Trim());
        return chunks.Count > 0 ? chunks : [text.Trim()];
    }

    public void Dispose() {
        _dpOrt?.Dispose();
        _textEncOrt?.Dispose();
        _vectorEstOrt?.Dispose();
        _vocoderOrt?.Dispose();
    }
}
