using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Reactor.Wrappers;

namespace Tonarink.Controls;

[GenerateReactorWrapper(typeof(SettingsCard), RegisterAssembly = false)]
[WrapElementSlot("HeaderIcon")]
public partial record SettingsCardElement;

[GenerateReactorWrapper(typeof(SettingsExpander), RegisterAssembly = false)]
[WrapElementSlot("HeaderIcon")]
[WrapManual("Content")]
public partial record SettingsExpanderElement
{
    public Element? Content { get; init; }

    public static SettingsExpanderElement SettingsExpander(
        Element? content,
        Element? headerIcon = null,
        params object[] items) =>
        SettingsExpander(headerIcon: headerIcon, items: items) with { Content = content };

    // Items is the primary child collection; the header content needs its own
    // reconciled slot so updates preserve the live control and its visual state.
    private static partial ControlDescriptor<SettingsExpanderElement, SettingsExpander> Customize(
        ControlDescriptor<SettingsExpanderElement, SettingsExpander> d)
    {
        // The generated flat ItemsHost skips/rebuilds whole item collections.
        // Stateful cards need native identity and an explicit reconcile on each render.
        d.Children = new Imperative<SettingsExpanderElement, SettingsExpander>(
            static (context, _, element, control) => ReconcileItems(context, control, element.Items));
        return d.ImperativeBridged(
            mount: static (context, control, element) =>
            {
                if (element.Content is not null)
                    control.Content = context.MountChild(element.Content)!;
            },
            update: static (context, control, oldElement, newElement) =>
            {
                if (oldElement.Content is null && newElement.Content is null)
                    return;

                var existing = control.Content as UIElement;
                var next = context.ReconcileChild(oldElement.Content, newElement.Content, existing);
                if (!ReferenceEquals(existing, next))
                    control.Content = next!;
            }).WithUnmount(UnmountExpander);
    }
}

[GenerateReactorWrapper(typeof(Segmented), RegisterAssembly = false)]
[WrapControlled("SelectedIndex", ChangedEvent = "SelectionChanged")]
public partial record SegmentedElement;
