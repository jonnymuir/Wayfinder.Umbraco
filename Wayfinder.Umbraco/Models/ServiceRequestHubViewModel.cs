using Umbraco.Cms.Core.Models.PublishedContent;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Models;

/// <summary>
/// View model for the <c>serviceRequestHub</c> document type's "My Service Requests" page —
/// every instance <see cref="Controllers.ServiceRequestHubController"/> found for the current caller, split
/// into still-in-progress and completed.
/// </summary>
public class ServiceRequestHubViewModel : PublishedContentWrapped
{
    /// <summary>Instances the caller can still resume — not yet in a terminal state.</summary>
    public IReadOnlyList<ServiceRequestViewModel> ActiveInstances { get; init; } = Array.Empty<ServiceRequestViewModel>();

    /// <summary>Instances that have already reached a terminal state.</summary>
    public IReadOnlyList<ServiceRequestViewModel> CompletedInstances { get; init; } = Array.Empty<ServiceRequestViewModel>();

    public ServiceRequestHubViewModel(IPublishedContent content, IPublishedValueFallback publishedValueFallback)
        : base(content, publishedValueFallback) { }
}

/// <summary>One row in the "My Service Requests" list — a summary plus the URL to resume it.</summary>
public class ServiceRequestViewModel
{
    public ServiceRequestSummary Summary { get; init; } = null!;
    public string ResumeUrl { get; init; } = "#";
}
