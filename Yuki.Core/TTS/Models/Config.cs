namespace Yuki.Core.TTS.Models;

public class Config {
    public AEConfig AE { get; set; } = null!;
    public TTLConfig TTL { get; set; } = null!;

    public class AEConfig {
        public int SampleRate { get; set; }
        public int BaseChunkSize { get; set; }
    }

    public class TTLConfig {
        public int ChunkCompressFactor { get; set; }
        public int LatentDim { get; set; }
    }
}
