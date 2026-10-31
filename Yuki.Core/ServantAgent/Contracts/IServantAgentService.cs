namespace Yuki.Core.ServantAgent.Contracts;

public interface IServantAgentService : IDisposable {
    Task ExecuteTaskAsync(string taskDescription, CancellationToken ct =  default);
    event Action<string>? TaskCompleted;
}
