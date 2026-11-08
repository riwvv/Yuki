using Yuki.Core.ResourceManagement.Contracts;
using Yuki.Core.ServantAgent.Contracts;
using Yuki.Core.STT.Contracts;
using Yuki.Core.TTS.Contracts;

namespace Yuki.Core.ResourceManagement.Services;

public class SafeToReloadGate(IVoiceInteractionService _voice, IServantAgentService _servant, ITextToSpeechPlayer _tts) : ISafeToReloadGate {
    public bool IsSafeToReload() => _voice.State == VoiceInteractionState.Idle && _servant.IsIdle && !_tts.IsSpeaking;
}
