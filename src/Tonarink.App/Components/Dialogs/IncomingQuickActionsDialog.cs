using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using static Microsoft.UI.Reactor.Factories;
using Tonarink.Components.Animations;
using static Tonarink.Controls.SegmentedElement;

namespace Tonarink.Components.Dialogs;

sealed record IncomingQuickActionFile(string Id, string FileName);

sealed record IncomingQuickActionsDialogProps(
    IReadOnlyList<IncomingQuickActionFile> Files,
    ElementTheme Theme,
    bool IsOpen,
    Action Close,
    Action<IReadOnlyDictionary<string, string>> Apply);

sealed class IncomingQuickActionsDialog : Component<IncomingQuickActionsDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (mode, setMode) = UseState(0);
        var (prefix, setPrefix) = UseState("");
        var (padZero, setPadZero) = UseState(false);
        var (sortFirst, setSortFirst) = UseState(false);
        var (randomExample, setRandomExample) = UseState(Guid.NewGuid().ToString());
        var modes = UseMemo(() => new object[]
        {
            new SegmentedItem { Content = t.Message(new("App", "QuickActionsCounter")) },
            new SegmentedItem { Content = t.Message(new("App", "QuickActionsRandom")) },
        }, t.Locale);

        UseEffect(() =>
        {
            if (!Props.IsOpen)
                return;

            setMode(0);
            setPrefix("");
            setPadZero(false);
            setSortFirst(false);
            setRandomExample(Guid.NewGuid().ToString());
        }, Props.IsOpen);

        var extension = Props.Files.Count > 0
            ? Path.GetExtension(Props.Files[0].FileName)
            : ".jpg";
        if (string.IsNullOrEmpty(extension))
            extension = ".jpg";
        var prefixValid = IsValidPrefix(prefix);
        var exampleName = mode == 0
            ? $"{prefix}{(padZero ? "04" : "4")}{extension}"
            : $"{randomExample}{extension}";

        Element body = VStack(12,
                Segmented(
                        selectedIndex: mode,
                        onSelectedIndexChanged: setMode,
                        items: modes)
                    .HAlign(HorizontalAlignment.Stretch),
                Component<SegmentedContentSwitcher, SegmentedContentSwitcherProps>(
                        new(
                            mode,
                            CounterModeContent(),
                            Caption(t.Message(new("App", "QuickActionsExample"), ("name", exampleName)))
                                .Foreground(Theme.SecondaryText)))
                    .MinHeight(160)
                    .HAlign(HorizontalAlignment.Stretch))
            .MinWidth(360)
            .HAlign(HorizontalAlignment.Stretch);

        return (ContentDialog(
                t.Message(new("App", "QuickActionsTitle")),
                body,
                primaryButtonText: t.Message(new("App", "Confirm"))) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = Props.Files.Count > 0 && (mode == 1 || prefixValid),
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary && Props.Files.Count > 0 && (mode == 1 || prefixValid))
                    Props.Apply(BuildNames());
                Props.Close();
            },
        }).Themed(Props.Theme);

        Element CounterModeContent() =>
            VStack(8,
                TextBox(prefix, setPrefix)
                    .Header(t.Message(new("App", "QuickActionsPrefix")))
                    .AutomationName(t.Message(new("App", "QuickActionsPrefix")))
                    .HelpText(prefixValid
                        ? string.Empty
                        : t.Message(new("App", "QuickActionsInvalidPrefix")))
                    .Required(),
                prefixValid
                    ? null
                    : Caption(t.Message(new("App", "QuickActionsInvalidPrefix")))
                        .Foreground(Theme.SystemCritical)
                        .LiveRegion(AutomationLiveSetting.Assertive),
                CheckBox(
                    (bool?)padZero,
                    value => setPadZero(value),
                    t.Message(new("App", "QuickActionsPadZero"))),
                CheckBox(
                    (bool?)sortFirst,
                    value => setSortFirst(value),
                    t.Message(new("App", "QuickActionsSortBeforeCount"))),
                Caption(t.Message(new("App", "QuickActionsExample"), ("name", exampleName)))
                    .Foreground(Theme.SecondaryText));

        IReadOnlyDictionary<string, string> BuildNames() => mode switch
        {
            1 => Props.Files.ToDictionary(
                file => file.Id,
                file => KeepExtension(file.FileName, Guid.NewGuid().ToString()),
                StringComparer.Ordinal),
            _ => BuildCounterNames(),
        };

        IReadOnlyDictionary<string, string> BuildCounterNames()
        {
            var files = Props.Files.ToList();
            if (sortFirst)
                files.Sort(static (left, right) =>
                    string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase));

            var width = files.Count.ToString(CultureInfo.InvariantCulture).Length;
            var names = new Dictionary<string, string>(files.Count, StringComparer.Ordinal);
            for (var index = 0; index < files.Count; index++)
            {
                var number = (index + 1).ToString(CultureInfo.InvariantCulture);
                if (padZero)
                    number = number.PadLeft(width, '0');
                names[files[index].Id] = KeepExtension(files[index].FileName, prefix + number);
            }

            return names;
        }
    }

    private static string KeepExtension(string originalName, string newBase) =>
        newBase + Path.GetExtension(originalName);

    private static bool IsValidPrefix(string value) =>
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !value.Contains(':', StringComparison.Ordinal);
}
