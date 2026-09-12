using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Components;

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

sealed record DeferredPasswordSettingProps(
    string Value,
    Action<string> Commit,
    string AutomationName,
    string? PlaceholderText = null,
    double MinWidth = 0,
    int MaxLength = 32);

sealed class DeferredPasswordSetting : Component<DeferredPasswordSettingProps>
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

        return PasswordBox(draft, value =>
            {
                draftRef.Current = value;
                setDraft(value);
            }, placeholderText: Props.PlaceholderText)
            .OnLostFocus((_, _) =>
            {
                var value = draftRef.Current.Trim();
                if (value.Length > 0 && !string.Equals(value, Props.Value, StringComparison.Ordinal))
                    Props.Commit(value);
            })
            .MaxLength(Props.MaxLength)
            .AutomationName(Props.AutomationName)
            .MinWidth(Props.MinWidth);
    }
}

sealed record DeferredNumberSettingProps(
    double Value,
    Action<double> Commit,
    string AutomationName,
    double Minimum,
    double Maximum,
    double MinWidth = 0);

sealed class DeferredNumberSetting : Component<DeferredNumberSettingProps>
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

        return NumberBox(draft, value =>
            {
                draftRef.Current = value;
                setDraft(value);
            })
            .OnLostFocus((_, _) =>
            {
                if (!draftRef.Current.Equals(Props.Value))
                    Props.Commit(draftRef.Current);
            })
            .Range(Props.Minimum, Props.Maximum)
            .SpinButtons()
            .AutomationName(Props.AutomationName)
            .MinWidth(Props.MinWidth);
    }
}
