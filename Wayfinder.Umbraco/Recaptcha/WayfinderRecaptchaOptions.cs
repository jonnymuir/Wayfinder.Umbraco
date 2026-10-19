using Microsoft.AspNetCore.Http;

namespace Wayfinder.Umbraco.Recaptcha;

/// <summary>Settings for <see cref="Extensions.WayfinderRecaptchaServiceCollectionExtensions.AddWayfinderRecaptcha"/>.</summary>
public sealed class WayfinderRecaptchaOptions
{
    /// <summary>The reCAPTCHA v3 site key (public: it goes in the page).</summary>
    public string SiteKey { get; set; } = "";

    /// <summary>The reCAPTCHA v3 secret key. Keep it out of source and committed configuration.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>The lowest score Google may give a submission for it to go through. Defaults to 0.5.</summary>
    public double MinimumScore { get; set; } = 0.5;

    /// <summary>The action name each token is requested and checked for. Defaults to <c>submit</c>.</summary>
    public string Action { get; set; } = "submit";

    /// <summary>The class on the "protected by reCAPTCHA" notice, to suit the site's styles. Defaults to GOV.UK's small body text.</summary>
    public string NoticeClass { get; set; } = "govuk-body-s";

    /// <summary>
    /// Who needn't prove they're human, such as the site's own signed-in team. Defaults to nobody. Their
    /// forms carry no reCAPTCHA and their submissions are not checked.
    /// </summary>
    public Func<HttpContext, bool> Skip { get; set; } = static _ => false;
}
