using System.Runtime.InteropServices;

namespace LabelMaker.App.Scanning;

/// <summary>A camera Windows knows about.</summary>
public sealed record CameraInfo(string Name, string Id)
{
    public override string ToString() => Name;
}

/// <summary>One picture from a camera: 32-bit BGRA pixels, top row first.</summary>
public sealed record CameraFrame(byte[] Bgra, int Width, int Height)
{
    public int Stride => Width * 4;
}

/// <summary>
/// Reads pictures from a webcam (or a video file, for testing) through Windows Media
/// Foundation, which every Windows 10 and 11 PC has — nothing to install. Frames are turned
/// into BGRA by Windows' own video processor, whatever the camera sends.
/// </summary>
public sealed class Camera : IDisposable
{
    private static readonly object StartLock = new();
    private static int _started;

    private IMFSourceReader? _reader;
    private IMFMediaSource? _source;

    // Bytes from one row to the next in a frame, as Windows reports it; negative: bottom row first. 0: not said.
    private readonly int _stride;

    private Camera(IMFSourceReader reader, IMFMediaSource? source, int width, int height, int stride)
    {
        _reader = reader;
        _source = source;
        Width = width;
        Height = height;
        _stride = stride;
    }

    public int Width { get; }
    public int Height { get; }

    private static void Start()
    {
        lock (StartLock)
        {
            if (_started++ == 0)
            {
                Check(MF.MFStartup(MF.Version, 0), "Windows could not start its video system");
            }
        }
    }

    /// <summary>The cameras on this PC (none when there are none, or Media Foundation is missing).</summary>
    public static IReadOnlyList<CameraInfo> List()
    {
        var cameras = new List<CameraInfo>();
        try
        {
            Start();
            Check(MF.MFCreateAttributes(out IMFAttributes attributes, 1), "Listing cameras");
            Guid sourceType = MF.DevSourceType, videoCapture = MF.DevSourceVideoCapture;
            attributes.SetGUID(ref sourceType, ref videoCapture);
            Check(MF.MFEnumDeviceSources(attributes, out IntPtr devices, out int count), "Listing cameras");
            for (int i = 0; i < count; i++)
            {
                IntPtr pointer = Marshal.ReadIntPtr(devices, i * IntPtr.Size);
                var activate = (IMFActivate)Marshal.GetObjectForIUnknown(pointer);
                cameras.Add(new CameraInfo(ReadString(activate, MF.FriendlyName) ?? $"Camera {i + 1}", ReadString(activate, MF.SymbolicLink) ?? i.ToString()));
                Marshal.ReleaseComObject(activate);
                Marshal.Release(pointer);
            }

            Marshal.FreeCoTaskMem(devices);
            Marshal.ReleaseComObject(attributes);
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            // No cameras to offer.
        }

        return cameras;
    }

    private static string? ReadString(IMFAttributes attributes, Guid key)
    {
        if (attributes.GetAllocatedString(ref key, out IntPtr text, out _) < 0 || text == IntPtr.Zero)
        {
            return null;
        }

        string? value = Marshal.PtrToStringUni(text);
        Marshal.FreeCoTaskMem(text);
        return value;
    }

    /// <summary>
    /// Opens a camera at the largest picture size it offers up to 1920 × 1080. Throws
    /// <see cref="InvalidOperationException"/> with a plain reason when it cannot (in use by
    /// another program, or blocked in Windows' privacy settings).
    /// </summary>
    public static Camera Open(CameraInfo camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Start();
        Check(MF.MFCreateAttributes(out IMFAttributes attributes, 2), "Opening the camera");
        Guid sourceType = MF.DevSourceType, videoCapture = MF.DevSourceVideoCapture, link = MF.SymbolicLink;
        attributes.SetGUID(ref sourceType, ref videoCapture);
        attributes.SetString(ref link, camera.Id);
        int hr = MF.MFCreateDeviceSource(attributes, out IMFMediaSource source);
        Marshal.ReleaseComObject(attributes);
        if (hr < 0)
        {
            throw new InvalidOperationException(hr == unchecked((int)0x80070005)
                ? "Windows is not letting apps use the camera. Turn on Settings › Privacy › Camera › Let desktop apps access your camera."
                : $"The camera could not be opened (it may be in use by another program). Error 0x{hr:X8}.");
        }

        return FromSource(source, ownsSource: true);
    }

    /// <summary>Opens a video file the same way (for checking the reading without a camera).</summary>
    public static Camera OpenFile(string path)
    {
        Start();
        Check(MF.MFCreateAttributes(out IMFAttributes attributes, 1), "Opening the video");
        Guid processing = MF.SourceReaderEnableVideoProcessing;
        attributes.SetUINT32(ref processing, 1);
        Check(MF.MFCreateSourceReaderFromURL(path, attributes, out IMFSourceReader reader), "Opening the video");
        Marshal.ReleaseComObject(attributes);
        return Configure(reader, null);
    }

    private static Camera FromSource(IMFMediaSource source, bool ownsSource)
    {
        Check(MF.MFCreateAttributes(out IMFAttributes attributes, 1), "Opening the camera");
        Guid processing = MF.SourceReaderEnableVideoProcessing;
        attributes.SetUINT32(ref processing, 1);
        int hr = MF.MFCreateSourceReaderFromMediaSource(source, attributes, out IMFSourceReader reader);
        Marshal.ReleaseComObject(attributes);
        if (hr < 0)
        {
            source.Shutdown();
            throw new InvalidOperationException($"The camera's pictures could not be read. Error 0x{hr:X8}.");
        }

        return Configure(reader, ownsSource ? source : null);
    }

    private static Camera Configure(IMFSourceReader reader, IMFMediaSource? source)
    {
        // The largest size up to full HD: enough detail to measure the bars, still quick.
        Guid frameSize = MF.FrameSize;
        int best = -1;
        long bestArea = 0;
        for (int i = 0; i < 200; i++)
        {
            if (reader.GetNativeMediaType(MF.FirstVideoStream, i, out IMFMediaType native) < 0)
            {
                break;
            }

            if (native.GetUINT64(ref frameSize, out long packed) >= 0)
            {
                long w = packed >> 32, h = packed & 0xFFFFFFFF, area = w * h;
                if (area > bestArea && w <= 1920 && h <= 1080)
                {
                    best = i;
                    bestArea = area;
                }
            }

            Marshal.ReleaseComObject(native);
        }

        if (best >= 0 && reader.GetNativeMediaType(MF.FirstVideoStream, best, out IMFMediaType chosen) >= 0)
        {
            reader.SetCurrentMediaType(MF.FirstVideoStream, IntPtr.Zero, chosen);
            Marshal.ReleaseComObject(chosen);
        }

        Check(MF.MFCreateMediaType(out IMFMediaType rgb), "Setting up the picture format");
        Guid major = MF.MajorType, video = MF.MediaTypeVideo, subtype = MF.Subtype, rgb32 = MF.VideoFormatRgb32;
        rgb.SetGUID(ref major, ref video);
        rgb.SetGUID(ref subtype, ref rgb32);
        int hr = reader.SetCurrentMediaType(MF.FirstVideoStream, IntPtr.Zero, rgb);
        Marshal.ReleaseComObject(rgb);
        if (hr < 0)
        {
            Marshal.ReleaseComObject(reader);
            source?.Shutdown();
            throw new InvalidOperationException($"The camera's picture format cannot be turned into colour pictures. Error 0x{hr:X8}.");
        }

        Check(reader.GetCurrentMediaType(MF.FirstVideoStream, out IMFMediaType current), "Reading the picture format");
        current.GetUINT64(ref frameSize, out long size);
        Guid strideKey = MF.DefaultStride;
        int stride = current.GetUINT32(ref strideKey, out int reported) >= 0 ? reported : 0;
        Marshal.ReleaseComObject(current);
        return new Camera(reader, source, (int)(size >> 32), (int)(size & 0xFFFFFFFF), stride);
    }

    /// <summary>The next picture; null at the end of a video file. Blocks until the camera has one.</summary>
    public CameraFrame? Read()
    {
        IMFSourceReader reader = _reader ?? throw new ObjectDisposedException(nameof(Camera));
        for (int attempt = 0; attempt < 50; attempt++)
        {
            Check(reader.ReadSample(MF.FirstVideoStream, 0, out _, out int flags, out _, out IMFSample? sample), "Reading a picture");
            if ((flags & MF.EndOfStream) != 0)
            {
                return null;
            }

            if (sample is null)
            {
                continue; // a gap in the stream (the camera warming up)
            }

            try
            {
                return Copy(sample);
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }
        }

        return null;
    }

    private CameraFrame Copy(IMFSample sample)
    {
        Check(sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer), "Reading a picture");
        try
        {
            var pixels = new byte[Width * Height * 4];
            int rowBytes = Width * 4;
            // A 2D buffer knows which way up the rows are (RGB is often stored bottom row first).
            if (buffer is IMF2DBuffer planar && planar.Lock2D(out IntPtr scan0, out int pitch) >= 0)
            {
                try
                {
                    for (int y = 0; y < Height; y++)
                    {
                        Marshal.Copy(scan0 + (y * pitch), pixels, y * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    planar.Unlock2D();
                }
            }
            else
            {
                Check(buffer.Lock(out IntPtr data, out _, out int length), "Reading a picture");
                try
                {
                    // Decoders often pad each row (and the height) to a multiple of 16 pixels.
                    // Some decoders report the unpadded row length: then the buffer size tells the real one.
                    int stride = _stride != 0 && Math.Abs(_stride) * Height == length ? _stride : (_stride < 0 ? -1 : 1) * GuessStride(length);
                    int step = Math.Abs(stride);
                    IntPtr first = stride < 0 ? data + ((Height - 1) * step) : data;
                    for (int y = 0; y < Height && (stride < 0 ? Height - 1 - y : y) * step + rowBytes <= length; y++)
                    {
                        Marshal.Copy(first + (y * stride), pixels, y * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    buffer.Unlock();
                }
            }

            // Video RGB leaves the alpha byte empty: make every pixel opaque.
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }

            return new CameraFrame(pixels, Width, Height);
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }
    }

    /// <summary>The row length a buffer of this size has: the frame's width, padded to 2, 4â€¦ 128 pixels.</summary>
    private int GuessStride(int length)
    {
        for (int align = 1; align <= 128; align *= 2)
        {
            int width = (Width + align - 1) / align * align;
            if (length % (width * 4) == 0 && length / (width * 4) >= Height)
            {
                return width * 4;
            }
        }

        return Width * 4;
    }

    public void Dispose()
    {
        if (_reader is not null)
        {
            Marshal.ReleaseComObject(_reader);
            _reader = null;
        }

        if (_source is not null)
        {
            _source.Shutdown();
            Marshal.ReleaseComObject(_source);
            _source = null;
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException($"{what} failed (error 0x{hr:X8}).");
        }
    }

    // ---- Media Foundation --------------------------------------------------------------------

    private static class MF
    {
        public const int Version = 0x00020070;
        public const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
        public const int EndOfStream = 0x2;

        public static readonly Guid DevSourceType = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
        public static readonly Guid DevSourceVideoCapture = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
        public static readonly Guid FriendlyName = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
        public static readonly Guid SymbolicLink = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
        public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid VideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");
        public static readonly Guid SourceReaderEnableVideoProcessing = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");

        [DllImport("mfplat.dll")]
        public static extern int MFStartup(int version, int flags);

        [DllImport("mfplat.dll")]
        public static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);

        [DllImport("mfplat.dll")]
        public static extern int MFCreateMediaType(out IMFMediaType mediaType);

        [DllImport("mf.dll")]
        public static extern int MFEnumDeviceSources(IMFAttributes attributes, out IntPtr activates, out int count);

        [DllImport("mf.dll")]
        public static extern int MFCreateDeviceSource(IMFAttributes attributes, out IMFMediaSource source);

        [DllImport("mfreadwrite.dll")]
        public static extern int MFCreateSourceReaderFromMediaSource(IMFMediaSource source, IMFAttributes attributes, out IMFSourceReader reader);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        public static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes attributes, out IMFSourceReader reader);
    }

    // The interfaces, method for method in the order of the Windows headers (mfobjects.h,
    // mfidl.h, mfreadwrite.h). Every method returns its HRESULT; [PreserveSig] keeps it.

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, IntPtr buffer, int size, IntPtr length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, int size, IntPtr blobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr unknown);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
    }

    [ComImport, Guid("7FEE9E9A-4A89-47a6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFActivate
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, IntPtr buffer, int size, IntPtr length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
    }

    private static string? ReadString(IMFActivate activate, Guid key)
    {
        if (activate.GetAllocatedString(ref key, out IntPtr text, out _) < 0 || text == IntPtr.Zero)
        {
            return null;
        }

        string? value = Marshal.PtrToStringUni(text);
        Marshal.FreeCoTaskMem(text);
        return value;
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, IntPtr buffer, int size, IntPtr length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, int size, IntPtr blobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr unknown);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
    }

    [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(int streamIndex, out int selected);
        [PreserveSig] int SetStreamSelection(int streamIndex, int selected);
        [PreserveSig] int GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType mediaType);
        [PreserveSig] int GetCurrentMediaType(int streamIndex, out IMFMediaType mediaType);
        [PreserveSig] int SetCurrentMediaType(int streamIndex, IntPtr reserved, IMFMediaType mediaType);
        [PreserveSig] int SetCurrentPosition(ref Guid timeFormat, IntPtr position);
        [PreserveSig] int ReadSample(int streamIndex, int controlFlags, out int actualStreamIndex, out int streamFlags, out long timestamp, out IMFSample? sample);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);
        [PreserveSig] int GetItemType(ref Guid key, out int type);
        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
        [PreserveSig] int Compare(IMFAttributes theirs, int matchType, out int result);
        [PreserveSig] int GetUINT32(ref Guid key, out int value);
        [PreserveSig] int GetUINT64(ref Guid key, out long value);
        [PreserveSig] int GetDouble(ref Guid key, out double value);
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int GetStringLength(ref Guid key, out int length);
        [PreserveSig] int GetString(ref Guid key, IntPtr buffer, int size, IntPtr length);
        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
        [PreserveSig] int GetBlobSize(ref Guid key, out int size);
        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, int size, IntPtr blobSize);
        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size);
        [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr unknown);
        [PreserveSig] int SetItem(ref Guid key, IntPtr value);
        [PreserveSig] int DeleteItem(ref Guid key);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int SetUINT64(ref Guid key, long value);
        [PreserveSig] int SetDouble(ref Guid key, double value);
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, int size);
        [PreserveSig] int SetUnknown(ref Guid key, IntPtr unknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
        [PreserveSig] int CopyAllItems(IMFAttributes destination);
        [PreserveSig] int GetSampleFlags(out int flags);
        [PreserveSig] int SetSampleFlags(int flags);
        [PreserveSig] int GetSampleTime(out long time);
        [PreserveSig] int SetSampleTime(long time);
        [PreserveSig] int GetSampleDuration(out long duration);
        [PreserveSig] int SetSampleDuration(long duration);
        [PreserveSig] int GetBufferCount(out int count);
        [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int length);
        [PreserveSig] int SetCurrentLength(int length);
        [PreserveSig] int GetMaxLength(out int length);
    }

    [ComImport, Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMF2DBuffer
    {
        [PreserveSig] int Lock2D(out IntPtr scanline0, out int pitch);
        [PreserveSig] int Unlock2D();
    }

    [ComImport, Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaSource
    {
        // IMFMediaEventGenerator
        [PreserveSig] int GetEvent(int flags, out IntPtr mediaEvent);
        [PreserveSig] int BeginGetEvent(IntPtr callback, IntPtr state);
        [PreserveSig] int EndGetEvent(IntPtr result, out IntPtr mediaEvent);
        [PreserveSig] int QueueEvent(int type, ref Guid extendedType, int status, IntPtr value);

        // IMFMediaSource
        [PreserveSig] int GetCharacteristics(out int characteristics);
        [PreserveSig] int CreatePresentationDescriptor(out IntPtr descriptor);
        [PreserveSig] int Start(IntPtr descriptor, ref Guid timeFormat, IntPtr startPosition);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Shutdown();
    }
}
