using System.Text.Json;
using Tonarink.Application;

namespace Tonarink.WidgetProvider;

internal sealed class ReceiveWidget
{
    public const string DefinitionId = AppIntegrationIds.WidgetDefinitionId;
    private static readonly string TemplatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "ReceiveWidget.json");
    private static string? Template;

    public ReceiveWidget(string widgetId, string page = WidgetSnapshot.NearbyPage)
    {
        Id = widgetId;
        Page = string.Equals(page, WidgetSnapshot.HistoryPage, StringComparison.Ordinal)
            ? WidgetSnapshot.HistoryPage
            : WidgetSnapshot.NearbyPage;
    }

    public string Id { get; }

    public string Page { get; set; }

    public static string GetTemplate()
    {
        if (Template is not null)
            return Template;

        Template = File.Exists(TemplatePath)
            ? File.ReadAllText(TemplatePath)
            : $$"""{"type":"AdaptiveCard","version":"1.5","body":[{"type":"TextBlock","text":{{JsonSerializer.Serialize(AppIdentity.Name, WidgetJsonContext.Default.String)}},"weight":"bolder"}]}""";
        return Template;
    }

    public string GetData() => WidgetSnapshot.Capture(Page);
}
