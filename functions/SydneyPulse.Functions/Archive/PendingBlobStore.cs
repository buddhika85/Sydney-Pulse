// PendingBlobStore.cs
// -------------------
// Default IPendingBlobStore implementation. Resolves the pending container
// once at construction and hands out AppendBlobClient instances per call.
//
// Registered as a singleton in Program.cs — BlobServiceClient and the
// container client are both thread-safe and reuse the same HTTP pipeline.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;

namespace SydneyPulse.Functions.Archive;

public sealed class PendingBlobStore : IPendingBlobStore
{
    // Cached container — resolving once avoids a per-call BlobServiceClient hop.
    private readonly BlobContainerClient _container;

    public PendingBlobStore(BlobServiceClient blobs, IOptions<ArchiveOptions> options)
    {
        _container = blobs.GetBlobContainerClient(options.Value.PendingContainer);
    }

    public AppendBlobClient GetAppendBlob(string partitionRelativePath) =>
        _container.GetAppendBlobClient(partitionRelativePath);
}
