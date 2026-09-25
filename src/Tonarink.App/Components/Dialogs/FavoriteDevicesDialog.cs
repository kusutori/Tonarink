using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record FavoriteDevicesDialogProps(
    IReadOnlyDictionary<string, FavoriteDevice> Devices,
    string? PreferredFingerprint,
    ElementTheme Theme,
    bool IsOpen,
    Action<FavoriteDevice> Send,
    Action Hide,
    Action Create,
    Action<FavoriteDevice> Edit,
    Action<FavoriteDevice> Delete,
    Action Close);

/// <summary>Displays saved devices and routes create, edit, and delete actions after the dialog closes.</summary>
sealed class FavoriteDevicesDialog : Component<FavoriteDevicesDialogProps>
{
    private enum DialogAction
    {
        Edit,
        Delete,
    }

    private sealed record PendingAction(DialogAction Action, FavoriteDevice Device);

    public override Element Render()
    {
        var t = UseIntl();
        var pendingActionRef = UseRef<PendingAction?>();
        var entries = Props.Devices.Values
            .OrderBy(favorite => string.Equals(
                favorite.Fingerprint,
                Props.PreferredFingerprint,
                StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(static favorite => favorite.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        Element body = entries switch
        {
            [] => TextBlock(t.Message(new("App", "FavoritesEmpty")))
                .Foreground(Theme.SecondaryText)
                .HAlign(HorizontalAlignment.Center)
                .Margin(0, 28),
            _ => VStack(8, [.. entries.Select(FavoriteRow)]),
        };

        return (ContentDialog(
                t.Message(new("App", "FavoritesTitle")),
                ScrollView(body)
                    .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                    .MaxHeight(420)
                    .MinWidth(420),
                primaryButtonText: t.Message(new("App", "NewFavorite"))) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            DefaultButton = ContentDialogButton.None,
            OnClosed = result =>
            {
                var pending = pendingActionRef.Current;
                pendingActionRef.Current = null;

                if (result == ContentDialogResult.Primary)
                {
                    Props.Hide();
                    Props.Create();
                    return;
                }

                switch (pending)
                {
                    case { Action: DialogAction.Edit, Device: var device }:
                        Props.Edit(device);
                        return;
                    case { Action: DialogAction.Delete, Device: var device }:
                        Props.Delete(device);
                        return;
                }

                Props.Close();
            },
        }).Themed(Props.Theme);

        Element FavoriteRow(FavoriteDevice favorite) =>
            Card(
                Grid(
                        columns: [GridSize.Star(), GridSize.Auto, GridSize.Auto],
                        rows: [GridSize.Auto],
                        Button(
                                VStack(2,
                                    BodyStrong(favorite.Name)
                                        .TextTrimming(TextTrimming.CharacterEllipsis),
                                    TextBlock(favorite.Address)
                                        .Foreground(Theme.SecondaryText)
                                        .TextTrimming(TextTrimming.CharacterEllipsis)),
                                () => Props.Send(favorite))
                            .Padding(12, 8)
                            .HAlign(HorizontalAlignment.Stretch)
                            .HorizontalContentAlignment(HorizontalAlignment.Left)
                            .AutomationName(t.Message(
                                new("App", "SendToFavorite"),
                                ("device", favorite.Name)))
                            .GhostButton()
                            .Grid(column: 0),
                        Button(Icon("\uE70F").AccessibilityHidden(), () => Queue(DialogAction.Edit, favorite))
                            .AutomationName(t.Message(
                                new("App", "EditFavoriteDevice"),
                                ("device", favorite.Name)))
                            .ToolTip(t.Message(new("App", "EditFavorite")))
                            .MinWidth(40)
                            .MinHeight(40)
                            .SubtleButton()
                            .Grid(column: 1),
                        Button(Icon("\uE74D").AccessibilityHidden(), () => Queue(DialogAction.Delete, favorite))
                            .AutomationName(t.Message(
                                new("App", "RemoveFavoriteDevice"),
                                ("device", favorite.Name)))
                            .ToolTip(t.Message(new("App", "Delete")))
                            .MinWidth(40)
                            .MinHeight(40)
                            .SubtleButton()
                            .Grid(column: 2))
                    .HAlign(HorizontalAlignment.Stretch));

        void Queue(DialogAction action, FavoriteDevice favorite)
        {
            pendingActionRef.Current = new(action, favorite);
            Props.Hide();
        }
    }
}
