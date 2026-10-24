using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

enum SettingsToggleIconKind
{
    History,
    Startup,
    Pin,
    MinimizeToTray,
    Notification,
    Contact,
    ContextMenu,
    Checksum,
    DragDrop,
    Theme,
    Server,
    Language,
    Preview,
    Rename,
    ExecutionAlias,
    AutoReturn,
    ReceiveBadge,
    TopNavigation,
    SendTo,
    JumpList,
}

static class AnimatedSettingsToggleIcon
{
    public static Element Create(
        SettingsToggleIconKind kind,
        bool isOn,
        double iconSize = 24) =>
        Create(kind, isOn ? "On" : "Off", iconSize);

    public static Element Create(
        SettingsToggleIconKind kind,
        string state,
        double iconSize = 24)
    {
        return AnimatedIcon()
            .Width(iconSize)
            .Height(iconSize)
            .Set(icon =>
            {
                icon.Source ??= CreateSource(kind);
                icon.FallbackIconSource ??= new FontIconSource
                {
                    Glyph = kind switch
                    {
                        SettingsToggleIconKind.History => "\uE81C",
                        SettingsToggleIconKind.Startup => "\uEC4A",
                        SettingsToggleIconKind.Pin => "\uE72E",
                        SettingsToggleIconKind.MinimizeToTray => "\uF2AE",
                        SettingsToggleIconKind.Notification => "\uEA8F",
                        SettingsToggleIconKind.Contact => "\uE716",
                        SettingsToggleIconKind.ContextMenu => "\uE7AC",
                        SettingsToggleIconKind.Checksum => "\uF32A",
                        SettingsToggleIconKind.DragDrop => "\uF413",
                        SettingsToggleIconKind.Theme => "\uE706",
                        SettingsToggleIconKind.Server => "\uE703",
                        SettingsToggleIconKind.Language => "\uF2B7",
                        SettingsToggleIconKind.Preview => state == "Off" ? "\uED1A" : "\uE890",
                        SettingsToggleIconKind.Rename => AppIcons.Rename,
                        SettingsToggleIconKind.ExecutionAlias => AppIcons.ExecutionAlias,
                        SettingsToggleIconKind.AutoReturn => AppIcons.Home,
                        SettingsToggleIconKind.ReceiveBadge => AppIcons.Badge,
                        SettingsToggleIconKind.TopNavigation => AppIcons.TopNavigation,
                        SettingsToggleIconKind.SendTo => AppIcons.Send,
                        SettingsToggleIconKind.JumpList => AppIcons.JumpList,
                    },
                };

                Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(icon, state);
            });
    }

    private static IAnimatedVisualSource2 CreateSource(SettingsToggleIconKind kind) => kind switch
    {
        SettingsToggleIconKind.History => new SettingsHistoryToggleIcon(),
        SettingsToggleIconKind.Startup => new SettingsStartupToggleIcon(),
        SettingsToggleIconKind.Pin => new SettingsPinToggleIcon(),
        SettingsToggleIconKind.MinimizeToTray => new SettingsMinimizeToTrayToggleIcon(),
        SettingsToggleIconKind.Notification => new SettingsNotificationToggleIcon(),
        SettingsToggleIconKind.Contact => new SettingsContactToggleIcon(),
        SettingsToggleIconKind.ContextMenu => new SettingsContextMenuToggleIcon(),
        SettingsToggleIconKind.Checksum => new SettingsChecksumToggleIcon(),
        SettingsToggleIconKind.DragDrop => new SettingsDragDropToggleIcon(),
        SettingsToggleIconKind.Theme => new SettingsThemeIcon(),
        SettingsToggleIconKind.Server => new SettingsServerIcon(),
        SettingsToggleIconKind.Language => new SettingsLanguageIcon(),
        SettingsToggleIconKind.Preview => new SettingsPreviewToggleIcon(),
        SettingsToggleIconKind.Rename => new SettingsRenameFocusIcon(),
        SettingsToggleIconKind.ExecutionAlias => new SettingsExecutionAliasIcon(),
        SettingsToggleIconKind.AutoReturn => new SettingsAutoReturnToggleIcon(),
        SettingsToggleIconKind.ReceiveBadge => new SettingsReceiveBadgeToggleIcon(),
        SettingsToggleIconKind.TopNavigation => new SettingsTopNavigationToggleIcon(),
        SettingsToggleIconKind.SendTo => new NavigationSendIcon(),
        SettingsToggleIconKind.JumpList => new SettingsJumpListToggleIcon(),
    };
}
