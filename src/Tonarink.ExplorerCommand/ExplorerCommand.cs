using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using System.Text;
using Tonarink.Application;

namespace Tonarink.ExplorerCommand;

[GeneratedComClass]
internal sealed partial class ExplorerCommand : IExplorerCommand, IObjectWithSite
{
    private nint _site;
    private readonly ExplorerCommand? _parent;
    private readonly ExplorerFavorite? _favorite;

    public ExplorerCommand() { }
    private ExplorerCommand(ExplorerCommand parent, ExplorerFavorite? favorite = null)
    {
        _parent = parent;
        _favorite = favorite;
    }

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
            var title = _favorite?.Name ?? (_parent is not null
                ? ExplorerStrings.Get("ExplorerCommandAddToList") ?? "Add to send list"
                : AppPaths.MenuTitle());
            ppszName = AppPaths.Dup(title.Replace("&", "&&", StringComparison.Ordinal));
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
        pguidCommandName = _favorite is { } favorite
            ? new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(AppIntegrationIds.ExplorerFavoriteIdPrefix + favorite.Fingerprint)).AsSpan(0, 16))
            : _parent is not null ? new Guid("09B36B4B-35A8-4367-B3A8-AC5E9A0BF632") : new Guid(AppPaths.Clsid);
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
            // A submenu parent is not a send action. Only its leaves invoke.
            if (SubmenuFavorites().Count > 0) return Ok;
            var paths = ComVtbl.GetShellItemPaths(psiItemArray);
            if (paths.Count == 0)
                paths = ComVtbl.GetShellItemPathsFromSite(_parent?._site ?? _site);
            if (paths.Count == 0)
                return Ok;

            AppPaths.WriteShareRequest(paths, _favorite?.Fingerprint);
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
        pFlags = SubmenuFavorites().Count > 0 ? 1u : 0u; // ECF_HASSUBCOMMANDS
        return Ok;
    }

    private IReadOnlyList<ExplorerFavorite> SubmenuFavorites() =>
        _parent is null && AppPaths.IsFavoriteMenuEnabled() ? AppPaths.Favorites() : [];

    public int EnumSubCommands(out nint ppEnum)
    {
        ppEnum = 0;
        try
        {
            var favorites = SubmenuFavorites();
            if (favorites.Count == 0) return NotImplemented;
            ExplorerCommand[] commands = [new(this), .. favorites.Select(favorite => new ExplorerCommand(this, favorite))];
            return ComServer.ExportInterface(new ExplorerCommandEnumerator(commands),
                typeof(IEnumExplorerCommand).GUID, out ppEnum);
        }
        catch (Exception exception)
        {
            AppPaths.Report("Could not create the favorites submenu", exception);
            return Fail;
        }
    }
}
