using System.Security.Claims;

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
              <link rel="stylesheet" href="/_content/Wayfinder.Rendering.GovUk/govuk-frontend/govuk-frontend.min.css">
              <link rel="stylesheet" href="/_content/Wayfinder.Rendering.GovUk/css/wayfinder-components.css">
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
                          <li class="govuk-service-navigation__item"><a class="govuk-service-navigation__link" href="/">Apply</a></li>
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

              <script type="module" src="/_content/Wayfinder.Rendering.GovUk/js/wayfinder-govuk-frontend-init.js"></script>
              <script src="/_content/Wayfinder.Rendering.GovUk/js/wayfinder-poll.js"></script>
              <script src="/_content/Wayfinder.Rendering.GovUk/js/wayfinder-slider.js"></script>
              <script type="module" src="/_content/Wayfinder.Rendering.GovUk/js/wayfinder-live-form.js"></script>
            </body>
            </html>
            """;
    }
}
