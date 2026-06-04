// IPendingBlobStore.cs
// --------------------
// Thin abstraction over the "pending" Data Lake container — the staging area
// where ArchiverIngestFunction appends JSONL lines per Hive partition.
//
// Why this exists: Azure SDK's BlobContainerClient.GetAppendBlobClient(string)
// is an extension method (SpecializedBlobExtensions), which Moq cannot mock.
// Wrapping it in this interface gives us a virtual seam for unit tests and a
// single home for the container-resolution logic so each Function isn't
// repeating the BlobServiceClient → BlobContainerClient hop.
//
// Lives in the Functions project (not Core) because it directly surfaces an
// Azure SDK type — pure-domain interfaces stay in Core; SDK-coupled ones stay here.

using Azure.Storage.Blobs.Specialized;

namespace SydneyPulse.Functions.Archive;

public interface IPendingBlobStore
{
    // Returns an AppendBlobClient pointing at {pendingContainer}/{partitionRelativePath}.
    // Caller composes the partition-relative path
    // (e.g. "yyyy=2026/MM=06/dd=04/HH=14/events.jsonl").
    AppendBlobClient GetAppendBlob(string partitionRelativePath);
}
