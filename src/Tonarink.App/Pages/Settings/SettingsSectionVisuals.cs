using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;

namespace Tonarink.Pages.Settings;

static class SettingsSectionVisuals
{
    public static Element HeaderGlyph(string glyph) =>
        Icon(glyph).AccessibilityHidden();

    public static Element SettingsGroup(string title, params Element[] cards) =>
        VStack(4,
        [
            Subtitle(title)
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .Margin(bottom: 8),
            .. cards.Select(card => card.HAlign(HorizontalAlignment.Stretch)),
        ]);

}
