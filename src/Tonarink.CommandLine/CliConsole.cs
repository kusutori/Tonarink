using System.Runtime.InteropServices;
using System.Text;

namespace Tonarink.Cli;

static partial class CliConsole
{
    // Alias activations request the console subsystem. For direct developer
    // launches attach to the caller only when no redirected handles exist;
    // attaching indiscriminately would destroy shell pipelines/redirection.
    public static void Initialize()
    {
        if (!OperatingSystem.IsWindows()) return;
        var output = GetStdHandle(-11);
        if (output == IntPtr.Zero || output == new IntPtr(-1))
            _ = AttachConsole(uint.MaxValue);
        var encoding = new UTF8Encoding(false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), encoding) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), encoding));
    }

    public static bool HasConsole => OperatingSystem.IsWindows() && GetConsoleWindow() != IntPtr.Zero;

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetStdHandle(int handle);
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);
    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();
}
