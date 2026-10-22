using System.Reflection;

namespace Tonarink.Application;

/// <summary>Shared product metadata, supplied by Directory.Build.props.</summary>
public static class AppIdentity
{
    private static readonly Assembly IdentityAssembly = typeof(AppIdentity).Assembly;

    public static string Name { get; } = Required(
        IdentityAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product, "Product");

    public static string Publisher { get; } = Required(
        IdentityAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company, "Company");

    public static Uri RepositoryUri { get; } = new(Required(
        IdentityAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static attribute => attribute.Key == "RepositoryUrl")?.Value,
        "RepositoryUrl").TrimEnd('/') + "/");

    public static Uri IssuesUri { get; } = new(RepositoryUri, "issues");
    public static Uri NoticesUri { get; } = new(RepositoryUri, "blob/main/NOTICE");

    // Shared by the desktop app, Explorer extension and widget provider. Values
    // must continue matching the existing unpackaged data directory.
    public static string SharedDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Publisher, Name);

    private static string Required(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Application metadata '{key}' is missing.");
}
