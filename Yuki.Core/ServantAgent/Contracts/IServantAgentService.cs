namespace Yuki.Core.ServantAgent.Contracts;

public interface IServantAgentService : IDisposable {
    bool IsIdle { get; }
    event Action<string>? TaskCompleted;
    Task ExecuteTaskAsync(string taskDescription, CancellationToken ct =  default);
    Task<bool> TryAcquireReloadLockAsync(TimeSpan timeout, CancellationToken ct = default);
    void ReleaseReloadLock();
}
