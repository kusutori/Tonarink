using Microsoft.UI.Xaml;
using Tonarink.Services.History;

namespace Tonarink.Pages.History;

sealed record HistoryPageProps(
    string DownloadDirectory,
    ElementTheme Theme,
    Guid? NavigationHistoryId,
    Action<Guid> ConsumeNavigationHistory,
    IReceiveHistoryStore? HistoryStore = null);
