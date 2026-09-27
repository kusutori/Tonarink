using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Dialogs;

sealed record RenameItemDialogProps(
    ElementTheme Theme,
    bool IsOpen,
    string FileName,
    Action<string> SetFileName,
    Action<string> Confirm,
    Action Close);

/// <summary>Shared editor for the protocol filename used by incoming and outgoing transfers.</summary>
sealed class RenameItemDialog : Component<RenameItemDialogProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var validName = IsValidFileName(Props.FileName);
        return (ContentDialog(
                t.Message(new("App", "Rename")),
                VStack(6,
                    TextBox(Props.FileName, Props.SetFileName)
                        .Header(t.Message(new("App", "Name")))
                        .AutomationName(t.Message(new("App", "Name")))
                        .HelpText(validName || string.IsNullOrWhiteSpace(Props.FileName)
                            ? string.Empty
                            : t.Message(new("App", "InvalidFileName")))
                        .Required(),
                    validName || string.IsNullOrWhiteSpace(Props.FileName)
                        ? null
                        : Caption(t.Message(new("App", "InvalidFileName")))
                            .Foreground(Theme.SystemCritical)
                            .LiveRegion(AutomationLiveSetting.Assertive)),
                primaryButtonText: t.Message(new("App", "Save"))) with
        {
            IsOpen = Props.IsOpen,
            SecondaryButtonText = t.Message(new("App", "Cancel")),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = validName,
            OnClosed = result =>
            {
                if (result == ContentDialogResult.Primary && validName)
                    Props.Confirm(Props.FileName.Trim());

                Props.Close();
            },
        }).Themed(Props.Theme);
    }

    public static bool IsValidFileName(string value)
    {
        var name = value.Trim();
        return name.Length > 0
               && !name.EndsWith('.')
               && !name.EndsWith(' ')
               && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
               && !name.Contains(':', StringComparison.Ordinal);
    }
}
