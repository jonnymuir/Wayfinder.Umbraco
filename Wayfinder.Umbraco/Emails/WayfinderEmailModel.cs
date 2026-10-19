using Wayfinder.Engine.Models;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Emails;

/// <summary>
/// What an email template renders from. A conversation is shown as it was when the move happened, so
/// a message the person sends after it, before the email goes, is never quoted as though it came first.
/// </summary>
public sealed class WayfinderEmailModel(
    ServiceRequest request,
    string action,
    IReadOnlyDictionary<string, int> conversationLengths,
    string publicBaseUrl,
    string? privateLinkUrl,
    TimeSpan privateLinkLifetime)
{
    public ServiceRequest Request { get; } = request;

    /// <summary>The move that sent this email.</summary>
    public string Action { get; } = action;

    /// <summary>The site's public address, for links back to it.</summary>
    public string PublicBaseUrl { get; } = publicBaseUrl.TrimEnd('/');

    /// <summary>A private link opening the request, when the email asks for one.</summary>
    public string? PrivateLinkUrl { get; } = privateLinkUrl;

    /// <summary>How long <see cref="PrivateLinkUrl"/> works.</summary>
    public TimeSpan PrivateLinkLifetime { get; } = privateLinkLifetime;

    /// <summary>A field's value as text, or null.</summary>
    public string? Field(string fieldKey) => Request.FieldValues.GetValueOrDefault(fieldKey)?.ToString();

    /// <summary>A conversation field's messages, oldest first, up to the move that sent this email.</summary>
    public IReadOnlyList<ConversationEntry> Conversation(string fieldKey)
    {
        var thread = ConversationEntry.ReadThread(Request.FieldValues.GetValueOrDefault(fieldKey));
        var length = Math.Min(conversationLengths.GetValueOrDefault(fieldKey), thread.Count);
        return thread.Take(length).ToList();
    }

    /// <summary>A conversation field's latest message at the move that sent this email, or null.</summary>
    public ConversationEntry? LatestMessage(string fieldKey) => Conversation(fieldKey) is [.., var last] ? last : null;
}
