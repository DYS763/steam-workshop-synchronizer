using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using SteamWorkshopSynchronizer.Core.Operations;

namespace SteamWorkshopSynchronizer.Infrastructure.Steam;

public interface ISteamWorkshopOperator
{
    Task<WorkshopOperationResult> SubmitAsync(WorkshopOperationRequest request, CancellationToken cancellationToken = default);
}

public sealed class SteamProtocolOperator : ISteamWorkshopOperator
{
    public Task<WorkshopOperationResult> SubmitAsync(WorkshopOperationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.PublishedFileId) || request.PublishedFileId.Any(character => !char.IsDigit(character)))
        {
            return Task.FromResult(new WorkshopOperationResult(request, WorkshopOperationResultState.Failed, null, "Workshop ID 无效"));
        }

        var verb = request.Action == WorkshopOperationAction.Subscribe ? "subscribe" : "unsubscribe";
        var uri = $"steam://{verb}/?id={request.PublishedFileId}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
            return Task.FromResult(new WorkshopOperationResult(request, WorkshopOperationResultState.Submitted, uri, null));
        }
        catch (Win32Exception exception)
        {
            return Task.FromResult(new WorkshopOperationResult(request, WorkshopOperationResultState.Failed, uri, $"Steam 客户端协议启动失败：{exception.Message}"));
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(new WorkshopOperationResult(request, WorkshopOperationResultState.Failed, uri, $"Steam 客户端协议不可用：{exception.Message}"));
        }
    }
}
