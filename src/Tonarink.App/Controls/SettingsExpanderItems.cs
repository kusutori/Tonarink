using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;

namespace Tonarink.Controls;

public partial record SettingsExpanderElement
{
    private sealed record MountedItem(Element? Element, object Value);
    private sealed class MountedItems
    {
        public List<MountedItem> Items { get; set; } = [];
    }

    private static readonly ConditionalWeakTable<SettingsExpander, MountedItems> LiveItems = new();

    private static void ReconcileItems(MountContext context, SettingsExpander control, IReadOnlyList<object> items)
    {
        if (!LiveItems.TryGetValue(control, out var state))
        {
            state = new MountedItems();
            LiveItems.Add(control, state);
            // Toolkit's default Items is a List<object>. Its ItemsRepeater cannot
            // observe insert/remove operations on that list after template creation.
            control.Items = new ObservableCollection<object>();
        }
        var previous = state.Items;
        var used = new HashSet<MountedItem>(ReferenceEqualityComparer.Instance);
        var next = new List<MountedItem>(items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is Element element)
            {
                // Dynamic provider cards are keyed; static cards keep their slot.
                // Never lend a removed provider's component state to another provider.
                var existing = element.Key is { } key
                    ? previous.FirstOrDefault(item => !used.Contains(item) && item.Element?.Key == key)
                    : index < previous.Count && previous[index].Element is { Key: null }
                        && !used.Contains(previous[index]) ? previous[index] : null;
                if (existing is not null)
                    used.Add(existing);

                var mounted = context.ReconcileChild(existing?.Element, element, existing?.Value as UIElement);
                if (mounted is not null)
                    next.Add(new(element, mounted));
            }
            else if (items[index] is { } value)
            {
                var existing = previous.FirstOrDefault(item => !used.Contains(item)
                    && item.Element is null && ReferenceEquals(item.Value, value));
                if (existing is not null)
                    used.Add(existing);
                next.Add(new(null, value));
            }
        }

        // Property-only updates don't touch Items at all, preserving realization,
        // input focus, pointer handlers and the active animation's native player.
        for (var index = control.Items.Count - 1; index >= 0; index--)
        {
            if (!next.Any(item => ReferenceEquals(item.Value, control.Items[index])))
                control.Items.RemoveAt(index);
        }
        foreach (var removed in previous.Where(item => !used.Contains(item)))
        {
            if (removed.Element is not null && removed.Value is UIElement child)
                context.ReconcileChild(removed.Element, null, child);
        }
        for (var index = 0; index < next.Count; index++)
        {
            var value = next[index].Value;
            if (index < control.Items.Count && ReferenceEquals(control.Items[index], value))
                continue;
            var oldIndex = control.Items.IndexOf(value);
            if (oldIndex >= 0)
                control.Items.RemoveAt(oldIndex);
            control.Items.Insert(index, value);
        }
        state.Items = next;
    }

    private static void UnmountExpander(in UnmountContext context, SettingsExpander control)
    {
        if (LiveItems.TryGetValue(control, out var state))
        {
            foreach (var item in state.Items)
            {
                if (item.Element is not null && item.Value is UIElement child)
                    context.Reconciler.UnmountChild(child);
            }
            LiveItems.Remove(control);
        }
        control.Items.Clear();
        if (control.Content is UIElement content)
            context.Reconciler.UnmountChild(content);
        if (control.HeaderIcon is UIElement icon)
            context.Reconciler.UnmountChild(icon);
    }
}
