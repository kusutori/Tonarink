using LocalSendDotNet;
using Microsoft.UI.Reactor.Input;

namespace Tonarink.Services.Tray;

sealed record DroppedSendPayload(
    IReadOnlyList<SendItem> Items,
    long TotalBytes,
    IReadOnlyList<string> LocalPaths);

static class DroppedSendItemReader
{
    public static async Task<DroppedSendPayload> ReadAsync(
        DragData data, CancellationToken cancellationToken = default)
    {
        var selected = await SelectedSendItemReader.ReadDroppedAsync(data, cancellationToken);
        return new(
            selected.Select(static item => item.Item).ToArray(),
            selected.Sum(static item => item.Length),
            selected.Where(static item => !string.IsNullOrWhiteSpace(item.LocalPath))
                .Select(static item => item.LocalPath!).ToArray());
    }
}
