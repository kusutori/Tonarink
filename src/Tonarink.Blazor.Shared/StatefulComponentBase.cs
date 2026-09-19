using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using System.Globalization;
using Tonarink.Application;
using Tonarink.Blazor.Shared.Resources;

namespace Tonarink.Blazor.Shared;

public abstract class StatefulComponentBase : ComponentBase, IDisposable
{
    [Inject]
    protected TonarinkAppState AppState { get; set; } = null!;

    [Inject]
    protected IStringLocalizer<SharedResources> Loc { get; set; } = null!;

    protected override void OnInitialized()
    {
        ApplyCulture();
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

    protected string T(string key) => Loc[key];

    protected string T(string key, params object[] args) => Loc[key, args];

    protected void ApplyCulture()
    {
        var name = AppLanguages.Resolve(AppState.Settings.Language, CultureInfo.CurrentUICulture.Name);
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    protected virtual void OnAppStateChanged() { }

    private void HandleStateChanged() => _ = InvokeAsync(() =>
    {
        ApplyCulture();
        OnAppStateChanged();
        StateHasChanged();
    });
}
