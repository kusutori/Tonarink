using System.Diagnostics;

// Test-owned subprocess only. No production app/profile or host is involved.
if (args.Length != 2 || !Guid.TryParse(args[1], out var nonce))
    return 2;
using var self = Process.GetCurrentProcess();
Console.WriteLine($"ready:{nonce:D}:{self.Id}");
Console.Out.Flush();
switch (args[0])
{
    case "success":
        Console.WriteLine("probe stdout");
        Console.Error.WriteLine("probe stderr");
        return 0;
    case "fail":
        Console.Error.WriteLine("intentional exit 17");
        return 17;
    case "kill":
        self.Kill();
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 2;
    case "hang":
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 2;
    default:
        return 2;
}
