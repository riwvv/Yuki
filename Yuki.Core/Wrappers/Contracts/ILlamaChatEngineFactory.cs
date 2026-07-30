using Yuki.Core.Wrappers.Models;

namespace Yuki.Core.Wrappers.Contracts;

public interface ILlamaChatEngineFactory {
    ILlamaChatEngine Create(LlamaChatEngineOptions options);
}
