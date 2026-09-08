using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

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
