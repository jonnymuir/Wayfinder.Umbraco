using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// What a failed stage submission leaves for the page it redirects to (the PRG pattern): the problems to
/// show and what the person typed, so nothing they wrote is lost (WCAG 3.3.1). It belongs to one request,
/// and a render of any other request ignores it, so it can never turn up on a different form, even one
/// the same browser opens next. It rides in TempData, and the next stage render takes it.
/// </summary>
public sealed record StageFlash(
    string InstanceId,
    IReadOnlyList<ServiceRequestProblem> Problems,
    IReadOnlyDictionary<string, string> FormValues)
{
    private const string TempDataKey = "Wayfinder.StageFlash";
    private const string MovedOnKey = "Wayfinder.MovedOn";

    /// <summary>Records, for the next page, that the actor's move on this request was saved and their part is done.</summary>
    public static void KeepMovedOn(ITempDataDictionary tempData, string instanceId) => tempData[MovedOnKey] = instanceId;

    /// <summary>Whether the actor's last move on <paramref name="instanceId"/> moved it on; taken once.</summary>
    public static bool TakeMovedOn(ITempDataDictionary tempData, string instanceId)
    {
        // ToString, not "as string": TempData hands back a value that looks like a GUID as a Guid.
        var movedOn = tempData[MovedOnKey]?.ToString();
        tempData.Remove(MovedOnKey);
        return !string.IsNullOrEmpty(instanceId) && string.Equals(movedOn, instanceId, StringComparison.Ordinal);
    }

    /// <summary>The problems and values for <paramref name="instanceId"/>, or null if this flash is another request's.</summary>
    public (IReadOnlyList<ServiceRequestProblem> Problems, IReadOnlyDictionary<string, string> FormValues)? For(string instanceId) =>
        !string.IsNullOrEmpty(InstanceId) && string.Equals(InstanceId, instanceId, StringComparison.Ordinal)
            ? (Problems, FormValues)
            : null;

    public void Keep(ITempDataDictionary tempData) => tempData[TempDataKey] = JsonSerializer.Serialize(this);

    /// <summary>Takes the flash out of TempData, so it is shown once; null when there is none or it can't be read.</summary>
    public static StageFlash? Take(ITempDataDictionary tempData)
    {
        if (tempData[TempDataKey] is not string json)
        {
            return null;
        }

        tempData.Remove(TempDataKey);
        try
        {
            return JsonSerializer.Deserialize<StageFlash>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
