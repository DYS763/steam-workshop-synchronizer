using SteamWorkshopSynchronizer.Core.Operations;
using SteamWorkshopSynchronizer.Infrastructure.Steam;

namespace SteamWorkshopSynchronizer.Application.Operations;

public sealed class WorkshopOperationQueue
{
    private readonly ISteamWorkshopOperator steamOperator;
    private readonly object stateLock = new();
    private TaskCompletionSource<bool> resumeSignal = CompletedSignal();
    private bool stopped;
    private bool paused;

    public WorkshopOperationQueue(ISteamWorkshopOperator steamOperator)
    {
        this.steamOperator = steamOperator ?? throw new ArgumentNullException(nameof(steamOperator));
    }

    public bool IsPaused
    {
        get
        {
            lock (stateLock)
            {
                return paused;
            }
        }
    }

    public bool IsStopped
    {
        get
        {
            lock (stateLock)
            {
                return stopped;
            }
        }
    }

    public void Pause()
    {
        lock (stateLock)
        {
            if (!stopped)
            {
                paused = true;
                resumeSignal = NewSignal();
            }
        }
    }

    public void Resume()
    {
        lock (stateLock)
        {
            paused = false;
            resumeSignal.TrySetResult(true);
        }
    }

    public void Stop()
    {
        lock (stateLock)
        {
            stopped = true;
            paused = false;
            resumeSignal.TrySetResult(true);
        }
    }

    public async Task<IReadOnlyList<WorkshopOperationResult>> RunAsync(
        IReadOnlyList<WorkshopOperationRequest> requests,
        IProgress<WorkshopOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        lock (stateLock)
        {
            stopped = false;
            paused = false;
            resumeSignal = CompletedSignal();
        }

        var results = new List<WorkshopOperationResult>(requests.Count);
        for (var index = 0; index < requests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            if (IsStopped)
            {
                var stoppedResult = new WorkshopOperationResult(requests[index], WorkshopOperationResultState.Stopped, null, "队列已停止");
                results.Add(stoppedResult);
                progress?.Report(new(index + 1, requests.Count, stoppedResult));
                continue;
            }

            var result = await steamOperator.SubmitAsync(requests[index], cancellationToken).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(new(index + 1, requests.Count, result));
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task signal;
        lock (stateLock)
        {
            signal = resumeSignal.Task;
        }

        return signal.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<bool> CompletedSignal()
    {
        var signal = NewSignal();
        signal.TrySetResult(true);
        return signal;
    }
}
