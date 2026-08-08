using Yuki.Core.ResourceManagement.Contracts;
using Yuki.Core.STT.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class SafeToReloadGate(IVoiceInteractionService _voice) : ISafeToReloadGate {
    public bool IsSafeToReload() => _voice.State == VoiceInteractionState.Idle && IsServantIdle() && IsTtsIdle();
    private bool IsServantIdle() => true;
    private bool IsTtsIdle() => true;
}
