using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Tonarink.Controls.SettingsCardElement;

namespace Tonarink.Components.Settings;

sealed record FilePreviewProviderContentProps(
    FilePreviewProvider Provider,
    string Path,
    Action<string> SavePath,
    Action Remove);

sealed class FilePreviewProviderContent : Component<FilePreviewProviderContentProps>
{
    // SettingsExpander applies a SettingsCard style to each realized item.
    // Return the native card element directly; a Component item would mount as
    // a Border identity wrapper. Only its Content can safely host a component.
    public static SettingsCardElement CreateCard(IntlAccessor t, FilePreviewProviderContentProps props) =>
        SettingsCard(
            header: ProviderName(t, props.Provider),
            description: t.Message(new("App", "SettingsFilePreviewToolPathDescription")),
            isClickEnabled: false,
            isActionIconVisible: false,
            content: Component<FilePreviewProviderContent, FilePreviewProviderContentProps>(props)
                .WithKey($"preview-provider-content-{props.Provider}"));

    public override Element Render()
    {
        var t = UseIntl();
        var (path, setPath) = UseState(Props.Path);
        var pathRef = UseRef(path);
        pathRef.Current = path;
        var (feedback, setFeedback) = UseState((Version: 0, Succeeded: true));
        var feedbackVersionRef = UseRef(0);
        UseEffect(() =>
        {
            pathRef.Current = Props.Path;
            setPath(Props.Path);
        }, Props.Path);

        var name = ProviderName(t, Props.Provider);
        var verifyName = t.Message(new("App", "SettingsFilePreviewVerifyPath"));
        var removeName = t.Message(new("App", "RemoveItem"), ("item", name));
        var placeholder = t.Message(new("App", "SettingsFilePreviewAutoPathPlaceholder"));

        return (Grid(
                    columns: [GridSize.Star(), GridSize.Auto, GridSize.Auto],
                    rows: [GridSize.Auto],
                    TextBox(path, value =>
                        {
                            pathRef.Current = value;
                            setPath(value);
                        }, placeholder)
                        .AutomationName(t.Message(new("App", "SettingsFilePreviewToolPath"), ("tool", name)))
                        .Width(300)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 0),
                    AnimatedButtons.VerifyFeedback(feedback.Version, verifyName, ValidateAndSave,
                            feedback.Version == 0 ? null : t.Message(new("App", feedback.Succeeded
                                ? "SettingsFilePreviewPathSaved" : "SettingsFilePreviewPathMissing")),
                            isEnabled: !string.IsNullOrWhiteSpace(FilePreviewSettings.NormalizePath(path)),
                            succeeded: feedback.Succeeded)
                        .Size(40, 40)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 1),
                    // Delete's 48px canvas includes 4px of top/bottom whitespace.
                    // At 24px its visible mark is 20px, matching the FontIcon.
                    // Same hover-to-open lid as the send card. Removal is immediate,
                    // so this entry never requests the optional shake feedback.
                    AnimatedButtons.Delete(removeName, Props.Remove, subtle: true,
                            shakeVersion: 0, iconSize: 24, buttonSize: 40)
                        .Size(40, 40)
                        .VAlign(VerticalAlignment.Center)
                        .Grid(column: 2)) with { ColumnSpacing = 8 })
                .Width(396)
                .HAlign(HorizontalAlignment.Right)
                .VAlign(VerticalAlignment.Center);

        void ValidateAndSave()
        {
            if (FilePreviewSettings.NormalizePath(pathRef.Current).Length == 0)
                return;
            if (!FilePreviewSettings.TryValidateOverride(pathRef.Current, out var normalized))
            {
                setFeedback((++feedbackVersionRef.Current, false));
                return;
            }

            Props.SavePath(normalized);
            pathRef.Current = normalized;
            setPath(normalized);
            setFeedback((++feedbackVersionRef.Current, true));
        }
    }

    private static string ProviderName(IntlAccessor t, FilePreviewProvider provider) =>
        t.Message(new("App", provider == FilePreviewProvider.QuickLook
            ? "SettingsFilePreviewProviderQuickLook" : "SettingsFilePreviewProviderPowerToysPeek"));
}
