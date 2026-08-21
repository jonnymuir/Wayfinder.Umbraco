using Microsoft.AspNetCore.Http;
using Wayfinder.Models.ServiceDesign;

namespace Wayfinder.Umbraco.Services;

/// <summary>
/// Bridges this package's own richer <see cref="IServiceRequestFileStorage"/> (the one every
/// upload/download controller in a host actually saves a citizen's file through) onto
/// <c>Wayfinder.Engine.Abstractions.IServiceRequestFileStorage</c> — a distinct, narrower
/// interface the toolkit's own bulk-data-review machinery (<c>IBulkDatasetStore</c>) and any
/// <c>ISupportSystemClient</c> are written against, since that lower-level engine package has no
/// dependency on <see cref="IFormFile"/> or this package's richer <see cref="ServiceRequestFileReference"/>
/// shape. Registering both interfaces against the *same* underlying store (this adapter just
/// delegates) is what lets a citizen-uploaded file and the engine's own dataset ingest/support-
/// system round trip see the same bytes — two separate storage backends here would silently break
/// that round trip the moment either side tried to read what the other wrote.
/// </summary>
public sealed class EngineServiceRequestFileStorageAdapter(IServiceRequestFileStorage inner)
    : Wayfinder.Engine.Abstractions.IServiceRequestFileStorage
{
    public async Task<string> SaveAsync(string instanceId, string fieldKey, Stream content, string fileName, CancellationToken ct = default)
    {
        // FormFile's own ContentType/Length getters read Headers/the constructor args directly —
        // a bare `new FormFile(...)` leaves Headers null, which DiskServiceRequestFileStorage's
        // own file.ContentType read then NullReferenceExceptions on. FormFile is normally only
        // ever constructed by ASP.NET Core's own multipart form parser, which always sets this;
        // direct construction (the only option this adapter has, since it's handed a plain
        // Stream, not a real multipart request) needs to set it explicitly instead.
        var formFile = new FormFile(content, 0, content.Length, fieldKey, fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };
        var reference = await inner.SaveAsync(instanceId, fieldKey, formFile, ct);
        return reference.StorageKey;
    }

    public async Task<Stream?> OpenReadAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            return await inner.OpenReadAsync(new ServiceRequestFileReference { StorageKey = reference }, ct);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
