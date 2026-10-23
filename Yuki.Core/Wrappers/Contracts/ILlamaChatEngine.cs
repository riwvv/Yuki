using LLama.Sampling;

namespace Yuki.Core.Wrappers.Contracts;

public interface ILlamaChatEngine : IDisposable {
    /// <summary>
    /// Отправляет сообщение пользователя в LLM и возвращает асинхронный поток сгенерированных токенов.
    /// </summary>
    /// <param name="userMessage">Текст запроса от пользователя.</param>
    /// <param name="grammar">
    /// Опциональная GBNF-грамматика для жесткого ограничения формата вывода. 
    /// Если передана, модель будет генерировать текст строго в соответствии с этими правилами (например, для JSON).
    /// Если null — генерация происходит без ограничений (свободный текст).
    /// </param>
    /// <param name="cancellationToken">Токен для безопасной отмены генерации (например, при выходе из приложения или прерывании пользователем).</param>
    /// <returns>Асинхронный поток токенов, по мере их генерации моделью.</returns>
    IAsyncEnumerable<string> RespondAsync(string userMessage, Grammar? grammar = null, CancellationToken cancellationToken = default);
}
