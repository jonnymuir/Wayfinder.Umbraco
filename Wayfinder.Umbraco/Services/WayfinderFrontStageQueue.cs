namespace Wayfinder.Umbraco.Services;

/// <summary>
/// The single queue a Wayfinder.Umbraco-hosted service blueprint runs on today — declared once
/// here and threaded through the backoffice editor host (as the only entry in
/// <c>availableQueues</c>, which is what naturally locks the editor's queue-picker to
/// single-queue authoring) and <see cref="SingleQueueStructuralValidator"/>.
/// </summary>
/// <remarks>
/// Not a permanent product ceiling — Wayfinder.Umbraco's rendering pipeline currently only
/// serves one actor's perspective (a visitor-facing page), so only one queue's worth of content
/// can ever be rendered through it. Multi-queue/back-stage authoring (a reviewer/admin
/// perspective) is future Wayfinder work, not something this constant or the validator that
/// reads it needs to anticipate.
/// </remarks>
public static class WayfinderFrontStageQueue
{
    public const string Key = "front-stage";
    public const string DisplayName = "Visitor touchpoints";
}
