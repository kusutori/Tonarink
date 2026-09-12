using System.Runtime.InteropServices.Marshalling;

namespace Tonarink.ExplorerCommand;

[GeneratedComClass]
internal sealed partial class ExplorerCommand : IExplorerCommand, IObjectWithSite
{
    private nint _site;

    private const int Ok = 0;
    private const int NotImplemented = unchecked((int)0x80004001);
    private const int Fail = unchecked((int)0x80004005);
    private const uint Enabled = 0;
    private const uint Hidden = 2;

    public int GetTitle(nint psiItemArray, out nint ppszName)
    {
        _ = psiItemArray;
        ppszName = 0;
        try
        {
            ppszName = AppPaths.Dup(AppPaths.MenuTitle());
            return Ok;
        }
        catch (Exception exception)
        {
            AppPaths.Report("Could not provide the Explorer command title", exception);
            return Fail;
        }
    }

    public int GetIcon(nint psiItemArray, out nint ppszIcon)
    {
        _ = psiItemArray;
        ppszIcon = 0;
        try
        {
            var path = AppPaths.IconResource();
            if (path is null)
                return NotImplemented;

            ppszIcon = AppPaths.Dup(path);
            return Ok;
        }
        catch (Exception exception)
        {
            AppPaths.Report("Could not provide the Explorer command icon", exception);
            return Fail;
        }
    }

    public int GetToolTip(nint psiItemArray, out nint ppszInfotip)
    {
        _ = psiItemArray;
        ppszInfotip = 0;
        return NotImplemented;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = new Guid(AppPaths.Clsid);
        return Ok;
    }

    public int GetState(nint psiItemArray, int fOkToBeSlow, out uint pCmdState)
    {
        _ = psiItemArray;
        _ = fOkToBeSlow;
        pCmdState = AppPaths.IsMenuEnabled() ? Enabled : Hidden;
        return Ok;
    }

    public int Invoke(nint psiItemArray, nint pbc)
    {
        _ = pbc;
        try
        {
            var paths = ComVtbl.GetShellItemPaths(psiItemArray);
            if (paths.Count == 0)
                paths = ComVtbl.GetShellItemPathsFromSite(_site);
            if (paths.Count == 0)
                return Ok;

            AppPaths.WriteShareRequest(paths);
            AppPaths.LaunchApp();
            return Ok;
        }
        catch (Exception exception)
        {
            AppPaths.Report("Could not invoke the Explorer share command", exception);
            return Fail;
        }
    }

    public int SetSite(nint pUnkSite)
    {
        var previous = Interlocked.Exchange(ref _site, 0);
        ComVtbl.Release(previous);
        if (pUnkSite == 0)
            return Ok;

        ComVtbl.AddRef(pUnkSite);
        Interlocked.Exchange(ref _site, pUnkSite);
        return Ok;
    }

    public int GetSite(in Guid riid, out nint ppvSite)
    {
        var site = Interlocked.CompareExchange(ref _site, 0, 0);
        if (site == 0)
        {
            ppvSite = 0;
            return unchecked((int)0x80004005);
        }

        return ComVtbl.QueryInterface(site, in riid, out ppvSite);
    }

    public int GetFlags(out uint pFlags)
    {
        pFlags = 0;
        return Ok;
    }

    public int EnumSubCommands(out nint ppEnum)
    {
        ppEnum = 0;
        return NotImplemented;
    }
}
