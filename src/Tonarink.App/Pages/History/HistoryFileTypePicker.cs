using System.Collections.Immutable;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages.History;

sealed record HistoryFileTypePickerProps(
    ImmutableHashSet<FileTypeGlyphKind> Selected,
    Action<ImmutableHashSet<FileTypeGlyphKind>> Changed);

sealed class HistoryFileTypePicker : Component<HistoryFileTypePickerProps>
{
    private const int CommonTypeCount = 8;
    private const int RevealBatchSize = 4;

    public override Element Render()
    {
        var t = UseIntl();
        var reduceMotion = UseReducedMotion();
        var reveal = UseMemo(static () => new HistoryTypeReveal());
        var (visibleCount, setVisibleCount) = UseState(CommonTypeCount);
        var (phase, setPhase) = UseState(RevealPhase.Idle);
        UseEffect(() =>
        {
            if (reduceMotion) reveal.Complete();
        }, reduceMotion);
        var types = HistoryFilter.AvailableFileTypes;
        var hidden = types.Select((kind, index) => (Kind: kind, Index: index))
            .Where(item => item.Index >= visibleCount && !Props.Selected.Contains(item.Kind))
            .ToArray();
        var buttons = types
            .Where((kind, index) => index < visibleCount || Props.Selected.Contains(kind))
            .Select(kind =>
            {
                var label = t.Message(new("App", LabelKey(kind)));
                return Controls.ContentToggleButtonElement.ToggleButton(
                        content: HStack(6,
                            Icon(FontIcon(FileTypeGlyphs.ForKind(kind), fontSize: 16)).AccessibilityHidden(),
                            TextBlock(label)),
                        isChecked: Props.Selected.Contains(kind),
                        onIsCheckedChanged: selected => Props.Changed(selected == true
                            ? Props.Selected.Add(kind)
                            : Props.Selected.Remove(kind)))
                    .Padding(10, 6)
                    .AutomationName(label)
                    .AutomationId($"History.Filter.Type.{kind}")
                    .OnMountAdd(element => reveal.AttachItem(kind, element))
                    .OnUnmountAdd(element => reveal.DetachItem(kind, element))
                    .WithKey(kind.ToString());
            })
            .Cast<Element?>()
            .ToList();

        if (types.Length > CommonTypeCount)
        {
            buttons.Add(Component<HistoryTypeExpandButton, HistoryTypeExpandButtonProps>(
                    new(phase == RevealPhase.Collapsing || hidden.Length == 0 && phase == RevealPhase.Idle,
                        phase != RevealPhase.Idle, Advance, reveal))
                .WithKey("more-types"));
        }

        return (FlexRow([.. buttons]) with
            {
                Wrap = FlexWrap.Wrap,
                ColumnGap = HistoryTypeReveal.ItemGap,
                RowGap = HistoryTypeReveal.ItemGap,
            })
            .AutomationName(t.Message(new("App", "HistoryFilterFileType")))
            .HelpText(t.Message(new("App", "HistoryFilterTypeAll")))
            .OnMountAdd(reveal.AttachRoot)
            .OnUnmountAdd(reveal.DetachRoot)
            .AutomationId("History.Filter.FileType");

        void Advance()
        {
            if (phase != RevealPhase.Idle) return;
            if (hidden.Length == 0)
            {
                var retained = types.Take(CommonTypeCount).Concat(Props.Selected).ToHashSet();
                if (!reduceMotion && reveal.BeginCollapse(retained, Collapse))
                    setPhase(RevealPhase.Collapsing);
                else
                    Collapse();
                return;
            }

            var nextCount = hidden[Math.Min(RevealBatchSize, hidden.Length) - 1].Index + 1;
            var animate = !reduceMotion && reveal.Begin(() => setPhase(RevealPhase.Idle));
            setPhase(animate ? RevealPhase.Revealing : RevealPhase.Idle);
            setVisibleCount(nextCount);
        }

        void Collapse()
        {
            setVisibleCount(CommonTypeCount);
            setPhase(RevealPhase.Idle);
        }
    }

    private enum RevealPhase { Idle, Revealing, Collapsing }

    private static string LabelKey(FileTypeGlyphKind kind) => kind switch
    {
        FileTypeGlyphKind.Document => "HistoryFilterTypeDocuments",
        FileTypeGlyphKind.Folder => "HistoryFilterTypeFolders",
        FileTypeGlyphKind.Archive => "HistoryFilterTypeArchives",
        FileTypeGlyphKind.Pdf => "HistoryFilterTypePdf",
        FileTypeGlyphKind.Text => "HistoryFilterTypeText",
        FileTypeGlyphKind.Video => "HistoryFilterTypeVideos",
        FileTypeGlyphKind.Image => "HistoryFilterTypeImages",
        FileTypeGlyphKind.Audio => "HistoryFilterTypeAudio",
        FileTypeGlyphKind.Email => "HistoryFilterTypeEmail",
        FileTypeGlyphKind.Calendar => "HistoryFilterTypeCalendar",
        FileTypeGlyphKind.OfflineMap => "HistoryFilterTypeOfflineMaps",
        FileTypeGlyphKind.Database => "HistoryFilterTypeDatabases",
        FileTypeGlyphKind.Font => "HistoryFilterTypeFonts",
        FileTypeGlyphKind.Ebook => "HistoryFilterTypeEbooks",
        FileTypeGlyphKind.Contact => "HistoryFilterTypeContacts",
        FileTypeGlyphKind.Subtitle => "HistoryFilterTypeSubtitles",
        FileTypeGlyphKind.TextMessage => "HistoryFilterTypeTextMessages",
        FileTypeGlyphKind.Clipboard => "HistoryFilterTypeClipboard",
    };
}
