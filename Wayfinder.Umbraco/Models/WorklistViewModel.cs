using Wayfinder.Models.ServiceDesign;
using Wayfinder.Umbraco.Controllers;

namespace Wayfinder.Umbraco.Models;

/// <summary>
/// What the <c>wayfinderServiceRequestWorklist</c> block's list shows, for
/// <c>~/Views/Partials/_WayfinderWorklist.cshtml</c>. The package's own copy renders the GOV.UK worklist
/// (<see cref="DefaultBody"/>); a host replaces that partial with its own to show the queue its way,
/// building its links and pickup forms with <see cref="ItemUrl"/> and <see cref="PickupUrl"/>.
/// </summary>
/// <param name="PageUrl">The worklist page's own path.</param>
/// <param name="Envelope">The page of work items the caller's access profile can see.</param>
/// <param name="Rows">Each of those items with its request's field values, for a host showing them as columns.</param>
/// <param name="AntiforgeryToken">For a pickup form's <c>__RequestVerificationToken</c>.</param>
/// <param name="Notice">What to tell the caller about the item the page was asked for, if it is no longer theirs to see.</param>
/// <param name="DefaultBody">The GOV.UK worklist markup the package's own partial renders.</param>
public sealed record WorklistViewModel(
    string PageUrl,
    QueueWorkListEnvelope Envelope,
    IReadOnlyList<WorklistRow> Rows,
    string AntiforgeryToken,
    WorklistNotice Notice,
    string DefaultBody)
{
    /// <summary>Opens <paramref name="item"/> on this page, where the block renders its stage.</summary>
    public string ItemUrl(QueueWorkItem item) =>
        $"{PageUrl}?instanceId={Uri.EscapeDataString(item.InstanceId)}&blueprintKey={Uri.EscapeDataString(item.BlueprintKey)}";

    /// <summary>Where a pickup form posts (fields: <c>cursorId</c>, <c>returnTo</c>, the antiforgery token).</summary>
    public static string PickupUrl(QueueWorkItem item) =>
        $"{WayfinderWorklistSurfaceController.ItemUrlPrefix}/{Uri.EscapeDataString(item.BlueprintKey)}/{Uri.EscapeDataString(item.InstanceId)}/pickup";
}

/// <summary>Why the item a worklist page was asked for isn't shown.</summary>
public enum WorklistNotice
{
    None,

    /// <summary>The caller's own move on it was just saved and took it on to someone else: done.</summary>
    Done,

    /// <summary>It isn't theirs to see any more: someone else picked it up, or it was finished elsewhere.</summary>
    NoLongerInQueue,
}

/// <param name="Item">The work item.</param>
/// <param name="FieldValues">Its request's field values (empty if the request has gone).</param>
public sealed record WorklistRow(QueueWorkItem Item, IReadOnlyDictionary<string, object?> FieldValues);

/// <summary>
/// One work item open on the worklist page, for <c>~/Views/Partials/_WayfinderWorklistItem.cshtml</c>:
/// the way back to the list and the item's stage.
/// </summary>
public sealed record WorklistItemViewModel(string PageUrl, ServiceRequestPageViewModel Stage);
