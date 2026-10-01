using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Animations;

public static class AnimatedButtons
{
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
        bool isEnabled = true) =>
        Component<AnimatedFavoritesButton, AnimatedFavoritesButtonProps>(
            new(automationName, onClick, toolTip, isEnabled));

    public static Element AddressTarget(
        string automationName,
        Action onClick,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedAddressTargetButton, AnimatedAddressTargetButtonProps>(
            new(automationName, onClick, toolTip, isEnabled));

    public static Element MultipleReceivers(
        bool isChecked,
        Action<bool> onChanged,
        string automationName,
        string? toolTip = null,
        bool isEnabled = true) =>
        Component<AnimatedMultipleReceiversToggle, AnimatedMultipleReceiversToggleProps>(
            new(isChecked, onChanged, automationName, toolTip, isEnabled));

    public static Element Delete(
        string automationName,
        Action onClick,
        string? label = null,
        string? toolTip = null,
        bool isEnabled = true,
        bool subtle = false,
        bool critical = false) =>
        Component<AnimatedDeleteButton, AnimatedDeleteButtonProps>(
            new(automationName, onClick, label, toolTip, isEnabled, subtle, critical));
}
