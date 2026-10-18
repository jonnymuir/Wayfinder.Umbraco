using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// A check every stage form submission must pass before anything about the request changes: bot
/// protection (a CAPTCHA, a honeypot), rate limiting, or any other rule of the host's. Register one or
/// more in DI. Wayfinder.Umbraco adds each guard's <see cref="FormFields"/> to every stage form it
/// renders, and runs every guard's <see cref="CheckAsync"/> on every stage submission, after the
/// submission's nonce has been verified and before any field, file or engine work. With none
/// registered there is no extra check.
/// </summary>
public interface IStageSubmissionGuard
{
    /// <summary>
    /// Markup added inside every stage form (a hidden field for a token, say), or <see langword="null"/>
    /// for none. It is rendered exactly as given, so escape anything built from data.
    /// </summary>
    IHtmlContent? FormFields(StageSubmissionGuardContext context);

    /// <summary>
    /// <see langword="null"/> to let the submission through, or the message to show the person. A
    /// rejected submission changes nothing and returns them to the stage with that message.
    /// </summary>
    Task<string?> CheckAsync(StageSubmissionGuardContext context, IFormCollection form);
}

/// <summary>The stage form a guard is rendering into, or checking a submission of.</summary>
/// <param name="HttpContext">The current request.</param>
/// <param name="BlueprintKey">The blueprint the stage belongs to.</param>
/// <param name="InstanceId">The request the stage belongs to.</param>
public sealed record StageSubmissionGuardContext(HttpContext HttpContext, string BlueprintKey, string InstanceId);
