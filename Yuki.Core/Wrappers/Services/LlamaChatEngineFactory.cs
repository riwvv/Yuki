using Microsoft.Extensions.Options;
using Yuki.Core.Configurations;
using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;

namespace Yuki.Core.Wrappers.Services;

public class LlamaChatEngineFactory(IOptions<LlamaChatEngineSettings> settings) : ILlamaChatEngineFactory {
    public ILlamaChatEngine Create(LlamaChatEngineOptions options) => new LlamaChatEngine(options, settings.Value);
}
