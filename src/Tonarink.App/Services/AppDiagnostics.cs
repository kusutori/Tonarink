using System.Diagnostics;

static class AppDiagnostics
{
    public static void Report(string operation, Exception exception) =>
        Trace.WriteLine($"[Tonarink] {operation}: {exception.GetType().Name}: {exception.Message}");
}
