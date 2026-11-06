using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor.Wrappers;

namespace Tonarink.Controls;

[GenerateReactorWrapper(typeof(SettingsCard), RegisterAssembly = false)]
[WrapElementSlot("HeaderIcon")]
public partial record SettingsCardElement;

[GenerateReactorWrapper(typeof(SettingsExpander), RegisterAssembly = false)]
[WrapElementSlot("HeaderIcon")]
[WrapElementSlot("Content")]
public partial record SettingsExpanderElement;

[GenerateReactorWrapper(typeof(Segmented), RegisterAssembly = false)]
[WrapControlled("SelectedIndex", ChangedEvent = "SelectionChanged")]
public partial record SegmentedElement;

// TokenizingTextBox owns its interspersed editor/token collection. Supply a
// stable ItemsSource instead of treating its Items as a Reactor ItemsHost.
[GenerateReactorWrapper(typeof(TokenizingTextBox), RegisterAssembly = false,
    Exclude = new[] { "Items", "Text" })]
[WrapEvent("TextChanged")]
[WrapEvent("TokenItemAdding")]
[WrapEvent("TokenItemAdded")]
[WrapEvent("TokenItemRemoved")]
public partial record TokenizingTextBoxElement;
