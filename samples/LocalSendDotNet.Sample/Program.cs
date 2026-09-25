using LocalSendDotNet;

var root = Path.Combine(Path.GetTempPath(), "LocalSendDotNet.Sample");
await using var node = new LocalSendNode(new LocalSendOptions
{
    Alias = $"Sample on {Environment.MachineName}",
    DataDirectory = Path.Combine(root, "identity"),
    DownloadDirectory = Path.Combine(root, "downloads")
});

using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await node.StartAsync(stop.Token);
Console.WriteLine($"Running as {node.Identity!.Alias} ({node.Identity.Fingerprint})");
Console.WriteLine("Watching LocalSend peers for 10 seconds...");

try
{
    await foreach (var change in node.WatchDeviceChangesAsync(stop.Token))
        Console.WriteLine($"{change.Kind}: {change.Device.Alias} at {change.Device.PreferredEndpoint}");
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }

internal static class OutcomeFormatting
{
    public static string Describe(SendOutcome outcome) => outcome switch
    {
        SendOutcome.Completed completed => $"Sent {completed.Items.Count} item(s).",
        SendOutcome.Cancelled cancelled => $"Cancelled after {cancelled.Items.Count} item(s).",
        SendOutcome.PinRequired required => required.InvalidPin ? "Incorrect PIN." : "PIN required.",
        SendOutcome.PinRateLimited => "PIN attempts are rate-limited.",
        SendOutcome.PeerBusy => "The peer is busy.",
        SendOutcome.Declined => "The peer declined the transfer.",
        SendOutcome.Failed failed => failed.Failure.Message,
    };
}
