using System.Runtime.InteropServices;

namespace Tonarink.Services.Platform;

// Use the native IShellLinkW/IPersistFile ABI, not dynamic WScript.Shell or a
// reflection-created RCW. These bounded COM calls also work in Native AOT.
sealed unsafe partial class WindowsShellLink : IDisposable
{
    private nint _link;
    private nint _file;

    public WindowsShellLink()
    {
        var clsid = new Guid("00021401-0000-0000-C000-000000000046");
        var iid = new Guid("000214F9-0000-0000-C000-000000000046");
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in iid, out _link));
        try
        {
            var persist = new Guid("0000010B-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_link, in persist, out _file));
        }
        catch { Dispose(); throw; }
    }

    public string Description
    {
        get
        {
            Span<char> buffer = stackalloc char[256];
            buffer.Clear();
            var get = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)Function(_link, 6);
            fixed (char* pointer = buffer)
                Marshal.ThrowExceptionForHR(get(_link, pointer, buffer.Length));
            var end = buffer.IndexOf('\0');
            return new string(end < 0 ? buffer : buffer[..end]);
        }
    }

    public void SetTarget(string value) => SetString(20, value);
    public void SetArguments(string value) => SetString(11, value);
    public void SetDescription(string value) => SetString(7, value);
    public void SetWorkingDirectory(string value) => SetString(9, value);

    public void SetIcon(string path)
    {
        var set = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)Function(_link, 17);
        fixed (char* value = path) Marshal.ThrowExceptionForHR(set(_link, value, 0));
    }

    public void Load(string path)
    {
        var load = (delegate* unmanaged[Stdcall]<nint, char*, uint, int>)Function(_file, 5);
        fixed (char* value = path) Marshal.ThrowExceptionForHR(load(_file, value, 0));
    }

    public void Save(string path)
    {
        var save = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)Function(_file, 6);
        fixed (char* value = path) Marshal.ThrowExceptionForHR(save(_file, value, 1));
    }

    public void Dispose()
    {
        if (_file != 0) Marshal.Release(Interlocked.Exchange(ref _file, 0));
        if (_link != 0) Marshal.Release(Interlocked.Exchange(ref _link, 0));
    }

    private void SetString(int slot, string text)
    {
        var set = (delegate* unmanaged[Stdcall]<nint, char*, int>)Function(_link, slot);
        fixed (char* value = text) Marshal.ThrowExceptionForHR(set(_link, value));
    }

    private static nint Function(nint instance, int slot) => (*(nint**)instance)[slot];

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
