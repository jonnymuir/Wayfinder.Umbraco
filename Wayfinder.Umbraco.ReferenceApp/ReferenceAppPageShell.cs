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
        <h1 class="govuk-heading-xl">Wayfinder.Umbraco reference app</h1>
        <p class="govuk-body-l">
          A real, bootable Umbraco 17 site that proves
          <a class="govuk-link" href="https://github.com/jonnymuir/Wayfinder.Umbraco">Wayfinder.Umbraco</a>
          end to end. Every service below is an ordinary Umbraco page with a Wayfinder Block Grid
          block on it, backed by a real in-process workflow engine. Nothing is mocked.
        </p>
        <p class="govuk-body">
          Wayfinder's model is the
          <a class="govuk-link" href="https://www.nngroup.com/articles/service-blueprints-definition/">Nielsen Norman Group service blueprint</a>
          made executable: a citizen journey, a backstage caseworker queue, and support processes,
          separated by the lines of interaction and visibility.
        </p>

        <h2 class="govuk-heading-l govuk-!-margin-top-8">Start here: pick who you are</h2>
        <p class="govuk-body">
          Every journey needs a signed-in persona. <a class="govuk-link" href="/demo/login">Choose a demo persona</a>
          (no password), then open one of the services below. Switch personas any time from the
          same page.
        </p>
        <table class="govuk-table">
          <thead class="govuk-table__head">
            <tr class="govuk-table__row">
              <th scope="col" class="govuk-table__header">Persona</th>
              <th scope="col" class="govuk-table__header">Role</th>
              <th scope="col" class="govuk-table__header">Use for</th>
            </tr>
          </thead>
          <tbody class="govuk-table__body">
            <tr class="govuk-table__row">
              <td class="govuk-table__cell">Alex Applicant</td>
              <td class="govuk-table__cell">Citizen</td>
              <td class="govuk-table__cell">Submitting an application on any service</td>
            </tr>
            <tr class="govuk-table__row">
              <td class="govuk-table__cell">Casey Caseworker</td>
              <td class="govuk-table__cell">Caseworker / NJF registrar</td>
              <td class="govuk-table__cell">Reviewing and deciding applications</td>
            </tr>
            <tr class="govuk-table__row">
              <td class="govuk-table__cell">Jordan Caseworker</td>
              <td class="govuk-table__cell">Caseworker / NJF registrar</td>
              <td class="govuk-table__cell">A second caseworker — shows team pickup / hand-off</td>
            </tr>
          </tbody>
        </table>

        <h2 class="govuk-heading-l govuk-!-margin-top-8">The services</h2>

        <div class="govuk-!-margin-bottom-8">
          <h3 class="govuk-heading-m govuk-!-margin-bottom-1">Reference demo</h3>
          <p class="govuk-body-s govuk-!-margin-bottom-2"><strong>Shows off:</strong> the two Block Grid blocks, mandatory pickup / putback, access control, and a join gateway with a live "we're reviewing your request" wait screen.</p>
          <p class="govuk-body">
            A deliberately tiny two-actor service. A citizen submits a free-text request; a
            caseworker approves or rejects it. The smallest thing that exercises the whole engine.
          </p>
          <div class="govuk-button-group">
            <a class="govuk-button" data-module="govuk-button" href="/apply">Apply <span class="govuk-visually-hidden">on the reference demo</span> (as a citizen)</a>
            <a class="govuk-button govuk-button--secondary" data-module="govuk-button" href="/caseworker-queue">Caseworker queue</a>
          </div>
        </div>

        <div class="govuk-!-margin-bottom-8">
          <h3 class="govuk-heading-m govuk-!-margin-bottom-1">Register as a juggling coach</h3>
          <p class="govuk-body-s govuk-!-margin-bottom-2"><strong>Shows off:</strong> NN/g's <em>support processes</em> lane as a <strong>configuration-only webhook support system</strong> — an external check wired up with one block of <code>appsettings.json</code> and <strong>no bespoke code</strong>.</p>
          <p class="govuk-body">
            A coach applies to join the National Juggling Federation coaching register. An NJF
            registrar reviews the application, then runs a coaching-standards check against an
            external body. That check is an <strong>Umbraco Automate</strong> automation, seeded
            and published in code: it branches on the applicant's own data, emails a standards
            officer, waits for a human <em>accredited / provisional / referred</em> decision, then
            resolves the registrar's wait screen. The registrar keeps the case through the whole
            send-and-wait round trip.
          </p>
          <div class="govuk-inset-text">
            Enter <strong>2 or more</strong> years of experience <strong>and</strong> a safeguarding
            disclosure reference to see the automation auto-accredit. Otherwise it routes to a human,
            who approves it in the backoffice <strong>Automate</strong> section.
          </div>
          <div class="govuk-button-group">
            <a class="govuk-button" data-module="govuk-button" href="/apply-to-coach">Apply to coach (as a citizen)</a>
            <a class="govuk-button govuk-button--secondary" data-module="govuk-button" href="/coaching-register-queue">Coaching register queue</a>
          </div>
        </div>

        <h2 class="govuk-heading-l govuk-!-margin-top-8">Behind the scenes</h2>
        <ul class="govuk-list govuk-list--bullet">
          <li><a class="govuk-link" href="/umbraco">Umbraco backoffice</a> — sign in with <code>admin@example.test</code> / <code>Wayfinder123!</code>. The pages, the Block Grid blocks, the service blueprints and the seeded Automate automation are all real content and configuration you can inspect and edit.</li>
          <li><strong>Blueprints</strong> (Settings section of the backoffice) — the JSON behind each service, with a visual editor.</li>
          <li><strong>Automate</strong> section — the "NJF Coaching Standards" automation, its run history, and pending approvals.</li>
          <li><a class="govuk-link" href="https://localhost:8025">Mailpit</a> — the mailbox the coaching-standards automation sends to (only when the app is launched via the Aspire host).</li>
        </ul>
        <p class="govuk-body">
          Full walkthrough:
          <a class="govuk-link" href="https://github.com/jonnymuir/Wayfinder.Umbraco/blob/main/docs/automate-support-system-walkthrough.md">docs/automate-support-system-walkthrough.md</a>.
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
