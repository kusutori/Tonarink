namespace Tonarink.Services.Activation;

sealed record ShareTargetPayload(
    Guid Id,
    IReadOnlyList<ShareTargetItem> Items,
    string? SuggestedContactFingerprint = null);

union ShareTargetItem(ShareTargetItem.FileSystem, ShareTargetItem.Text)
{
    public sealed record FileSystem(string Path, bool IsDirectory);

    public sealed record Text(string Value, string FileName);
}
