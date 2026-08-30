using Microsoft.AspNetCore.Components;
using System.Globalization;
using Tonarink.Application;

namespace Tonarink.Blazor.Shared;

public abstract class StatefulComponentBase : ComponentBase, IDisposable
{
    [Inject]
    protected TonarinkAppState AppState { get; set; } = null!;

    protected override void OnInitialized()
    {
        AppState.Changed += HandleStateChanged;
        base.OnInitialized();
    }

    public void Dispose()
    {
        AppState.Changed -= HandleStateChanged;
        GC.SuppressFinalize(this);
    }

    protected static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var display = (double)Math.Max(0, bytes);
        var unit = 0;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }
        return $"{display:0.#} {units[unit]}";
    }

    protected bool IsChinese => AppState.Settings.Language switch
    {
        TonarinkLanguage.SimplifiedChinese => true,
        TonarinkLanguage.English => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase),
    };

    protected string L(string chinese, string english) => IsChinese ? chinese : english;

    protected virtual void OnAppStateChanged() { }

    private void HandleStateChanged() => _ = InvokeAsync(() =>
    {
        OnAppStateChanged();
        StateHasChanged();
    });
}
