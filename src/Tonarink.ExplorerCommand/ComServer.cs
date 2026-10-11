using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Tonarink.ExplorerCommand;

internal static partial class ComServer
{
    private const int ClassEClassNotAvailable = unchecked((int)0x80040111);
    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int False = 1;

    public static readonly StrategyBasedComWrappers Wrappers = new();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static unsafe int DllGetClassObject(Guid* rclsid, Guid* riid, void** ppv)
    {
        *ppv = null;
        if (*rclsid != new Guid(AppPaths.Clsid))
            return ClassEClassNotAvailable;

        return Export(new ClassFactory(), *riid, ppv);
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int DllCanUnloadNow() => False;

    internal static unsafe string? GetModuleFilePath()
    {
        const uint fromAddress = 0x00000004;
        const uint unchangedRefCount = 0x00000002;
        nint module = 0;
        if (GetModuleHandleExW(fromAddress | unchangedRefCount, (nint)(delegate* unmanaged<Guid*, Guid*, void**, int>)&DllGetClassObject, &module) == 0
            || module == 0)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[32768];
        uint length;
        fixed (char* pointer = buffer)
            length = GetModuleFileNameW(module, pointer, (uint)buffer.Length);
        if (length == 0 || length >= buffer.Length)
            return null;

        return new string(buffer[..(int)length]);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial int GetModuleHandleExW(uint dwFlags, nint lpModuleName, nint* phModule);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial uint GetModuleFileNameW(nint hModule, char* lpFilename, uint nSize);

    internal static unsafe int Export(object instance, Guid riid, void** ppv)
    {
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
        var result = Marshal.QueryInterface(unknown, in riid, out var pointer);
        Marshal.Release(unknown);
        if (result != 0)
        {
            *ppv = null;
            return result;
        }

        *ppv = (void*)pointer;
        return 0;
    }

    internal static int ExportInterface(object instance, Guid iid, out nint pointer)
    {
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
        try { return Marshal.QueryInterface(unknown, in iid, out pointer); }
        finally { Marshal.Release(unknown); }
    }

    [GeneratedComClass]
    private sealed partial class ClassFactory : IClassFactory
    {
        public int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject)
        {
            ppvObject = 0;
            if (pUnkOuter != 0)
                return ClassENoAggregation;

            var unknown = Wrappers.GetOrCreateComInterfaceForObject(
                new ExplorerCommand(),
                CreateComInterfaceFlags.None);
            var result = Marshal.QueryInterface(unknown, in riid, out ppvObject);
            Marshal.Release(unknown);
            return result;
        }

        public int LockServer(int fLock)
        {
            _ = fLock;
            return 0;
        }
    }
}
