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

    public override Element Render()
    {
        var t = UseIntl();
        var reduceMotion = UseReducedMotion();
        var reveal = UseMemo(static () => new HistoryTypeReveal());
        var (phase, setPhase) = UseState(RevealPhase.Collapsed);
        var showAll = phase != RevealPhase.Collapsed;
        UseEffect(() =>
        {
            if (reduceMotion) reveal.Complete();
        }, reduceMotion);
        var types = HistoryFilter.AvailableFileTypes;
        var buttons = types
            .Where((kind, index) => showAll || index < CommonTypeCount || Props.Selected.Contains(kind))
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

        if (phase == RevealPhase.Revealing
            || (!showAll && types.Skip(CommonTypeCount).Any(kind => !Props.Selected.Contains(kind))))
        {
            var more = t.Message(new("App", "HistoryFilterMoreTypes"));
            buttons.Add(Button(Icon(AppIcons.More).AccessibilityHidden(), Expand)
                .IsHitTestVisible(phase != RevealPhase.Revealing)
                .IsTabStop(phase != RevealPhase.Revealing)
                .AutomationName(more)
                .ToolTip(more)
                .AutomationId("History.Filter.MoreTypes")
                .OnMountAdd(reveal.AttachMore)
                .OnUnmountAdd(reveal.DetachMore)
                .WithKey("more-types"));
        }

        return (FlexRow([.. buttons]) with
            {
                Wrap = FlexWrap.Wrap,
                ColumnGap = 8,
                RowGap = 8,
            })
            .AutomationName(t.Message(new("App", "HistoryFilterFileType")))
            .HelpText(t.Message(new("App", "HistoryFilterTypeAll")))
            .OnMountAdd(reveal.AttachRoot)
            .OnUnmountAdd(reveal.DetachRoot)
            .AutomationId("History.Filter.FileType");

        void Expand()
        {
            if (phase != RevealPhase.Collapsed) return;
            var animate = !reduceMotion && reveal.Begin(() => setPhase(RevealPhase.Expanded));
            setPhase(animate ? RevealPhase.Revealing : RevealPhase.Expanded);
        }
    }

    private enum RevealPhase { Collapsed, Revealing, Expanded }

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
