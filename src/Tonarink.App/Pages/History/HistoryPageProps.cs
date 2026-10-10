using Microsoft.UI.Xaml;
using Tonarink.Services.History;

namespace Tonarink.Pages.History;

sealed record HistoryPageProps(
    string DownloadDirectory,
    ElementTheme Theme,
    Guid? JumpListHistoryId,
    Action<Guid> ConsumeJumpListHistory,
    IReceiveHistoryStore? HistoryStore = null);
