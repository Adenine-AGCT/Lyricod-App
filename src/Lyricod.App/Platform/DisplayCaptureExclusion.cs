using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Lyricod.Platform;

/// <summary>Optional Windows 11 display-session exclusion, without raising our 19041 SDK target.</summary>
internal static class DisplayCaptureExclusion
{
    // Windows.Graphics.Capture.IDisplayGraphicsCaptureSession (UniversalApiContract 19).
    internal static readonly Guid SessionId = new("BB91F61B-218A-587D-8580-2701A74C0525");

    public static unsafe bool TryExcludeWindow(nint session, nint window, int windowsBuild)
    {
        // Updated 26100+ builds can expose this; the build number alone doesn't guarantee it.
        if (windowsBuild < 26100 || session == 0 || window == 0) return false;
        if (Marshal.QueryInterface(session, in SessionId, out nint display) < 0) return false;
        nint windows = 0;
        try
        {
            windows = (nint)ComInterfaceMarshaller<ICaptureWindowIds>.ConvertToUnmanaged(new CaptureWindowIds(window));
            // IUnknown (3), IInspectable (3), then SetWindowExclusionList(IIterable<WindowId>, out UInt64).
            var set = (delegate* unmanaged[Stdcall]<nint, nint, ulong*, int>)(*(void***)display)[6];
            ulong iteration;
            return set(display, windows, &iteration) >= 0;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("display capture exclusion: " + e);
            return false;
        }
        finally
        {
            if (windows != 0) ComInterfaceMarshaller<ICaptureWindowIds>.Free((void*)windows);
            Marshal.Release(display);
        }
    }

    internal static int GetIids(Guid id, out uint count, out nint ids)
    {
        count = 1;
        ids = Marshal.AllocCoTaskMem(16);
        Marshal.StructureToPtr(id, ids, false);
        return 0;
    }
}

// This is the ABI of *Windows.UI.WindowId* (one UInt64), NOT Microsoft.UI.WindowId.
// The older SDK doesn't project it. The collection IIDs below are specifically for this Windows type.
// HWND -> WindowId is the cast documented by the Windows capture API's maintainer.
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CaptureWindowId(ulong value)
{
    public readonly ulong Value = value;
}

[GeneratedComInterface, Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90")]
internal partial interface ICaptureInspectable
{
    [PreserveSig] int GetIids(out uint count, out nint ids);
    [PreserveSig] int GetRuntimeClassName(out nint name);
    [PreserveSig] int GetTrustLevel(out int level);
}

[GeneratedComInterface, Guid("745698BF-22AD-5C0D-B0E0-07D35A1C9719")]
internal partial interface ICaptureWindowIds : ICaptureInspectable
{
    [PreserveSig] int First(out nint iterator);
}

[GeneratedComInterface, Guid("BA0A30A1-C082-5671-AC07-7AAA4F269670")]
internal partial interface ICaptureWindowIdIterator : ICaptureInspectable
{
    [PreserveSig] int GetCurrent(out CaptureWindowId value);
    [PreserveSig] int GetHasCurrent(out byte value);
    [PreserveSig] int MoveNext(out byte value);
    [PreserveSig] unsafe int GetMany(uint capacity, CaptureWindowId* values, out uint actual);
}

[GeneratedComInterface, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
internal partial interface ICaptureAgileObject { }

/// <summary>A one-window WinRT iterable. Native references keep both it and its iterators alive.</summary>
[GeneratedComClass]
internal sealed partial class CaptureWindowIds(nint window) : ICaptureWindowIds, ICaptureAgileObject
{
    public int GetIids(out uint count, out nint ids) => DisplayCaptureExclusion.GetIids(typeof(ICaptureWindowIds).GUID, out count, out ids);
    public int GetRuntimeClassName(out nint name) { name = 0; return 0; }
    public int GetTrustLevel(out int level) { level = 0; return 0; }
    public unsafe int First(out nint iterator)
    {
        iterator = (nint)ComInterfaceMarshaller<ICaptureWindowIdIterator>.ConvertToUnmanaged(
            new CaptureWindowIdIterator(new CaptureWindowId(unchecked((ulong)(nuint)window))));
        return 0;
    }
}

[GeneratedComClass]
internal sealed partial class CaptureWindowIdIterator(CaptureWindowId window) : ICaptureWindowIdIterator, ICaptureAgileObject
{
    bool _done;
    public int GetIids(out uint count, out nint ids) => DisplayCaptureExclusion.GetIids(typeof(ICaptureWindowIdIterator).GUID, out count, out ids);
    public int GetRuntimeClassName(out nint name) { name = 0; return 0; }
    public int GetTrustLevel(out int level) { level = 0; return 0; }
    public int GetCurrent(out CaptureWindowId value) { value = _done ? default : window; return _done ? unchecked((int)0x8000000B) : 0; } // E_BOUNDS
    public int GetHasCurrent(out byte value) { value = _done ? (byte)0 : (byte)1; return 0; }
    public int MoveNext(out byte value) { _done = true; value = 0; return 0; }
    public unsafe int GetMany(uint capacity, CaptureWindowId* values, out uint actual)
    {
        actual = 0;
        if (capacity == 0 || _done) return 0;
        if (values == null) return unchecked((int)0x80004003); // E_POINTER
        values[0] = window;
        actual = 1;
        _done = true;
        return 0;
    }
}
