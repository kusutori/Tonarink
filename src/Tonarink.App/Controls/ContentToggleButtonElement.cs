using Microsoft.UI.Reactor.Wrappers;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Tonarink.Controls;

// Temporary until Reactor exposes Element content for its ToggleButton factory.
[GenerateReactorWrapper(typeof(ToggleButton), RegisterAssembly = false,
    AutoDiscover = false, Include = new[] { "Content", "IsChecked" })]
[WrapContent("Content")]
[WrapControlled("IsChecked", Events = new[] { "Checked", "Unchecked" })]
public partial record ContentToggleButtonElement;
