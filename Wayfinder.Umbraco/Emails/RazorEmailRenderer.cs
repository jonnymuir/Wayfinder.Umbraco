using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Wayfinder.Umbraco.Emails;

/// <summary>Renders an email template, a Razor view, to a string, outside any web request.</summary>
public sealed class RazorEmailRenderer(IRazorViewEngine viewEngine, ITempDataProvider tempDataProvider, IServiceProvider services)
{
    public async Task<string> RenderAsync(string viewPath, WayfinderEmailModel model)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var found = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: true);
        if (found.View is not { } view)
        {
            throw new InvalidOperationException($"Email template '{viewPath}' was not found. Searched: {string.Join(", ", found.SearchedLocations)}");
        }

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext,
            view,
            new ViewDataDictionary<WayfinderEmailModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model },
            new TempDataDictionary(httpContext, tempDataProvider),
            writer,
            new HtmlHelperOptions());
        await view.RenderAsync(viewContext);
        return writer.ToString();
    }
}
