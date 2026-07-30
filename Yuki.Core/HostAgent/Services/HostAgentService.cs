using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.HostAgent.Contracts;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;
using Yuki.Core.Wrappers.Utils;

namespace Yuki.Core.HostAgent.Services;

public class HostAgentService : IHostAgentService, IDisposable {
    private readonly ILlamaChatEngine _engine;
    public HostAgentService(ILlamaChatEngineFactory factory, IOptions<HostSettings> settings) {
        _engine = factory.Create(new LlamaChatEngineOptions {
            ModelPath = settings.Value.ModelPath,
            SystemPrompt = Utils.ReadSystemPromptFromFile("HostAgentPrompt.txt")
        });
    }

    public IAsyncEnumerable<string> RespondAsync(string userMessage, CancellationToken cancellationToken = default) => _engine.RespondAsync(userMessage, cancellationToken);

    public void Dispose() => _engine.Dispose();
}
