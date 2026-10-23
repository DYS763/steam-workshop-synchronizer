using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using SteamWorkshopSynchronizer.Core.Operations;
using SteamWorkshopSynchronizer.Infrastructure.Diagnostics;

namespace SteamWorkshopSynchronizer.Infrastructure.Steam;

public interface ISteamWorkshopOperator
{
    Task<WorkshopOperationResult> SubmitAsync(WorkshopOperationRequest request, CancellationToken cancellationToken = default);
}

public sealed class SteamProtocolOperator : ISteamWorkshopOperator
{
    private readonly AppLogger? logger;

    public SteamProtocolOperator(AppLogger? logger = null)
    {
        this.logger = logger;
    }

    public Task<WorkshopOperationResult> SubmitAsync(WorkshopOperationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.PublishedFileId) || request.PublishedFileId.Any(character => !char.IsDigit(character)))
        {
            logger?.Warning("Steam", $"拒绝无效 Workshop ID：{request.PublishedFileId}");
            return Task.FromResult(new WorkshopOperationResult(request, WorkshopOperationResultState.Failed, null, "Workshop ID 无效"));
        }

        var verb = request.Action == WorkshopOperationAction.Subscribe ? "subscribe" : "unsubscribe";
        var uri = $"steam://{verb}/?id={request.PublishedFileId}";
        logger?.Info("Steam", $"提交协议请求：{verb}，Workshop ID {request.PublishedFileId}");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
            logger?.Info("Steam", $"协议请求已交给系统：{uri}");
            return Task.FromResult(new WorkshopOperationResult(request, WorkshopOperationResultState.Submitted, uri, null));
        }
        catch (Win32Exception exception)
        {
            logger?.Error("Steam", "Steam 客户端协议启动失败", exception);
            return Task.FromResult(new WorkshopOperationResult(request, WorkshopOperationResultState.Failed, uri, $"Steam 客户端协议启动失败：{exception.Message}"));
        }
        catch (InvalidOperationException exception)
        {
            logger?.Error("Steam", "Steam 客户端协议不可用", exception);
            return Task.FromResult(new WorkshopOperationResult(request, WorkshopOperationResultState.Failed, uri, $"Steam 客户端协议不可用：{exception.Message}"));
        }
    }
}
