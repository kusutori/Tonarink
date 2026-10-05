using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components.Settings;

// Network interface patterns still commit independently on focus loss.
sealed record DeferredTextSettingProps(
    string Value,
    Action<string> Commit,
    string AutomationName,
    string? PlaceholderText = null,
    double MinWidth = 0);

sealed class DeferredTextSetting : Component<DeferredTextSettingProps>
{
    public override Element Render()
    {
        var (draft, setDraft) = UseState(Props.Value);
        var draftRef = UseRef(draft);
        draftRef.Current = draft;

        UseEffect(() =>
        {
            draftRef.Current = Props.Value;
            setDraft(Props.Value);
        }, Props.Value);

        return TextBox(draft, value =>
            {
                draftRef.Current = value;
                setDraft(value);
            }, Props.PlaceholderText)
            .OnLostFocus((_, _) =>
            {
                if (!string.Equals(draftRef.Current, Props.Value, StringComparison.Ordinal))
                    Props.Commit(draftRef.Current);
            })
            .AutomationName(Props.AutomationName)
            .MinWidth(Props.MinWidth);
    }
}
