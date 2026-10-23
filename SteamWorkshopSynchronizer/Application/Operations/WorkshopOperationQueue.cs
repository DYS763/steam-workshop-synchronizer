using SteamWorkshopSynchronizer.Core.Operations;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;
using SteamWorkshopSynchronizer.Infrastructure.Steam;

namespace SteamWorkshopSynchronizer.Application.Operations;

public sealed class WorkshopOperationQueue
{
    private readonly ISteamWorkshopOperator steamOperator;
    private readonly AppLogger? logger;
    private readonly object stateLock = new();
    private TaskCompletionSource<bool> resumeSignal = CompletedSignal();
    private bool stopped;
    private bool paused;

    public WorkshopOperationQueue(ISteamWorkshopOperator steamOperator, AppLogger? logger = null)
    {
        this.steamOperator = steamOperator ?? throw new ArgumentNullException(nameof(steamOperator));
        this.logger = logger;
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
                logger?.Info("队列", "操作队列已暂停");
            }
        }
    }

    public void Resume()
    {
        lock (stateLock)
        {
            paused = false;
            resumeSignal.TrySetResult(true);
            logger?.Info("队列", "操作队列已继续");
        }
    }

    public void Stop()
    {
        lock (stateLock)
        {
            stopped = true;
            paused = false;
            resumeSignal.TrySetResult(true);
            logger?.Warning("队列", "操作队列已停止，后续项目将标记为停止");
        }
    }

    public async Task<IReadOnlyList<WorkshopOperationResult>> RunAsync(
        IReadOnlyList<WorkshopOperationRequest> requests,
        IProgress<WorkshopOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        logger?.Info("队列", $"开始执行操作队列，共 {requests.Count} 项");
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
                logger?.Info("队列", $"第 {index + 1}/{requests.Count} 项已停止：{requests[index].PublishedFileId}");
                progress?.Report(new(index + 1, requests.Count, stoppedResult));
                continue;
            }

            var result = await steamOperator.SubmitAsync(requests[index], cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (result.State == WorkshopOperationResultState.Failed)
            {
                logger?.Error("队列", $"第 {index + 1}/{requests.Count} 项失败：{requests[index].PublishedFileId}，{result.ErrorMessage}");
            }
            else
            {
                logger?.Info("队列", $"第 {index + 1}/{requests.Count} 项结果：{result.State}，{requests[index].PublishedFileId}");
            }
            progress?.Report(new(index + 1, requests.Count, result));
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        logger?.Info("队列", $"操作队列完成，提交 {results.Count(result => result.State == WorkshopOperationResultState.Submitted)} 项，失败 {results.Count(result => result.State == WorkshopOperationResultState.Failed)} 项");
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
