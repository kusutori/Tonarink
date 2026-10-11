using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Tonarink.ExplorerCommand;

// Keep a local snapshot for one menu. Explorer's enumeration callbacks must
// never scan the network or launch the app.
[GeneratedComClass]
internal sealed partial class ExplorerCommandEnumerator(ExplorerCommand[] commands, int position = 0)
    : IEnumExplorerCommand
{
    private int _position = position;

    public unsafe int Next(uint count, nint output, nint fetched)
    {
        if (fetched != 0) *(uint*)fetched = 0;
        if (count == 0) return 0;
        if (output == 0) return unchecked((int)0x80004003);
        var available = (int)Math.Min(count, (uint)(commands.Length - _position));
        var written = 0;
        try
        {
            for (; written < available; written++)
            {
                Marshal.ThrowExceptionForHR(ComServer.ExportInterface(commands[_position + written],
                    typeof(IExplorerCommand).GUID, out var pointer));
                ((nint*)output)[written] = pointer;
            }
            _position += written;
            if (fetched != 0) *(uint*)fetched = (uint)written;
            return (uint)written == count ? 0 : 1; // S_OK / S_FALSE
        }
        catch (Exception exception)
        {
            for (var index = 0; index < written; index++)
            {
                Marshal.Release(((nint*)output)[index]);
                ((nint*)output)[index] = 0;
            }
            AppPaths.Report("Could not enumerate Explorer commands", exception);
            return Marshal.GetHRForException(exception);
        }
    }

    public int Skip(uint count)
    {
        var skipped = (int)Math.Min(count, (uint)(commands.Length - _position));
        _position += skipped;
        return (uint)skipped == count ? 0 : 1;
    }

    public int Reset() { _position = 0; return 0; }

    public int Clone(out nint enumerator)
    {
        enumerator = 0;
        try
        {
            return ComServer.ExportInterface(new ExplorerCommandEnumerator(commands, _position),
                typeof(IEnumExplorerCommand).GUID, out enumerator);
        }
        catch (Exception exception)
        {
            AppPaths.Report("Could not clone Explorer commands", exception);
            return Marshal.GetHRForException(exception);
        }
    }
}
