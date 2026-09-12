using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LocalSendDotNet;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.UI.Text;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages;

sealed record NetworkInterfacesPageProps(
    AppSettings Settings,
    Action<Func<AppSettings, AppSettings>> UpdateSettings);

sealed class NetworkInterfacesPage : Component<NetworkInterfacesPageProps>
{
    public override Element Render()
    {
        var t = UseIntl();
        var settings = Props.Settings;
        var currentList = settings.NetworkWhitelist ?? settings.NetworkBlacklist;
        var adapters = ListAdapters();

        var previewCards = adapters.Count == 0
            ? new Element[]
            {
                Caption(t.Message(new("App", "SettingsNetworkInterfacesEmpty")))
                    .Foreground(Theme.SecondaryText),
            }
            : adapters.Select(adapter => AdapterCard(adapter, IsIgnored(adapter, settings)).WithKey(adapter.Id)).ToArray();

        var patternRows = currentList is null
            ? Array.Empty<Element>()
            : currentList.Select((pattern, index) =>
                PatternRow(t, pattern, index, value => ReplacePattern(index, value), () => RemovePattern(index))
                    .WithKey($"pattern-{index}"))
                .ToArray();

        return ScrollView(
            VStack(24,
                Heading(t.Message(new("App", "SettingsNetworkInterfaces")))
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                TextBlock(t.Message(new("App", "SettingsNetworkInterfacesInfo")))
                    .Foreground(Theme.SecondaryText)
                    .TextAlignment(TextAlignment.Center)
                    .TextWrapping(TextWrapping.WrapWholeWords),
                VStack(8,
                    BodyStrong(t.Message(new("App", "SettingsNetworkInterfacesPreview"))),
                    (FlexRow(previewCards) with
                    {
                        ColumnGap = 12,
                        RowGap = 12,
                        Wrap = FlexWrap.Wrap,
                    })),
                Grid(
                    columns: [GridSize.Star(), GridSize.Star()],
                    rows: [GridSize.Auto],
                    CheckBox(
                        (bool?)(settings.NetworkWhitelist is not null),
                        enabled => SetMode(whitelist: true, enable: enabled),
                        t.Message(new("App", "SettingsNetworkInterfacesWhitelist")))
                        .HAlign(HorizontalAlignment.Center)
                        .Grid(column: 0),
                    CheckBox(
                        (bool?)(settings.NetworkBlacklist is not null),
                        enabled => SetMode(whitelist: false, enable: enabled),
                        t.Message(new("App", "SettingsNetworkInterfacesBlacklist")))
                        .HAlign(HorizontalAlignment.Center)
                        .Grid(column: 1)),
                VStack(12, patternRows),
                currentList is null
                    ? null
                    : Grid(
                        columns: [GridSize.Star(), GridSize.Auto],
                        rows: [GridSize.Auto],
                        VStack(0,
                            TextBlock(t.Message(new("App", "SettingsNetworkInterfacesExample"))),
                            TextBlock("123.123.123.123"),
                            TextBlock("123.123.123.*"))
                            .Grid(column: 0),
                        Button(
                            HStack(8,
                                Icon("Add").AccessibilityHidden(),
                                TextBlock(t.Message(new("App", "Add")))),
                            AddPattern)
                            .AutomationName(t.Message(new("App", "Add")))
                            .VAlign(VerticalAlignment.Bottom)
                            .Grid(column: 1)))
                .Padding(AppLayout.PagePadding))
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Landmark(AutomationLandmarkType.Main);

        void SetMode(bool whitelist, bool enable)
        {
            if (!enable)
            {
                Props.UpdateSettings(current => whitelist
                    ? current with { NetworkWhitelist = null }
                    : current with { NetworkBlacklist = null });
                return;
            }

            var seed = currentList is { Count: > 0 } ? [.. currentList] : new[] { "" };
            Props.UpdateSettings(current => whitelist
                ? current with { NetworkWhitelist = seed, NetworkBlacklist = null }
                : current with { NetworkWhitelist = null, NetworkBlacklist = seed });
        }

        void ReplacePattern(int index, string value)
        {
            if (currentList is null)
                return;
            var next = currentList.ToArray();
            next[index] = value;
            WriteList(next);
        }

        void RemovePattern(int index)
        {
            if (currentList is null)
                return;
            if (currentList.Count <= 1)
            {
                WriteList(null);
                return;
            }

            WriteList([.. currentList.Take(index), .. currentList.Skip(index + 1)]);
        }

        void AddPattern()
        {
            var next = currentList is null ? new[] { "" } : [.. currentList, ""];
            WriteList(next);
        }

        void WriteList(IReadOnlyList<string>? next)
        {
            Props.UpdateSettings(current => current.NetworkWhitelist is not null
                ? current with { NetworkWhitelist = next, NetworkBlacklist = null }
                : current with { NetworkBlacklist = next, NetworkWhitelist = null });
        }
    }

    private static Element PatternRow(
        IntlAccessor t,
        string pattern,
        int index,
        Action<string> onChanged,
        Action onRemove) =>
        Grid(
            columns: [GridSize.Star(), GridSize.Auto],
            rows: [GridSize.Auto],
            Component<DeferredTextSetting, DeferredTextSettingProps>(new(
                pattern,
                onChanged,
                t.Message(new("App", "SettingsNetworkInterfacesPattern"), ("index", index + 1))))
                .Grid(column: 0),
            Button(Icon("\uE711"), onRemove)
                .SubtleButton()
                .AutomationName(t.Message(new("App", "SettingsNetworkInterfacesRemovePattern")))
                .ToolTip(t.Message(new("App", "SettingsNetworkInterfacesRemovePattern")))
                .MinWidth(40)
                .MinHeight(40)
                .Grid(column: 1)) with
        {
            ColumnSpacing = 8,
        };

    private static Element AdapterCard(NetworkAdapterPreview adapter, bool ignored)
    {
        var name = TextBlock($"[#{adapter.Index}] {adapter.Name}")
            .TextWrapping(TextWrapping.WrapWholeWords);
        var addresses = adapter.Addresses.Select(address => (Element)TextBlock(address)).ToArray();
        if (ignored)
        {
            name = name.Foreground(Theme.DisabledText).TextDecorations(TextDecorations.Strikethrough);
            addresses = [.. addresses.Select(static address =>
                ((TextBlockElement)address).Foreground(Theme.DisabledText).TextDecorations(TextDecorations.Strikethrough))];
        }

        return Border(
                VStack(4, [name, .. addresses]))
            .Padding(8)
            .MinWidth(160)
            .CornerRadius(8)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1);
    }

    private static bool IsIgnored(NetworkAdapterPreview adapter, AppSettings settings) =>
        NetworkAddressPatterns.IsInterfaceIgnored(adapter.Addresses, settings.NetworkWhitelist, settings.NetworkBlacklist);

    private static IReadOnlyList<NetworkAdapterPreview> ListAdapters()
    {
        var adapters = new List<NetworkAdapterPreview>();
        var index = 1;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var addresses = nic.GetIPProperties().UnicastAddresses
                .Select(static item => item.Address)
                .Where(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .Select(static address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (addresses.Length == 0)
                continue;

            adapters.Add(new NetworkAdapterPreview($"{nic.Id}:{index}", index, nic.Name, addresses));
            index++;
        }

        return adapters;
    }
}

sealed record NetworkAdapterPreview(
    string Id,
    int Index,
    string Name,
    IReadOnlyList<string> Addresses);
