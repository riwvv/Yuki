namespace Yuki.Core.Wrappers.Contracts;

public interface ILlamaChatEngine : IDisposable {
    IAsyncEnumerable<string> RespondAsync(string userMessage, CancellationToken cancellationToken = default);
}
