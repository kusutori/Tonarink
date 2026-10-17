# Discussion draft: Declarative data templates for generated Reactor wrappers

本地记录，尚未提交到 GitHub。记录日期：2026-10-17

这是一个开放式设计讨论，不是 PR 承诺或完整 API 规范。原生代码模板已由用户在 Tonarink 中运行确认；下述 DSL 尚未实现。SDK 仍处于实验通道，本文不建议 Reactor 为此立即升级其默认依赖。

## Suggested issue title

Discussion: Declarative data templates on top of WinUI's code-based DataTemplate factory

## Background

After reading the [Windows App SDK release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0#version-25-experimental-254-experimental), the [WinUI Gallery example](https://github.com/microsoft/WinUI-Gallery/pull/2258), and [Reactor PR #1125](https://github.com/microsoft/microsoft-ui-reactor/pull/1125), I tried the new `DataTemplate(DataTemplateElementFactory)` constructor in Tonarink.

I understand why #1125 remains a draft: the API is currently experimental, and making an application opt into that channel is different from making it a framework-wide dependency. This discussion is about a possible declarative API above that native capability, not a request to merge #1125 or change Reactor's default SDK now. The time before stabilization could be useful for discussing and prototyping the design.

My concrete use case is the Windows Community Toolkit `TokenizingTextBox`, integrated through a generated Reactor wrapper. A history filter offers all 18 categories from the application's existing `FileTypeGlyphs` helper. Both selected tokens and suggestions should show an icon and a localized name.

Relevant application code:

- [Picker and native template](../../src/Tonarink.App/Pages/History/HistoryFileTypePicker.cs)
- [Generated-wrapper declaration](../../src/Tonarink.App/Controls/ToolkitControls.cs)
- [Bindable item model](../../src/Tonarink.App/Controls/FileTypeToken.cs)

These are local source links for preparing the discussion; replace them with commit-pinned GitHub links before publishing, once the relevant application revision is committed and pushed.

## What I tried

### 1. A template parsed from a XAML string

The initial implementation used the familiar native escape hatch:

```csharp
var template = (DataTemplate)XamlReader.Load("""
    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
        <StackPanel Orientation="Horizontal" Spacing="6">
            <FontIcon Glyph="{Binding Glyph}" FontFamily="Segoe Fluent Icons" FontSize="16" />
            <TextBlock Text="{Binding Label}" VerticalAlignment="Center" />
        </StackPanel>
    </DataTemplate>
    """);
```

This expressed the desired layout, but brought handwritten markup and runtime XAML parsing into an otherwise C# Reactor application. Removing the template kept the control functional, but lost the icons.

### 2. A native template built entirely in C#

I opted the application into `Microsoft.WindowsAppSDK 2.5.4-experimental` and replaced the markup with the delegate constructor:

```csharp
var template = new DataTemplate(static () =>
{
    var icon = new FontIcon
    {
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        FontSize = 16,
    };
    icon.SetBinding(FontIcon.GlyphProperty, new Binding
    {
        Path = new PropertyPath(nameof(FileTypeToken.Glyph)),
    });

    var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
    label.SetBinding(TextBlock.TextProperty, new Binding
    {
        Path = new PropertyPath(nameof(FileTypeToken.Label)),
    });

    var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
    content.Children.Add(icon);
    content.Children.Add(label);
    return content;
});
```

The generated wrapper currently does not expose these `DataTemplate`-valued properties, so the application assigns the cached template through `.Set(...)`:

```csharp
.Set(control =>
{
    if (!ReferenceEquals(control.TokenItemTemplate, template))
        control.TokenItemTemplate = template;
    if (!ReferenceEquals(control.SuggestedItemTemplate, template))
        control.SuggestedItemTemplate = template;
})
```

The template instance is kept stable across ordinary renders. Its factory creates a new visual subtree for each realization; it does not share one `UIElement` instance between tokens and suggestions. The item model has generated bindable metadata for the string-path bindings.

The x64 Debug build succeeded, and the application user confirmed that this restored the icon-and-text presentation at runtime without handwritten XAML. This is evidence that the native API works for this scenario, not evidence that a general Reactor template adapter is already implemented or AOT-validated.

The remaining application code is still imperative: native control constructors, `Children.Add`, bindings, and explicit property assignment.

## A possible declarative direction

Could Reactor expose a reusable, strongly typed data-template abstraction that accepts a Reactor `viewBuilder`, and allow generated wrappers to consume it?

For example, the following is **illustrative syntax only**, not an existing API:

```csharp
var template = UseMemo(() =>
    Templates.Data<FileTypeToken>(item =>
        HStack(6,
            Icon(item.Glyph),
            TextBlock(item.Label))));

return TokenizingTextBox(
    itemsSource: state.Items,
    suggestedItemsSource: state.Suggestions,
    tokenItemTemplate: template,
    suggestedItemTemplate: template);
```

The intended division of responsibility would be:

- The application declares the item's view using ordinary Reactor elements or components.
- A template adapter handles the native factory, data delivery, reconciliation, and cleanup.
- Generated wrappers expose appropriate template properties, without knowing the template's internal layout.

The same idea could potentially apply to other `DataTemplate` slots such as item, content, and header templates. It would not automatically solve `ControlTemplate` or `ItemsPanelTemplate`; those are separate capabilities.

The simple example has no hooks inside the item builder. If stateful content is supported, normal Reactor components would be a clearer boundary for their hooks and lifetime.

## Questions worth discussing before implementation

1. **Public API and ownership.** Should this return a native `DataTemplate`, a Reactor-owned template description, or something else? Should wrappers accept it through native template properties, or through a dedicated generated template slot?
2. **Data delivery and updates.** The native factory takes no item parameter. How should the adapter handle `DataContext` assignment/replacement, a parent render changing captured values, and mutations of an existing data object? `DataContextChanged` alone does not describe all those update sources.
3. **Identity and recycling.** When a container moves from item A to item B, which state belongs to the visual container and which belongs to the data item? How should keys participate? Unconditionally remounting on each update would discard focus, drafts, animations, and component state.
4. **Context.** How can template components access the intended theme, localization, providers, routing, and error boundaries rather than silently becoming unrelated Reactor roots?
5. **Teardown.** A popup closing or a control being temporarily unloaded is not necessarily permanent destruction. Where should effects, subscriptions, and native/managed references be released, and how can double cleanup and leaked hosts be avoided?
6. **Cost and compatibility.** Can an existing reconciliation path be reused without a heavyweight independent root per item? How should template instances and builder updates be cached? Is this best explored behind an experimental opt-in while the native API stabilizes?

I am not proposing a final DSL or a large implementation upfront. Would a reusable declarative data-template adapter be a useful direction beyond #1125's internal migration? If so, which layer and lifecycle contract would maintainers prefer? A narrow TokenizingTextBox prototype could help explore the design before expanding the scope.

## Local follow-up: flashing while adding tokens

After the native template was confirmed to work, the user reported that adding a token made the editor/suggestion area flash. A whole-control remount was suspected, but has not been demonstrated. This report should not be presented as an upstream Reactor or WinUI bug without further evidence.

Two avoidable update paths were identified in the application and adjusted:

- Candidate refresh previously called `Suggestions.Clear()` and repopulated it. That sends a collection `Reset` and creates an artificial empty list. It now applies incremental inserts, moves, and removals, retaining surviving objects and avoiding a transient empty list when the result is nonempty.
- Native token submission restores editor focus before raising `TokenItemAdded`. An application focus callback queued before that event could reopen suggestions after submission closed them. Selection changes now invalidate those stale queued opens.

The existing notifying item/suggestion sources and template instance remain stable. These are application-side corrections, not modifications to the template factory or Reactor itself. The x64 Debug build is the verification scope for this correction; its effect on the reported flashing still needs the user's runtime confirmation. No new test suite or AOT publish was run for this follow-up.
