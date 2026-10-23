namespace SteamWorkshopSynchronizer.Core.Operations;

public enum WorkshopOperationAction
{
    Subscribe,
    Unsubscribe
}

public sealed record WorkshopOperationRequest(
    int AppId,
    string PublishedFileId,
    WorkshopOperationAction Action,
    string? Title,
    string? WorkshopUrl);

public enum WorkshopOperationResultState
{
    Submitted,
    Failed,
    Skipped,
    Stopped
}

public sealed record WorkshopOperationResult(
    WorkshopOperationRequest Request,
    WorkshopOperationResultState State,
    string? ProtocolUri,
    string? ErrorMessage);

public sealed record WorkshopOperationProgress(
    int Completed,
    int Total,
    WorkshopOperationResult Result);
