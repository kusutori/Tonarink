using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Tonarink.ExplorerCommand;

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject);

    [PreserveSig]
    int LockServer(int fLock);
}

[GeneratedComInterface]
[Guid("fc4801a3-2ba9-11cf-a229-00aa003d569a")]
internal partial interface IObjectWithSite
{
    [PreserveSig]
    int SetSite(nint pUnkSite);

    [PreserveSig]
    int GetSite(in Guid riid, out nint ppvSite);
}

[GeneratedComInterface]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
internal partial interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(nint psiItemArray, out nint ppszName);

    [PreserveSig]
    int GetIcon(nint psiItemArray, out nint ppszIcon);

    [PreserveSig]
    int GetToolTip(nint psiItemArray, out nint ppszInfotip);

    [PreserveSig]
    int GetCanonicalName(out Guid pguidCommandName);

    [PreserveSig]
    int GetState(nint psiItemArray, int fOkToBeSlow, out uint pCmdState);

    [PreserveSig]
    int Invoke(nint psiItemArray, nint pbc);

    [PreserveSig]
    int GetFlags(out uint pFlags);

    [PreserveSig]
    int EnumSubCommands(out nint ppEnum);
}

[GeneratedComInterface]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
internal partial interface IEnumExplorerCommand
{
    [PreserveSig]
    int Next(uint count, nint commands, nint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out nint enumerator);
}


