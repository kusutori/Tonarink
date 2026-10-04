using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

public static class AnimatedButtons
{
    public static Element SettingsHistoryIcon(bool trigger, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(SettingsToggleIconKind.History, trigger, iconSize);

    public static Element SettingsStartupIcon(bool trigger, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(SettingsToggleIconKind.Startup, trigger, iconSize);

    public static Element SettingsPinIcon(bool isOn, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(SettingsToggleIconKind.Pin, isOn, iconSize);

    public static Element SettingsNotificationIcon(bool isOn, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(SettingsToggleIconKind.Notification, isOn, iconSize);

    public static Element SettingsContactIcon(bool isOn, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(SettingsToggleIconKind.Contact, isOn, iconSize);

    public static Element SettingsContextMenuIcon(bool isOn, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(SettingsToggleIconKind.ContextMenu, isOn, iconSize);

    public static Element SettingsChecksumIcon(bool isOn, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(SettingsToggleIconKind.Checksum, isOn, iconSize);

    public static Element SettingsDragDropIcon(bool isOn, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(SettingsToggleIconKind.DragDrop, isOn, iconSize);

    public static Element SettingsThemeIcon(bool isDark, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(
            SettingsToggleIconKind.Theme,
            isDark ? "Moon" : "Sun",
            iconSize);

    public static Element SettingsLanguageIcon(int animationVersion, double iconSize = 24) =>
        AnimatedSettingsToggleIcon.Create(
            SettingsToggleIconKind.Language,
            $"Language{Math.Abs(animationVersion % 3)}",
            iconSize);

    public static Element Add(
        string automationName,
        Action onClick,
        string? label = null,
        string? toolTip = null,
        bool isEnabled = true,
        double iconSize = 20) =>
        Component<AnimatedAddButton, AnimatedAddButtonProps>(
            new(automationName, onClick, label, toolTip, isEnabled, iconSize));

    public static Element OpenFolder(
        string automationName,
        Action onClick,
        string? label = null,
        string? toolTip = null,
        bool isEnabled = true,
        double iconSize = 20) =>
        Component<AnimatedOpenFolderButton, AnimatedOpenFolderButtonProps>(
            new(automationName, onClick, label, toolTip, isEnabled, iconSize));

    public static Element More(
        string automationName,
        Element flyout,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedMoreButton, AnimatedMoreButtonProps>(
            new(automationName, flyout, toolTip, isEnabled));

    public static Element Rename(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedRenameButton, AnimatedRenameButtonProps>(
            new(automationName, onClick, toolTip, isEnabled));

    public static Element DisplayZoom(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedDisplayZoomButton, AnimatedDisplayZoomButtonProps>(
            new(automationName, onClick, toolTip, isEnabled));

    public static Element QrCode(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedQrCodeButton, AnimatedQrCodeButtonProps>(
            new(automationName, onClick, toolTip, isEnabled));

    public static Element Share(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedShareButton, AnimatedShareButtonProps>(
            new(automationName, onClick, toolTip, isEnabled));

    public static Element Link(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedLinkButton, AnimatedLinkButtonProps>(
            new(automationName, onClick, toolTip, isEnabled));

    public static Element QuickActions(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true,
        double iconSize = 24) =>
        Component<AnimatedQuickActionsButton, AnimatedQuickActionsButtonProps>(
            new(automationName, onClick, toolTip, isEnabled, iconSize));

    public static Element Undo(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true,
        double iconSize = 24) =>
        Component<AnimatedUndoRedoButton, AnimatedUndoRedoButtonProps>(
            new(UndoRedoIconKind.Undo, automationName, onClick, toolTip, isEnabled, iconSize));

    public static Element Redo(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true,
        double iconSize = 24) =>
        Component<AnimatedUndoRedoButton, AnimatedUndoRedoButtonProps>(
            new(UndoRedoIconKind.Redo, automationName, onClick, toolTip, isEnabled, iconSize));

    public static Element SelectAll(
        bool? checkedState,
        Action<bool?> onChanged,
        string automationName,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedSelectAllToggle, AnimatedSelectAllToggleProps>(
            new(checkedState, onChanged, automationName, toolTip, isEnabled));

    public static Element FileSelection(
        string label,
        string automationName,
        Action onClick,
        bool isEnabled = true) =>
        Component<AnimatedFileSelectionButton, AnimatedFileSelectionButtonProps>(
            new(label, automationName, onClick, isEnabled));

    public static Element TextSelection(
        string label,
        string automationName,
        Action onClick,
        bool isEnabled = true) =>
        Component<AnimatedTextSelectionButton, AnimatedTextSelectionButtonProps>(
            new(label, automationName, onClick, isEnabled));

    public static Element FolderSelection(
        string label,
        string automationName,
        Action onClick,
        bool isEnabled = true) =>
        Component<AnimatedFolderSelectionButton, AnimatedFolderSelectionButtonProps>(
            new(label, automationName, onClick, isEnabled));

    public static Element ClipboardSelection(
        string label,
        string automationName,
        Action onClick,
        bool isEnabled = true) =>
        Component<AnimatedClipboardSelectionButton, AnimatedClipboardSelectionButtonProps>(
            new(label, automationName, onClick, isEnabled));

    public static Element CopyFeedback(
        int successVersion,
        string automationName,
        Action onClick,
        string? successAnnouncement = null,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedCopyButton, AnimatedCopyButtonProps>(
            new(successVersion, automationName, onClick, successAnnouncement, toolTip, isEnabled));

    public static Element Refresh(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true,
        int durationMilliseconds = 500) =>
        Component<AnimatedRefreshButton, AnimatedRefreshButtonProps>(
            new(automationName, onClick, toolTip, isEnabled, durationMilliseconds));

    public static Element Favorites(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true,
        bool isDialogOpen = false) =>
        Component<AnimatedFavoritesButton, AnimatedFavoritesButtonProps>(
            new(automationName, onClick, toolTip, isEnabled, isDialogOpen));

    public static Element AddressTarget(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true,
        bool isDialogOpen = false) =>
        Component<AnimatedAddressTargetButton, AnimatedAddressTargetButtonProps>(
            new(automationName, onClick, toolTip, isEnabled, isDialogOpen));

    public static Element MultipleReceivers(
        bool isChecked,
        Action<bool> onChanged,
        string automationName,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedMultipleReceiversToggle, AnimatedMultipleReceiversToggleProps>(
            new(isChecked, onChanged, automationName, toolTip, isEnabled));

    public static Element TrayService(
        bool isChecked,
        Action<bool> onChanged,
        string automationName,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedTrayToggleButton, AnimatedTrayToggleButtonProps>(
            new(TrayToggleIconKind.Service, isChecked, onChanged, automationName, toolTip, isEnabled));

    public static Element TrayPin(
        bool isChecked,
        Action<bool> onChanged,
        string automationName,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedTrayToggleButton, AnimatedTrayToggleButtonProps>(
            new(TrayToggleIconKind.Pin, isChecked, onChanged, automationName, toolTip, isEnabled));

    public static Element SettingsServiceActions(
        bool isOnline,
        bool isStarting,
        bool isBusy,
        bool canStop,
        Action startOrRestart,
        Action stop,
        string startOrRestartName,
        string stopName) =>
        Component<AnimatedSettingsServiceButtons, AnimatedSettingsServiceButtonsProps>(
            new(
                isOnline,
                isStarting,
                isBusy,
                canStop,
                startOrRestart,
                stop,
                startOrRestartName,
                stopName));

    public static Element Delete(
        string automationName,
        Action onClick,
        string? label = null,
        string? toolTip = null,
        bool isEnabled = true,
        bool subtle = false,
        bool critical = false,
        int shakeVersion = 0,
        double iconSize = 24) =>
        Component<AnimatedDeleteButton, AnimatedDeleteButtonProps>(
            new(
                automationName,
                onClick,
                label,
                toolTip,
                isEnabled,
                subtle,
                critical,
                shakeVersion,
                iconSize));
}
