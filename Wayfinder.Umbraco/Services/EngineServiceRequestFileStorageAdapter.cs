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
        var formFile = new FormFile(content, 0, content.Length, fieldKey, fileName);
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
