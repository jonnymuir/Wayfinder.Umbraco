using Microsoft.Extensions.Logging;

namespace Wayfinder.Umbraco.Services;

public partial class ServiceRequestStageService
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Stage advance: missing nonce, possible form tampering")]
    private static partial void MissingNonce(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stage advance: nonce expired, invalid, or bound to a different instance/user; redirecting to GET")]
    private static partial void InvalidNonce(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception rendering stage {BlueprintKey}/{InstanceId}")]
    private static partial void StageRenderFailed(ILogger logger, Exception exception, string blueprintKey, string? instanceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stage advance: rejected by a submission guard")]
    private static partial void RejectedByGuard(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stage advance: missing InstanceId or Action")]
    private static partial void MissingInstanceOrAction(ILogger logger);
}
