using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.System;

static class WindowsStartup
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Tonarink";

    public static async Task<bool> IsEnabledAsync()
    {
        if (AppPlatform.HasPackageIdentity())
        {
            try
            {
                var task = await StartupTask.GetAsync(AppPlatform.StartupTaskId);
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
                AppDiagnostics.Report("Could not read the packaged startup task; using the registry fallback", exception);
            }
        }

        return IsRegistryEnabled();
    }

    public static async Task SetEnabledAsync(bool enabled, bool startMinimized)
    {
        if (AppPlatform.HasPackageIdentity())
        {
            try
            {
                await SetPackagedAsync(enabled);
                return;
            }
            catch (StartupDisabledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
                AppDiagnostics.Report("Could not update the packaged startup task", exception);
                if (enabled)
                    throw;
            }
        }

        SetRegistry(enabled, startMinimized);
    }

    public static void UpdateLaunchCommand(bool startMinimized)
    {
        if (AppPlatform.HasPackageIdentity() || !IsRegistryEnabled())
            return;

        SetRegistry(enabled: true, startMinimized);
    }

    private static async Task SetPackagedAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(AppPlatform.StartupTaskId);
        if (enabled)
        {
            var state = task.State switch
            {
                StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => task.State,
                StartupTaskState.Disabled => await task.RequestEnableAsync(),
                _ => task.State,
            };

            if (state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
                return;

            if (state is StartupTaskState.DisabledByUser)
            {
                try
                {
                    await Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));
                }
                catch (Exception exception) when (exception is COMException or InvalidOperationException)
                {
                    AppDiagnostics.Report("Could not open Windows startup app settings", exception);
                }

                throw new StartupDisabledException("StartupDisabledByUser");
            }

            if (state is StartupTaskState.DisabledByPolicy)
                throw new StartupDisabledException("StartupDisabledByPolicy");

            throw new StartupDisabledException("StartupFailed");
        }

        if (task.State is StartupTaskState.Enabled)
            task.Disable();
    }

    private static bool IsRegistryEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(RunValueName) is string value
            && !string.IsNullOrWhiteSpace(value);
    }

    private static void SetRegistry(bool enabled, bool startMinimized)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("StartupFailed");

        if (!enabled)
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            return;
        }

        var command = $"\"{AppPlatform.ExecutablePath}\"";
        if (startMinimized)
            command += $" {AppPlatform.MinimizedArgument}";
        key.SetValue(RunValueName, command);
    }
}

sealed class StartupDisabledException(string resourceKey) : Exception(resourceKey)
{
    public string ResourceKey { get; } = resourceKey;
}
