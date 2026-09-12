using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor.Wrappers;

namespace Tonarink.Controls;

[GenerateReactorWrapper(typeof(SettingsCard), RegisterAssembly = false)]
[WrapElementSlot("HeaderIcon")]
public partial record SettingsCardElement;

[GenerateReactorWrapper(typeof(SettingsExpander), RegisterAssembly = false)]
[WrapElementSlot("HeaderIcon")]
public partial record SettingsExpanderElement;

[GenerateReactorWrapper(typeof(Segmented), RegisterAssembly = false)]
[WrapControlled("SelectedIndex", ChangedEvent = "SelectionChanged")]
public partial record SegmentedElement;
