using Yuki.Core.Wrappers.Contracts;
using Yuki.Core.Wrappers.Models;

namespace Yuki.Core.Wrappers.Services;

public class LlamaChatEngineFactory : ILlamaChatEngineFactory {
    public ILlamaChatEngine Create(LlamaChatEngineOptions options) => new LlamaChatEngine(options);
}
