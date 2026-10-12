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

    protected string T(string key) => Localize(key, null);

    protected string T(string key, params object[] args) => Localize(key, args);

    protected string TransferStatusText(TransferStatus status) => T("Status" + status);

    protected string TransferSummary(TransferActivity activity) => activity.ItemCount is { } count
        ? AppText.Get("ItemSummary", AppState.Settings.Language, count) : activity.Summary;

    protected string DeviceModel(NearbyDevice device) => device.IsModelFallback
        && device.DeviceType is "Desktop" or "Mobile" or "Server" or "Headless" or "Web"
            ? AppText.Get("Device" + device.DeviceType, AppState.Settings.Language) : device.Model;

    protected string SenderModel(IncomingOffer offer) => offer.IsSenderModelFallback
        && offer.SenderDeviceType is "Desktop" or "Mobile" or "Server" or "Headless" or "Web"
            ? AppText.Get("Device" + offer.SenderDeviceType, AppState.Settings.Language) : offer.SenderModel;

    private string Localize(string key, object[]? args)
    {
        // A queued render can inherit a different execution context from the
        // state-change callback. Resolve in this component's selected culture,
        // without changing process defaults or leaking it to another renderer.
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUICulture = CultureInfo.CurrentUICulture;
        try
        {
            ApplyCulture();
            return args is null ? Loc[key] : Loc[key, args];
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUICulture;
        }
    }

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
