namespace Yuki.Core.TTS.Models;

public class Style(float[] ttl, long[] ttlShape, float[] dp, long[] dpShape) {
    public float[] Ttl { get; set; } = ttl;
    public long[] TtlShape { get; set; } = ttlShape;
    public float[] Dp { get; set; } = dp;
    public long[] DpShape { get; set; } = dpShape;
}
