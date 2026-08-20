using System.Security.Claims;
using Wayfinder.Rendering.GovUk;

namespace Wayfinder.Umbraco.ReferenceApp;

/// <summary>
/// Shared HTML page chrome — the real GOV.UK Design System page skeleton (govuk-template classes,
/// generic header, service navigation, width container, footer), copied from
/// <c>Wayfinder.ReferenceApp/Services/PageShell.cs</c>'s own proven pattern in the core Wayfinder
/// repo. Used both by <see cref="ReferenceAppAuth"/>'s hand-rolled <c>/demo/login</c> minimal-API
/// route and by <c>Views/referenceHome.cshtml</c>, so the two never carry two different copies of
/// the same head/nav/footer markup.
/// </summary>
public static class ReferenceAppPageShell
{
    // Wayfinder.Rendering.GovUk's own static-asset routes carry no Cache-Control header, so a
    // browser's heuristic caching can keep serving an old copy of wayfinder-components.css across
    // a NuGet package bump with no round-trip check at all — confirmed live (this exact scenario:
    // a real style fix landed in a new package version, but a long-lived tab kept showing the old
    // CSS until a hard refresh). Query-string-versioned by that package's own assembly version, so
    // every real upgrade gets a new URL the browser has never cached, with no host-side
    // Cache-Control change needed.
    private static readonly string AssetVersion =
        typeof(GovUkComponents).Assembly.GetName().Version?.ToString() ?? "0";

    /// <summary>
    /// The "Home" page's own welcome copy — a plain C# constant, not inline in
    /// <c>Views/referenceHome.cshtml</c>, because a large multi-line raw string literal
    /// (<c>"""..."""</c>) inside a Razor <c>@{ }</c> code block trips up
    /// <c>CollectibleRuntimeViewCompiler</c>'s own compile step (confirmed live: every page threw
    /// <c>UmbracoCompilationException</c> with no useful diagnostic logged, even for requests that
    /// never executed the branch containing it — Razor compiles the whole file as one class
    /// regardless of which branch runs). Plain .cs files don't have this problem.
    /// </summary>
    public const string HomePageBody = """
        <h1 class="govuk-heading-l">Wayfinder.Umbraco reference app</h1>
        <p class="govuk-body">
          This is a real, bootable Umbraco 17 site proving <a class="govuk-link" href="https://github.com/jonnymuir/Wayfinder.Umbraco">Wayfinder.Umbraco</a>
          end to end — the citizen-facing stage block and the caseworker-facing worklist block,
          composed onto ordinary Umbraco pages via Block Grid, backed by a real in-process
          workflow engine (pickup/putback, access control, a join gateway with a live wait
          screen). Nothing here is mocked.
        </p>

        <div class="govuk-button-group">
          <a class="govuk-button" data-module="govuk-button" href="/apply">Apply for something</a>
          <a class="govuk-button govuk-button--secondary" data-module="govuk-button" href="/caseworker-queue">View the caseworker queue</a>
        </div>

        <h2 class="govuk-heading-m">Try it as different people</h2>
        <p class="govuk-body">
          <a class="govuk-link" href="/demo/login">Pick a demo persona</a> — a citizen
          (Alex Applicant) or a caseworker (Casey or Jordan Caseworker). No password needed.
        </p>

        <h2 class="govuk-heading-m">See how it's put together</h2>
        <p class="govuk-body">
          Everything on this site — the pages, the two Block Grid blocks, the demo service
          blueprint — is real Umbraco content and configuration, viewable and editable in the
          backoffice.
        </p>
        <p class="govuk-body">
          <a class="govuk-link" href="/umbraco">Sign in to the backoffice</a> with
          <code>admin@example.test</code> / <code>Wayfinder123!</code>.
        </p>
        """;

    public static string Render(string title, string bodyHtml, ClaimsPrincipal? user)
    {
        string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);

        var nav = user?.Identity?.IsAuthenticated == true
            ? $"""
                <li class="govuk-service-navigation__item">
                  <span class="govuk-service-navigation__link" style="cursor:default">Signed in as {Esc(user.Identity!.Name ?? "")} ({Esc(user.FindFirst(ClaimTypes.Role)?.Value ?? "")})</span>
                </li>
                <li class="govuk-service-navigation__item">
                  <form method="post" action="/demo/logout">
                    <button class="govuk-service-navigation__link govuk-button--text-as-link" type="submit" style="background:none;border:0;padding:0;font:inherit;cursor:pointer">Sign out</button>
                  </form>
                </li>
                """
            : """
                <li class="govuk-service-navigation__item"><a class="govuk-service-navigation__link" href="/demo/login">Demo login</a></li>
                """;

        return $"""
            <!doctype html>
            <html lang="en" class="govuk-template">
            <head>
              <meta charset="utf-8">
              <title>{Esc(title)} — Wayfinder.Umbraco reference app</title>
              <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
              <meta name="theme-color" content="#1d70b8">
              <link rel="stylesheet" href="/_content/Wayfinder.Rendering.GovUk/govuk-frontend/govuk-frontend.min.css?v={AssetVersion}">
              <link rel="stylesheet" href="/_content/Wayfinder.Rendering.GovUk/css/wayfinder-components.css?v={AssetVersion}">
            </head>
            <body class="govuk-template__body">
              <script>document.body.className += ' js-enabled' + ('noModule' in HTMLScriptElement.prototype ? ' govuk-frontend-supported' : '');</script>
              <a href="#main-content" class="govuk-skip-link" data-module="govuk-skip-link">Skip to main content</a>

              <header class="govuk-template__header">
                <div class="govuk-generic-header">
                  <div class="govuk-generic-header__container govuk-width-container">
                    <div class="govuk-generic-header__logo">
                      <a href="/" class="govuk-generic-header__homepage-link">Wayfinder.Umbraco reference app</a>
                    </div>
                  </div>
                </div>
                <div class="govuk-service-navigation" data-module="govuk-service-navigation">
                  <div class="govuk-width-container">
                    <div class="govuk-service-navigation__container">
                      <nav aria-label="Menu" class="govuk-service-navigation__wrapper">
                        <ul class="govuk-service-navigation__list">
                          <li class="govuk-service-navigation__item"><a class="govuk-service-navigation__link" href="/">Home</a></li>
                          <li class="govuk-service-navigation__item"><a class="govuk-service-navigation__link" href="/apply">Apply</a></li>
                          <li class="govuk-service-navigation__item"><a class="govuk-service-navigation__link" href="/caseworker-queue">Caseworker queue</a></li>
                          {nav}
                          <li class="govuk-service-navigation__item"><a class="govuk-service-navigation__link" href="/umbraco">Backoffice</a></li>
                        </ul>
                      </nav>
                    </div>
                  </div>
                </div>
              </header>

              <div class="govuk-width-container">
                <main class="govuk-main-wrapper" id="main-content">
                  {bodyHtml}
                </main>
              </div>

              <footer class="govuk-template__footer">
                <div class="govuk-footer">
                  <div class="govuk-width-container">
                    <div class="govuk-footer__meta">
                      <div class="govuk-footer__meta-item govuk-footer__meta-item--grow">
                        <h2 class="govuk-visually-hidden">Support links</h2>
                        <div class="govuk-footer__meta-custom">
                          A transient Wayfinder.Umbraco reference host — <a class="govuk-footer__link" href="https://github.com/jonnymuir/Wayfinder.Umbraco">github.com/jonnymuir/Wayfinder.Umbraco</a>, MIT licensed.
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </footer>

              <script type="module" src="/_content/Wayfinder.Rendering.GovUk/js/wayfinder-govuk-frontend-init.js?v={AssetVersion}"></script>
              <script src="/_content/Wayfinder.Rendering.GovUk/js/wayfinder-poll.js?v={AssetVersion}"></script>
              <script src="/_content/Wayfinder.Rendering.GovUk/js/wayfinder-slider.js?v={AssetVersion}"></script>
              <script type="module" src="/_content/Wayfinder.Rendering.GovUk/js/wayfinder-live-form.js?v={AssetVersion}"></script>
            </body>
            </html>
            """;
    }
}
