using System.Net;
using System.Text.RegularExpressions;

namespace Wayfinder.Umbraco.Starter.IntegrationTests;

/// <summary>Submits a rendered form the way a browser would: its own hidden fields, plus what the person enters.</summary>
internal static partial class Forms
{
    public static async Task<string> GetHtml(this HttpClient browser, string path)
    {
        var response = await browser.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"GET {path} answered {(int)response.StatusCode}: {html[..Math.Min(html.Length, 2000)]}");
        }

        return html;
    }

    /// <summary>Posts the first form whose action contains <paramref name="actionContains"/>; returns where it redirects.</summary>
    public static async Task<string> Submit(this HttpClient browser, string html, string actionContains, IDictionary<string, string>? entered = null)
    {
        var form = FormPattern().Matches(html)
            .Select(match => (Action: WebUtility.HtmlDecode(match.Groups["action"].Value), Body: match.Groups["body"].Value))
            .First(candidate => candidate.Action.Contains(actionContains, StringComparison.Ordinal));

        var fields = HiddenPattern().Matches(form.Body)
            .ToDictionary(match => match.Groups["name"].Value, match => WebUtility.HtmlDecode(match.Groups["value"].Value));
        foreach (var (name, value) in entered ?? new Dictionary<string, string>())
        {
            fields[name] = value;
        }

        var response = await browser.PostAsync(form.Action, new FormUrlEncodedContent(fields));
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"POST {form.Action} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return response.Headers.Location!.OriginalString;
    }

    [GeneratedRegex("""<form[^>]*\saction="(?<action>[^"]*)"[^>]*>(?<body>.*?)</form>""", RegexOptions.Singleline)]
    private static partial Regex FormPattern();

    [GeneratedRegex("""<input(?=[^>]*type="hidden")(?=[^>]*name="(?<name>[^"]*)")(?=[^>]*value="(?<value>[^"]*)")[^>]*>""")]
    private static partial Regex HiddenPattern();
}
