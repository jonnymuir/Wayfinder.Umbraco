namespace Wayfinder.Umbraco.Emails;

/// <summary>
/// One email to send, as queued. It carries ids and counts only, never a name, address or message, so a
/// durable queue (a host's Hangfire, say) holds nothing personal.
/// </summary>
/// <param name="EmailName">The <see cref="WayfinderEmail.Name"/>.</param>
/// <param name="InstanceId">The request.</param>
/// <param name="Action">The move that sent it.</param>
/// <param name="ConversationLengths">Each conversation field's length at the move, to show it as it was then.</param>
public sealed record WayfinderEmailJob(string EmailName, string InstanceId, string Action, IReadOnlyDictionary<string, int> ConversationLengths);
