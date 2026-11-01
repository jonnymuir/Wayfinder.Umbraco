using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wayfinder.Umbraco.Services;

namespace Wayfinder.Umbraco.Recaptcha;

/// <summary>
/// Google reCAPTCHA v3 on every stage form. It fails closed: no token, no secret key, a low score, the
/// wrong action, or Google unreachable all turn the submission away with a message, never let it through.
/// </summary>
public sealed partial class RecaptchaSubmissionGuard(
    IOptions<WayfinderRecaptchaOptions> options,
    IHttpClientFactory httpClientFactory,
    IFileVersionProvider fileVersions,
    ILogger<RecaptchaSubmissionGuard> logger) : IStageSubmissionGuard
{
    public const string TokenField = "WayfinderRecaptchaToken";
    public const string ScriptPath = "/App_Plugins/Wayfinder/js/wayfinder-recaptcha.js";
    private const string Rejection = "We couldn't confirm you're not a robot. Please try again.";

    public IHtmlContent? FormFields(StageSubmissionGuardContext context)
    {
        var settings = options.Value;
        if (settings.Skip(context.HttpContext))
        {
            return null;
        }

        var siteKey = WebUtility.HtmlEncode(settings.SiteKey);
        var action = WebUtility.HtmlEncode(settings.Action);
        // Versioned, so a changed script reaches visitors at once instead of when a CDN's cache expires.
        var script = WebUtility.HtmlEncode(fileVersions.AddFileVersionToPath(context.HttpContext.Request.PathBase, ScriptPath));
        return new HtmlString($"""
            <input type="hidden" name="{TokenField}" data-wayfinder-recaptcha-token>
            <p class="{WebUtility.HtmlEncode(settings.NoticeClass)}" data-wayfinder-recaptcha-notice>
                This site is protected by reCAPTCHA and the Google
                <a href="https://policies.google.com/privacy" target="_blank" rel="noopener noreferrer" aria-label="Google Privacy Policy (opens in new tab)">Privacy Policy</a> and
                <a href="https://policies.google.com/terms" target="_blank" rel="noopener noreferrer" aria-label="Google Terms of Service (opens in new tab)">Terms of Service</a> apply.
            </p>
            <script src="https://www.google.com/recaptcha/api.js?render={siteKey}" async defer></script>
            <script src="{script}" data-recaptcha-site-key="{siteKey}" data-recaptcha-action="{action}" defer></script>
            """);
    }

    public async Task<string?> CheckAsync(StageSubmissionGuardContext context, IFormCollection form)
    {
        var settings = options.Value;
        if (settings.Skip(context.HttpContext))
        {
            return null;
        }

        var token = form[TokenField].ToString();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(settings.SecretKey))
        {
            SubmissionTurnedAway(logger, string.IsNullOrEmpty(settings.SecretKey) ? "the secret key is not configured" : "no token was sent");
            return Rejection;
        }

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(RecaptchaSubmissionGuard));
            // In the body, not the query string: URLs end up in logs, and the token is user input.
            using var response = await client.PostAsync(
                "https://www.google.com/recaptcha/api/siteverify",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["secret"] = settings.SecretKey, ["response"] = token }),
                context.HttpContext.RequestAborted);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<VerifyResult>(context.HttpContext.RequestAborted);
            if (result is { Success: true } && result.Score >= settings.MinimumScore && result.Action == settings.Action)
            {
                return null;
            }

            VerificationFailed(logger, result?.Success, result?.Score, result?.Action, string.Join(", ", result?.ErrorCodes ?? []));
            return Rejection;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            VerificationIncomplete(logger, ex);
            return Rejection;
        }
    }

    private sealed record VerifyResult(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "reCAPTCHA turned a submission away: {Reason}.")]
    private static partial void SubmissionTurnedAway(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "reCAPTCHA turned a submission away. Success: {Success}, Score: {Score}, Action: {Action}, Errors: {Errors}")]
    private static partial void VerificationFailed(ILogger logger, bool? success, double? score, string? action, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "reCAPTCHA verification could not be completed.")]
    private static partial void VerificationIncomplete(ILogger logger, Exception exception);
}
