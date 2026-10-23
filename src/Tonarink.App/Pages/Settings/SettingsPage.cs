using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages.Settings;

sealed class SettingsPage : Component<SettingsPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var (_, refreshCachedPage) = UseReducer(0);
        // Refresh only once NavigationHost has committed the restored page.
        // Rendering in the destination guard can re-enter cache restoration before
        // CurrentChildControl/LastRenderedRoute are committed, mounting a second page.
        UseNavigationLifecycle(onNavigatedTo: _ =>
            refreshCachedPage(value => value + 1));

        var (statusMessage, setStatusMessage) = UseState<string?>(null);
        var floatingInfo = UseContext(FloatingInfoBarHost.Slot);
        floatingInfo.Owner = AppRoute.Settings;
        floatingInfo.Build = statusMessage is null
                             && Props.Runtime.Error is null
                             && Props.Runtime.DiscoveryWarning is null
            ? null
            : () => VStack(4,
                statusMessage is null
                    ? null
                    : Component<FloatingInfoBarItem, FloatingInfoBarItemProps>(new(
                            (InfoBar(t.Message(new("App", "SettingsTitle")), statusMessage) with
                            {
                                IsOpen = true,
                                IsClosable = true,
                                OnClosed = () => setStatusMessage(null),
                            }).Severity(InfoBarSeverity.Error)))
                        .WithKey("settings-status"),
                Props.Runtime.Error is null
                    ? null
                    : Component<FloatingInfoBarItem, FloatingInfoBarItemProps>(new(
                            (InfoBar(t.Message(new("App", "NetworkStartFailed")), Props.Runtime.Error) with
                            {
                                IsOpen = true,
                                IsClosable = false,
                            }).Severity(InfoBarSeverity.Error)))
                        .WithKey("settings-runtime-error"),
                Props.Runtime.DiscoveryWarning is null
                    ? null
                    : Component<FloatingInfoBarItem, FloatingInfoBarItemProps>(new(
                            (InfoBar(
                                    t.Message(new("App", "NodeDiscoveryLimited")),
                                    Props.Runtime.DiscoveryWarning) with
                            {
                                IsOpen = true,
                                IsClosable = false,
                            }).Severity(InfoBarSeverity.Warning)))
                        .WithKey("settings-discovery-warning"));
        UseEffect(
            () => floatingInfo.Invalidate(),
            statusMessage,
            Props.Runtime.Error,
            Props.Runtime.DiscoveryWarning);
        var savedInputs = SettingsInputDraft.FromSettings(Props.Settings);
        var (draft, setDraft) = UseState(savedInputs);
        var draftRef = UseRef(draft);
        draftRef.Current = draft;
        var savedInputsRef = UseRef(savedInputs);
        UseEffect(() =>
        {
            var rebased = draftRef.Current.Rebase(savedInputsRef.Current, savedInputs);
            savedInputsRef.Current = savedInputs;
            if (rebased != draftRef.Current)
                UpdateDraft(_ => rebased);
        }, savedInputs);
        return ScrollView(
                VStack(24,
                        Component<GeneralSettingsSection, GeneralSettingsSectionProps>(new(
                            Props.Settings, Props.UpdateSettings, setStatusMessage)),
                        Component<ReceiveSettingsSection, ReceiveSettingsSectionProps>(new(
                            Props.Settings, draft, Props.UpdateSettings, UpdateDraft, setStatusMessage)),
                        Component<SendSettingsSection, SendSettingsSectionProps>(new(
                            Props.Settings, Props.UpdateSettings)),
                        Component<NetworkSettingsSection, NetworkSettingsSectionProps>(new(
                            Props.Settings, Props.Runtime, draft, Props.UpdateSettings, UpdateDraft,
                            ApplyAndRestart, Props.StopServer)),
                        Component<ExperimentalSettingsSection, ExperimentalSettingsSectionProps>(new(
                            Props.Settings, Props.UpdateSettings, setStatusMessage)),
                        Component<AboutSettingsSection>())
                    .Padding(AppLayout.PagePadding))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .AutomationName(t.Message(new("App", "SettingsTitle")))
            .AutomationId("Settings.Page")
            .Landmark(AutomationLandmarkType.Main);

        void UpdateDraft(Func<SettingsInputDraft, SettingsInputDraft> update)
        {
            var next = update(draftRef.Current);
            draftRef.Current = next;
            setDraft(next);
        }

        void ApplyAndRestart()
        {
            var pending = draftRef.Current;
            if (!pending.TryValidate(Props.Settings.ReceivePinEnabled, out var errorKey))
            {
                setStatusMessage(t.Message(new("App", errorKey!)));
                return;
            }

            try
            {
                Props.ApplyAndRestartServer(pending.ApplyTo);
                UpdateDraft(_ => SettingsInputDraft.FromSettings(pending.ApplyTo(Props.Settings)));
                setStatusMessage(null);
            }
            catch (Exception exception)
            {
                AppDiagnostics.Report("Could not apply server settings", exception);
                setStatusMessage(t.Message(new("App", "SettingsApplyFailed"), ("error", exception.Message)));
            }
        }
    }
}
