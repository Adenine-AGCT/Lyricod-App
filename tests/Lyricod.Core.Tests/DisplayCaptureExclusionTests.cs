using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Lyricod.Platform;

namespace Lyricod.Core.Tests;

public unsafe class DisplayCaptureExclusionTests
{
    [Fact]
    public void OlderBuildsAndMissingHandlesNeverQueryTheSession()
    {
        Assert.False(DisplayCaptureExclusion.TryExcludeWindow(1, 1, 26099, out _));
        Assert.False(DisplayCaptureExclusion.TryExcludeWindow(0, 1, 26100, out _));
        Assert.False(DisplayCaptureExclusion.TryExcludeWindow(1, 0, 28000, out _));
    }

    [Fact]
    public void MissingDisplayInterfaceFallsBack()
    {
        nint other = (nint)ComInterfaceMarshaller<ICaptureWindowIds>.ConvertToUnmanaged(new CaptureWindowIds(123));
        try { Assert.False(DisplayCaptureExclusion.TryExcludeWindow(other, 123, 26100, out _)); }
        finally { ComInterfaceMarshaller<ICaptureWindowIds>.Free((void*)other); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSessionReceivesTheWindowsWindowIdIterable(bool useGetMany)
    {
        Assert.Equal(8, Marshal.SizeOf<CaptureWindowId>());
        var session = new TestDisplaySession { UseGetMany = useGetMany };
        nint native = (nint)ComInterfaceMarshaller<ITestDisplaySession>.ConvertToUnmanaged(session);
        nint window = unchecked((nint)0x1234ABCDFFFF1234L);
        try
        {
            Assert.True(DisplayCaptureExclusion.TryExcludeWindow(native, window, 26100, out ulong first));
            Assert.Equal(unchecked((ulong)(nuint)window), session.Window);
            Assert.Equal(1ul, first); // the configuration Windows says leaves the window out
            // A new session/monitor gets its own exclusion, including the same full-width HWND.
            Assert.True(DisplayCaptureExclusion.TryExcludeWindow(native, window, 28000, out ulong second));
            Assert.Equal(2, session.Calls);
            Assert.Equal(2ul, second);
        }
        finally { ComInterfaceMarshaller<ITestDisplaySession>.Free((void*)native); }
    }

    [Fact]
    public void RejectedExclusionDoesNotReportSuccess()
    {
        var session = new TestDisplaySession { Result = unchecked((int)0x80070005) };
        nint native = (nint)ComInterfaceMarshaller<ITestDisplaySession>.ConvertToUnmanaged(session);
        try { Assert.False(DisplayCaptureExclusion.TryExcludeWindow(native, 123, 26100, out _)); }
        finally { ComInterfaceMarshaller<ITestDisplaySession>.Free((void*)native); }
    }

    [Fact]
    public void AvailableOnlyWhereTheSessionHasTheDisplayInterface()
    {
        nint native = (nint)ComInterfaceMarshaller<ITestDisplaySession>.ConvertToUnmanaged(new TestDisplaySession());
        nint other = (nint)ComInterfaceMarshaller<ICaptureWindowIds>.ConvertToUnmanaged(new CaptureWindowIds(123));
        try
        {
            Assert.True(DisplayCaptureExclusion.IsAvailable(native, 26100));
            Assert.False(DisplayCaptureExclusion.IsAvailable(native, 26099));
            Assert.False(DisplayCaptureExclusion.IsAvailable(other, 26100));
            Assert.False(DisplayCaptureExclusion.IsAvailable(0, 26100));
        }
        finally
        {
            ComInterfaceMarshaller<ITestDisplaySession>.Free((void*)native);
            ComInterfaceMarshaller<ICaptureWindowIds>.Free((void*)other);
        }
    }

    [Fact]
    public void FrameSaysItsCaptureConfiguration()
    {
        nint frame = (nint)ComInterfaceMarshaller<ITestFrame3>.ConvertToUnmanaged(new TestFrame { Iteration = 7 });
        nint other = (nint)ComInterfaceMarshaller<ICaptureWindowIds>.ConvertToUnmanaged(new CaptureWindowIds(123));
        nint failing = (nint)ComInterfaceMarshaller<ITestFrame3>.ConvertToUnmanaged(new TestFrame { Result = unchecked((int)0x80004005) });
        try
        {
            Assert.Equal(7ul, DisplayCaptureExclusion.FrameIteration(frame));
            Assert.Null(DisplayCaptureExclusion.FrameIteration(other)); // older Windows: frames don't say
            Assert.Null(DisplayCaptureExclusion.FrameIteration(failing));
            Assert.Null(DisplayCaptureExclusion.FrameIteration(0));
        }
        finally
        {
            ComInterfaceMarshaller<ITestFrame3>.Free((void*)frame);
            ComInterfaceMarshaller<ICaptureWindowIds>.Free((void*)other);
            ComInterfaceMarshaller<ITestFrame3>.Free((void*)failing);
        }
    }

    [Fact]
    public unsafe void EmptyGetManyDoesNotAdvanceAndCurrentFailsPastTheEnd()
    {
        var iterator = new CaptureWindowIdIterator(new CaptureWindowId(123));
        Assert.Equal(0, iterator.GetMany(0, null, out uint actual));
        Assert.Equal(0u, actual);
        Assert.Equal(0, iterator.GetCurrent(out var value));
        Assert.Equal(123ul, value.Value);
        Assert.Equal(unchecked((int)0x80004003), iterator.GetMany(1, null, out actual));
        Assert.Equal(0, iterator.MoveNext(out byte more));
        Assert.Equal((byte)0, more);
        Assert.Equal(unchecked((int)0x8000000B), iterator.GetCurrent(out _));
    }
}

// A native ABI test double: validates the actual QI, vtable slots, iterable and iterator,
// without Windows.Graphics.Capture or a GPU. Real capture/recording still needs Windows testing.
[GeneratedComInterface, Guid("BB91F61B-218A-587D-8580-2701A74C0525")]
internal partial interface ITestDisplaySession : ICaptureInspectable
{
    [PreserveSig] int SetWindowExclusionList(nint windows, out ulong iteration);
    [PreserveSig] int GetWindowExclusionList(out nint windows);
}

[GeneratedComClass]
internal sealed partial class TestDisplaySession : ITestDisplaySession
{
    internal bool UseGetMany;
    internal int Result;
    internal ulong Window;
    internal int Calls;
    public int GetIids(out uint count, out nint ids) => DisplayCaptureExclusion.GetIids(typeof(ITestDisplaySession).GUID, out count, out ids);
    public int GetRuntimeClassName(out nint name) { name = 0; return 0; }
    public int GetTrustLevel(out int level) { level = 0; return 0; }
    public int GetWindowExclusionList(out nint windows) { windows = 0; return unchecked((int)0x80004001); }
    public unsafe int SetWindowExclusionList(nint windows, out ulong iteration)
    {
        iteration = (ulong)++Calls;
        if (Result < 0) return Result;
        var iid = typeof(ICaptureWindowIds).GUID;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(windows, in iid, out nint iterable));
        nint iterator = 0;
        try
        {
            var first = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(void***)iterable)[6];
            Marshal.ThrowExceptionForHR(first(iterable, &iterator));
            var vtable = *(void***)iterator;
            CaptureWindowId value;
            if (UseGetMany)
            {
                uint actual;
                var many = (delegate* unmanaged[Stdcall]<nint, uint, CaptureWindowId*, uint*, int>)vtable[9];
                Marshal.ThrowExceptionForHR(many(iterator, 1, &value, &actual));
                if (actual != 1) return unchecked((int)0x80004005);
            }
            else
            {
                var current = (delegate* unmanaged[Stdcall]<nint, CaptureWindowId*, int>)vtable[6];
                Marshal.ThrowExceptionForHR(current(iterator, &value));
                byte more;
                var next = (delegate* unmanaged[Stdcall]<nint, byte*, int>)vtable[8];
                Marshal.ThrowExceptionForHR(next(iterator, &more));
                if (more != 0) return unchecked((int)0x80004005);
            }
            Window = value.Value;
            return 0;
        }
        finally
        {
            if (iterator != 0) Marshal.Release(iterator);
            Marshal.Release(iterable);
        }
    }
}

// IDirect3D11CaptureFrame3's ABI: IInspectable, then get_ConfigurationIteration.
[GeneratedComInterface, Guid("71616DC8-FEA5-5741-A3D8-591ACC39A9EE")]
internal partial interface ITestFrame3 : ICaptureInspectable
{
    [PreserveSig] int GetConfigurationIteration(out ulong iteration);
}

[GeneratedComClass]
internal sealed partial class TestFrame : ITestFrame3
{
    internal ulong Iteration;
    internal int Result;
    public int GetIids(out uint count, out nint ids) => DisplayCaptureExclusion.GetIids(typeof(ITestFrame3).GUID, out count, out ids);
    public int GetRuntimeClassName(out nint name) { name = 0; return 0; }
    public int GetTrustLevel(out int level) { level = 0; return 0; }
    public int GetConfigurationIteration(out ulong iteration) { iteration = Iteration; return Result; }
}
