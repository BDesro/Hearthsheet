using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hearthsheet.App;

/// <summary>
/// Plays a video's frames into an <see cref="Image"/>, decoded by Media Foundation's source reader on a background thread.
/// MediaElement goes through the GPU driver's video pipeline, which on many PCs (NVIDIA's default) draws black as RGB 16;
/// the source reader converts to full-range RGB itself, so black is 0 and frames match what WPF draws. Video only, once.
/// </summary>
public sealed class VideoFrames : IDisposable
{
    private sealed record Frame(long Time, byte[] Pixels, int Width, int Height);

    private const int Ahead = 4; // decoded frames waiting to be shown
    private readonly string _path;
    private readonly Image _target;
    private readonly ConcurrentQueue<Frame> _frames = new();
    private readonly ConcurrentBag<byte[]> _pool = new();
    private readonly SemaphoreSlim _space = new(Ahead);
    private readonly CancellationTokenSource _cancel = new();
    private readonly Stopwatch _clock = new();
    private WriteableBitmap? _bitmap;
    private long _firstTime;
    private volatile bool _decoded;
    private volatile Exception? _error;
    private bool _finished;

    /// <summary>The first frame is on screen.</summary>
    public event Action? Started;
    /// <summary>The last frame has been shown; it stays on screen.</summary>
    public event Action? Ended;
    /// <summary>The video can't be decoded (e.g. a Windows N edition without the Media Feature Pack).</summary>
    public event Action<Exception>? Failed;

    public VideoFrames(string path, Image target)
    {
        _path = path;
        _target = target;
    }

    public void Start()
    {
        CompositionTarget.Rendering += OnRendering;
        new Thread(Decode) { IsBackground = true, Name = "Intro video" }.Start();
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
        _finished = true;
        _cancel.Cancel();
    }

    // UI thread, once per display frame: show the newest frame that's due, dropping any we fell behind on.
    private void OnRendering(object? sender, EventArgs e)
    {
        if (_finished) return;
        if (_error is { } error) { Finish(); Failed?.Invoke(error); return; }

        Frame? due = null;
        while (_frames.TryPeek(out var next))
        {
            if (!_clock.IsRunning) { _firstTime = next.Time; _clock.Start(); }
            if (next.Time - _firstTime > _clock.Elapsed.Ticks) break;
            _frames.TryDequeue(out _);
            _space.Release();
            if (due is not null) _pool.Add(due.Pixels);
            due = next;
        }

        if (due is not null)
        {
            var first = _bitmap is null;
            if (first)
            {
                _bitmap = new WriteableBitmap(due.Width, due.Height, 96, 96, PixelFormats.Bgr32, null);
                _target.Source = _bitmap;
            }
            _bitmap!.WritePixels(new Int32Rect(0, 0, due.Width, due.Height), due.Pixels, due.Width * 4, 0);
            _pool.Add(due.Pixels);
            if (first) Started?.Invoke();
        }

        if (_decoded && _frames.IsEmpty) { Finish(); Ended?.Invoke(); }
    }

    private void Finish()
    {
        _finished = true;
        CompositionTarget.Rendering -= OnRendering;
    }

    // Background thread: decode ahead of the clock, at most Ahead frames.
    private void Decode()
    {
        IMFSourceReader? reader = null;
        try
        {
            Check(MF.MFStartup(MF.Version, MF.StartupLite));
            Check(MF.MFCreateAttributes(out var attributes, 1));
            attributes.SetUINT32(MF.AdvancedVideoProcessing, 1); // lets the reader convert YUV to RGB32
            Check(MF.MFCreateSourceReaderFromURL(_path, attributes, out reader));
            reader.SetStreamSelection(MF.AllStreams, false);
            reader.SetStreamSelection(MF.FirstVideoStream, true);

            Check(MF.MFCreateMediaType(out var wanted));
            wanted.SetGUID(MF.MajorType, MF.Video);
            wanted.SetGUID(MF.Subtype, MF.Rgb32);
            reader.SetCurrentMediaType(MF.FirstVideoStream, IntPtr.Zero, wanted);
            reader.GetCurrentMediaType(MF.FirstVideoStream, out var actual);
            actual.GetUINT64(MF.FrameSize, out var size);
            int width = (int)(size >> 32), height = (int)(size & 0xFFFFFFFF), row = width * 4;
            // RGB buffers may be stored bottom-up (negative stride); with no stride given, assume top-down rows.
            var stride = actual.GetUINT32(MF.DefaultStride, out var s) >= 0 ? (int)s : row;

            while (true)
            {
                reader.ReadSample(MF.FirstVideoStream, 0, out _, out var flags, out var time, out var sample);
                if ((flags & MF.EndOfStream) != 0) break;
                if (sample is null) continue;
                _space.Wait(_cancel.Token);

                var pixels = _pool.TryTake(out var reused) ? reused : new byte[row * height];
                sample.ConvertToContiguousBuffer(out var buffer);
                buffer.Lock(out var data, out _, out _);
                try
                {
                    var top = stride < 0 ? data + (height - 1) * -stride : data;
                    for (var y = 0; y < height; y++) Marshal.Copy(top + y * stride, pixels, y * row, row);
                }
                finally
                {
                    buffer.Unlock();
                    Marshal.ReleaseComObject(buffer);
                    Marshal.ReleaseComObject(sample);
                }
                _frames.Enqueue(new Frame(time, pixels, width, height));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            _error = ex;
        }
        finally
        {
            if (reader is not null) Marshal.ReleaseComObject(reader);
            MF.MFShutdown();
            _decoded = true;
        }
    }

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    // ---- Media Foundation interop: just the calls above. Unused vtable slots are placeholders that keep the order.

    private static class MF
    {
        public const int Version = 0x00020070, StartupLite = 1, EndOfStream = 0x2;
        public const int FirstVideoStream = unchecked((int)0xFFFFFFFC), AllStreams = unchecked((int)0xFFFFFFFE);
        public static readonly Guid AdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
        public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
        public static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");

        [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] public static extern int MFShutdown();
        [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes attributes, int size);
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFAttributes type);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        public static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes attributes, out IMFSourceReader reader);
    }

    // IMFMediaType adds methods after IMFAttributes' but needs none of them, so media types are used as IMFAttributes.
    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        void GetItem(); void GetItemType(); void CompareItem(); void Compare();
        [PreserveSig] int GetUINT32(in Guid key, out uint value);
        void GetUINT64(in Guid key, out ulong value);
        void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
        void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem();
        void DeleteAllItems();
        void SetUINT32(in Guid key, int value);
        void SetUINT64();
        void SetDouble();
        void SetGUID(in Guid key, in Guid value);
        void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount();
        void GetItemByIndex(); void CopyAllItems();
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        void GetStreamSelection();
        void SetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
        void GetNativeMediaType();
        void GetCurrentMediaType(int stream, out IMFAttributes type);
        void SetCurrentMediaType(int stream, IntPtr reserved, IMFAttributes type);
        void SetCurrentPosition();
        void ReadSample(int stream, int control, out int actualStream, out int flags, out long time, out IMFSample? sample);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        // IMFAttributes (30 slots)
        void A0(); void A1(); void A2(); void A3(); void A4(); void A5(); void A6(); void A7(); void A8(); void A9();
        void A10(); void A11(); void A12(); void A13(); void A14(); void A15(); void A16(); void A17(); void A18(); void A19();
        void A20(); void A21(); void A22(); void A23(); void A24(); void A25(); void A26(); void A27(); void A28(); void A29();
        void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime(); void SetSampleTime(); void GetSampleDuration();
        void SetSampleDuration(); void GetBufferCount(); void GetBufferByIndex();
        void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        void Lock(out IntPtr data, out int maxLength, out int currentLength);
        void Unlock();
    }
}
