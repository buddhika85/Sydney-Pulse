// PendingBlobStore.cs
// -------------------
// Default IPendingBlobStore implementation. Resolves the pending container
// once at construction and hands out AppendBlobClient instances per call.
//
// Registered as a singleton in Program.cs — BlobServiceClient and the
// container client are both thread-safe and reuse the same HTTP pipeline.

using System.Runtime.CompilerServices;
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

    // Streams distinct partition prefixes from the pending container. Each blob
    // sits under one partition prefix (e.g. ".../HH=07/events.jsonl"), so the
    // partition is everything before the final slash. A HashSet dedupes across
    // any future case where one partition holds more than one blob.
    public async IAsyncEnumerable<string> ListPartitionPathsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>();
        await foreach (var blob in _container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            var lastSlash = blob.Name.LastIndexOf('/');
            if (lastSlash <= 0) continue;          // defensive — blob name without a partition prefix
            var partition = blob.Name[..lastSlash];
            if (seen.Add(partition))
                yield return partition;
        }
    }
}
