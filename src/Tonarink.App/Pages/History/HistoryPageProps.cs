namespace Tonarink.Pages.History;

sealed record HistoryPageProps(
    string DownloadDirectory,
    Guid? NavigationHistoryId,
    Action<Guid> ConsumeNavigationHistory,
    IReceiveHistoryStore? HistoryStore = null);
