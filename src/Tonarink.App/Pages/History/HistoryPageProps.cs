using Microsoft.UI.Xaml;

namespace Tonarink.Pages.History;

sealed record HistoryPageProps(
    string DownloadDirectory,
    ElementTheme Theme,
    Guid? NavigationHistoryId,
    Action<Guid> ConsumeNavigationHistory,
    IReceiveHistoryStore? HistoryStore = null);
