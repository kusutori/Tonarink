using Microsoft.UI.Xaml;

namespace Tonarink.Pages.History;

sealed record HistoryPageProps(
    string DownloadDirectory,
    ElementTheme Theme,
    Guid? JumpListHistoryId,
    Action<Guid> ConsumeJumpListHistory);
