// DlqExporter.cs
// ---------------
// Bulk DLQ message export from a Service Bus topic subscription to CSV.
// Read-only PEEK — does not consume, does not move messages, queue state
// is unchanged after the run.
//
// Why this exists: SP1-16 Phase D Alerter chain investigation. The portal
// Service Bus Explorer peeks small batches at a time; for a 19k DLQ we
// need a scripted bulk export. Same pattern as the TfNSW discovery spike
// in Program.cs and the CloudEvent round-trip test from story #9 —
// purpose-built console runners that exercise one external boundary at a
// time without dragging the Functions host into local dev.
//
// Auth: two paths, env-var-selected.
//   - $env:ServiceBus__ConnectionString set → use connection string. Fastest
//     for a local one-shot when the dev identity lacks the data-plane role
//     on the shared namespace (ADR-0003 — only the Function App MI gets it).
//   - Otherwise → DefaultAzureCredential (matches the Functions worker).
//     Caller needs "Azure Service Bus Data Receiver" on the topic/subscription
//     (or the parent namespace).

using System.Globalization;
using System.Text;
using Azure.Identity;
using Azure.Messaging.ServiceBus;

internal static partial class SpikeRunner
{
    // Default points at the shared DevPulse namespace (ADR-0003). Override
    // by setting ServiceBus__FullyQualifiedNamespace — symmetric with the
    // Functions worker's app-setting key so the same env conventions apply.
    private const string DefaultSbFullyQualifiedNamespace =
        "devpulse-service-bus.servicebus.windows.net";

    // ExportDlqAsync — peek every message in <topic>/<subscription>'s DLQ
    // and stream them out as CSV. Streaming write so a 19k run doesn't
    // hold all messages in memory.
    public static async Task<int> ExportDlqAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine(
                "usage: dotnet run -- dlq <topic> <subscription> <outputCsv>");
            return 1;
        }

        var topic = args[0];
        var subscription = args[1];
        var outputCsv = args[2];

        // 1. SB client. Prefer a connection string when set (fast local path,
        //    bypasses RBAC); otherwise fall back to az-login identity. Either
        //    way the rest of the code is the same.
        //    SubQueue.DeadLetter targets the $DeadLetterQueue side of the
        //    subscription specifically — not the active messages.
        var connectionString =
            Environment.GetEnvironmentVariable("ServiceBus__ConnectionString");

        ServiceBusClient client;
        string authMode;
        if (!string.IsNullOrEmpty(connectionString))
        {
            client = new ServiceBusClient(connectionString);
            authMode = "connection string";
        }
        else
        {
            var fqns =
                Environment.GetEnvironmentVariable("ServiceBus__FullyQualifiedNamespace")
                ?? DefaultSbFullyQualifiedNamespace;
            client = new ServiceBusClient(fqns, new DefaultAzureCredential());
            authMode = $"DefaultAzureCredential against {fqns}";
        }
        await using var _ = client;
        await using var receiver = client.CreateReceiver(
            topic,
            subscription,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        Console.Error.WriteLine(
            $"Peeking DLQ for {topic} / {subscription} (auth: {authMode}) ...");

        // 2. Write CSV header. Stream-write each row directly to disk so a 19k
        //    run doesn't materialise all messages in memory at once.
        await using var writer = new StreamWriter(outputCsv, append: false, Encoding.UTF8);
        await writer.WriteLineAsync(
            "SequenceNumber,MessageId,EnqueuedTimeUtc,DeliveryCount," +
            "DeadLetterReason,DeadLetterErrorDescription," +
            "Subject,ContentType,BodyLength,BodyPreview");

        // 3. Paginate via PeekMessagesAsync(maxMessages, fromSequenceNumber).
        //    Cursor advances by last-sequence + 1 each page. Loop exits when a
        //    page returns zero — the canonical "we've drained the peek window".
        const int pageSize = 100;
        long? nextSequence = null;
        var total = 0;

        while (true)
        {
            var batch = await receiver.PeekMessagesAsync(pageSize, nextSequence);
            if (batch.Count == 0) break;

            foreach (var m in batch)
            {
                await writer.WriteLineAsync(FormatRow(m));
                total++;
            }

            nextSequence = batch[^1].SequenceNumber + 1;

            // Progress on stderr so stdout (if redirected) stays clean.
            Console.Error.WriteLine($"  peeked {total} so far (cursor {nextSequence})");
        }

        Console.Error.WriteLine($"Exported {total} DLQ messages to {outputCsv}.");
        return 0;
    }

    // FormatRow — one CSV line per message. Body is truncated to 300 chars
    // and one-lined so Excel doesn't wrap rows on embedded newlines in
    // JSON payloads. BodyLength column preserves the original size for
    // signal even when the preview is truncated.
    private static string FormatRow(ServiceBusReceivedMessage m)
    {
        var bodyStr = m.Body.ToString();
        var bodyLen = bodyStr.Length;
        var preview = bodyStr.Length > 300 ? bodyStr[..300] : bodyStr;
        var oneLine = preview.Replace("\r", " ").Replace("\n", " ");

        return string.Join(",", new[]
        {
            m.SequenceNumber.ToString(CultureInfo.InvariantCulture),
            CsvEscape(m.MessageId),
            m.EnqueuedTime.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            m.DeliveryCount.ToString(CultureInfo.InvariantCulture),
            CsvEscape(m.DeadLetterReason),
            CsvEscape(m.DeadLetterErrorDescription),
            CsvEscape(m.Subject),
            CsvEscape(m.ContentType),
            bodyLen.ToString(CultureInfo.InvariantCulture),
            CsvEscape(oneLine)
        });
    }

    // CsvEscape — RFC 4180 quoting. Always wrap in double-quotes and
    // double-up any embedded double-quote. Avoids Excel mis-parsing when
    // error descriptions / JSON bodies contain commas or quotes.
    private static string CsvEscape(string? value)
    {
        if (value is null) return "\"\"";
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
