namespace Yuki.Core.Configurations;

public class ResourceManagerSettings {
    public int ReservedVramMb { get; set; } = 1024;
    public int ConfirmationThreshold { get; set; } = 3;
    public int PollIntervalSeconds { get; set; } = 30;
    public int MinLayerDeltaForReload { get; set; } = 5;
}
