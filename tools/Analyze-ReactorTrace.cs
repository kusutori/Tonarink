#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Diagnostics.Tracing.TraceEvent@3.1.21

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.EventPipe;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run tools/Analyze-ReactorTrace.cs -- <trace.nettrace> [output.json]");
    return 1;
}

var tracePath = Path.GetFullPath(args[0]);
var outputPath = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
var openSpans = new Dictionary<(int ThreadId, string Task), Stack<OpenSpan>>();
var completedSpans = new List<CompletedSpan>();
var eventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
var reconcileStops = new List<ReconcileStop>();

using (var source = new EventPipeEventSource(tracePath))
{
    source.Dynamic.All += traceEvent =>
    {
        if (!string.Equals(traceEvent.ProviderName, "Microsoft-UI-Reactor", StringComparison.Ordinal))
            return;

        var eventName = traceEvent.EventName ?? "<unnamed>";
        eventCounts[eventName] = eventCounts.GetValueOrDefault(eventName) + 1;

        var task = string.IsNullOrWhiteSpace(traceEvent.TaskName)
            ? EventBaseName(eventName)
            : traceEvent.TaskName;
        var key = (traceEvent.ThreadID, task);

        if (traceEvent.Opcode == TraceEventOpcode.Start)
        {
            if (!openSpans.TryGetValue(key, out var stack))
                openSpans[key] = stack = new Stack<OpenSpan>();

            stack.Push(new OpenSpan(
                traceEvent.TimeStampRelativeMSec,
                FirstStringPayload(traceEvent) ?? task));
        }
        else if (traceEvent.Opcode == TraceEventOpcode.Stop
                 && openSpans.TryGetValue(key, out var stack)
                 && stack.Count > 0)
        {
            var start = stack.Pop();
            completedSpans.Add(new CompletedSpan(
                task,
                start.Label,
                traceEvent.TimeStampRelativeMSec - start.TimestampMilliseconds));
        }

        if (eventName.Equals("ReconcileStop", StringComparison.OrdinalIgnoreCase)
            || (task.Equals("Reconcile", StringComparison.OrdinalIgnoreCase)
                && traceEvent.Opcode == TraceEventOpcode.Stop))
        {
            reconcileStops.Add(new ReconcileStop(
                PayloadInt(traceEvent, "elementsDiffed"),
                PayloadInt(traceEvent, "elementsSkipped"),
                PayloadInt(traceEvent, "uiElementsCreated"),
                PayloadInt(traceEvent, "uiElementsModified")));
        }
    };

    source.Process();
}

var spanSummaries = completedSpans
    .GroupBy(span => span.Task)
    .Select(group => Summarize(group.Key, group.Select(span => span.DurationMilliseconds)))
    .OrderByDescending(summary => summary.P95Milliseconds)
    .ToArray();

var slowestLabels = completedSpans
    .GroupBy(span => $"{span.Task}: {span.Label}")
    .Select(group => Summarize(group.Key, group.Select(span => span.DurationMilliseconds)))
    .Where(summary => summary.Count >= 2)
    .OrderByDescending(summary => summary.P95Milliseconds)
    .Take(20)
    .ToArray();

var report = new
{
    Trace = tracePath,
    Events = eventCounts.OrderBy(pair => pair.Key).ToDictionary(),
    Spans = spanSummaries,
    SlowestLabels = slowestLabels,
    Reconcile = new
    {
        Count = reconcileStops.Count,
        AverageElementsDiffed = Average(reconcileStops.Select(item => item.ElementsDiffed)),
        AverageElementsSkipped = Average(reconcileStops.Select(item => item.ElementsSkipped)),
        AverageUiElementsCreated = Average(reconcileStops.Select(item => item.UiElementsCreated)),
        AverageUiElementsModified = Average(reconcileStops.Select(item => item.UiElementsModified)),
        MaximumElementsDiffed = Maximum(reconcileStops.Select(item => item.ElementsDiffed)),
        MaximumUiElementsCreated = Maximum(reconcileStops.Select(item => item.UiElementsCreated)),
        MaximumUiElementsModified = Maximum(reconcileStops.Select(item => item.UiElementsModified)),
    },
};

var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
});
Console.WriteLine(json);
if (outputPath is not null)
    File.WriteAllText(outputPath, json);

return 0;

static string EventBaseName(string eventName) =>
    eventName.EndsWith("Start", StringComparison.Ordinal)
        ? eventName[..^"Start".Length]
        : eventName.EndsWith("Stop", StringComparison.Ordinal)
            ? eventName[..^"Stop".Length]
            : eventName;

static string? FirstStringPayload(TraceEvent traceEvent)
{
    foreach (var name in traceEvent.PayloadNames ?? [])
    {
        if (traceEvent.PayloadByName(name) is string value && !string.IsNullOrWhiteSpace(value))
            return value;
    }

    return null;
}

static int PayloadInt(TraceEvent traceEvent, string name)
{
    try
    {
        return Convert.ToInt32(traceEvent.PayloadByName(name));
    }
    catch (Exception)
    {
        return 0;
    }
}

static SpanSummary Summarize(string name, IEnumerable<double> values)
{
    var sorted = values.Order().ToArray();
    return new SpanSummary(
        name,
        sorted.Length,
        Round(sorted.Average()),
        Round(Percentile(sorted, 0.50)),
        Round(Percentile(sorted, 0.95)),
        Round(sorted[^1]));
}

static double Percentile(double[] sorted, double percentile)
{
    if (sorted.Length == 0)
        return 0;

    var index = Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1);
    return sorted[index];
}

static double Average(IEnumerable<int> values)
{
    var materialized = values.ToArray();
    return materialized.Length == 0 ? 0 : Round(materialized.Average());
}

static int Maximum(IEnumerable<int> values) => values.DefaultIfEmpty().Max();
static double Round(double value) => Math.Round(value, 3);

sealed record OpenSpan(double TimestampMilliseconds, string Label);
sealed record CompletedSpan(string Task, string Label, double DurationMilliseconds);
sealed record ReconcileStop(int ElementsDiffed, int ElementsSkipped, int UiElementsCreated, int UiElementsModified);
sealed record SpanSummary(
    string Name,
    int Count,
    double MeanMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double MaximumMilliseconds);
