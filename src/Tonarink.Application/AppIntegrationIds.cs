namespace Tonarink.Application;

/// <summary>
/// Stable registrations and cross-process contracts, not display names.
/// Keep these compatible with existing installations and Package.appxmanifest.
/// </summary>
public static class AppIntegrationIds
{
    public const string ProtocolScheme = "tonarink";
    public const string StartupTaskId = "TonarinkStartup";
    public const string StartupRegistryValue = "Tonarink";
    public const string DesktopExecutableName = "Tonarink.exe";
    public const string DesktopPackageName = "Tonarink.App";
    public const string PackageApplicationId = "App";
    public const string PrimaryInstanceKey = "Tonarink.Primary";
    public const string ShareIngestedEvent = @"Local\Tonarink.ShareIngested";
    public const string ExplorerShareEvent = @"Local\Tonarink.ExplorerShare";
    public const string RunningMutex = @"Local\Tonarink.App.Running";
    public const string WidgetCommandEvent = @"Local\Tonarink.WidgetCommand";
    public const string WidgetDefinitionId = "Tonarink_Receive";
    public const string ExplorerFavoriteIdPrefix = "Tonarink.ExplorerFavorite:";
    public const string CliPipePrefix = "Tonarink.Cli.";
    public const string SendToOwnershipMarker = "Tonarink Send To integration";
    public const string SendToShortcutName = "Tonarink.lnk";
    public const string PeopleContactListName = "Tonarink favorites";
}
