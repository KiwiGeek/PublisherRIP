using ComTypes = System.Runtime.InteropServices.ComTypes;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace PublisherRip.App.Services;

internal static class OutlookVirtualFileReader
{
    private const string FileGroupDescriptorUnicode = "FileGroupDescriptorW";
    private const string FileGroupDescriptorAnsi = "FileGroupDescriptor";
    private const string FileContents = "FileContents";

    public static bool CanRead(IDataObject dataObject)
    {
        return dataObject.GetDataPresent(FileGroupDescriptorUnicode)
            || dataObject.GetDataPresent(FileGroupDescriptorAnsi);
    }

    public static IReadOnlyList<VirtualFileData> ReadFiles(IDataObject dataObject)
    {
        var descriptorFormat = dataObject.GetDataPresent(FileGroupDescriptorUnicode)
            ? FileGroupDescriptorUnicode
            : FileGroupDescriptorAnsi;

        var descriptorPayload = ReadDescriptorPayload(dataObject, descriptorFormat);
        var fileNames = descriptorFormat == FileGroupDescriptorUnicode
            ? ParseUnicodeFileNames(descriptorPayload)
            : ParseAnsiFileNames(descriptorPayload);

        if (dataObject is not ComTypes.IDataObject comDataObject)
        {
            throw new InvalidOperationException("The dropped Outlook data is not exposed as a COM data object.");
        }

        var results = new List<VirtualFileData>(fileNames.Count);
        for (var index = 0; index < fileNames.Count; index++)
        {
            var contents = ReadFileContents(comDataObject, index);
            results.Add(new VirtualFileData(fileNames[index], contents));
        }

        return results;
    }

    private static byte[] ReadDescriptorPayload(IDataObject dataObject, string format)
    {
        return dataObject.GetData(format) switch
        {
            MemoryStream memoryStream => memoryStream.ToArray(),
            byte[] bytes => bytes,
            _ => throw new InvalidOperationException("The Outlook drop did not include a readable file descriptor payload.")
        };
    }

    private static List<string> ParseUnicodeFileNames(byte[] payload)
    {
        var count = BitConverter.ToInt32(payload, 0);
        var fileNames = new List<string>(count);
        var descriptorSize = Marshal.SizeOf<FileDescriptorUnicode>();
        var handle = GCHandle.Alloc(payload, GCHandleType.Pinned);

        try
        {
            var basePointer = handle.AddrOfPinnedObject();
            for (var index = 0; index < count; index++)
            {
                var pointer = IntPtr.Add(basePointer, sizeof(int) + (descriptorSize * index));
                var descriptor = Marshal.PtrToStructure<FileDescriptorUnicode>(pointer);
                fileNames.Add(Path.GetFileName(descriptor.FileName));
            }
        }
        finally
        {
            handle.Free();
        }

        return fileNames;
    }

    private static List<string> ParseAnsiFileNames(byte[] payload)
    {
        var count = BitConverter.ToInt32(payload, 0);
        var fileNames = new List<string>(count);
        var descriptorSize = Marshal.SizeOf<FileDescriptorAnsi>();
        var handle = GCHandle.Alloc(payload, GCHandleType.Pinned);

        try
        {
            var basePointer = handle.AddrOfPinnedObject();
            for (var index = 0; index < count; index++)
            {
                var pointer = IntPtr.Add(basePointer, sizeof(int) + (descriptorSize * index));
                var descriptor = Marshal.PtrToStructure<FileDescriptorAnsi>(pointer);
                fileNames.Add(Path.GetFileName(descriptor.FileName));
            }
        }
        finally
        {
            handle.Free();
        }

        return fileNames;
    }

    private static byte[] ReadFileContents(ComTypes.IDataObject dataObject, int index)
    {
        var fileContentsFormat = RegisterClipboardFormat(FileContents);
        var format = new ComTypes.FORMATETC
        {
            cfFormat = (short)fileContentsFormat,
            dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            tymed = ComTypes.TYMED.TYMED_ISTREAM | ComTypes.TYMED.TYMED_HGLOBAL
        };

        dataObject.GetData(ref format, out var medium);

        try
        {
            return medium.tymed switch
            {
                ComTypes.TYMED.TYMED_ISTREAM => ReadFromComStream((ComTypes.IStream)Marshal.GetObjectForIUnknown(medium.unionmember)),
                ComTypes.TYMED.TYMED_HGLOBAL => ReadFromGlobalHandle(medium.unionmember),
                _ => throw new InvalidOperationException($"Unsupported Outlook content medium: {medium.tymed}.")
            };
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static byte[] ReadFromComStream(ComTypes.IStream stream)
    {
        stream.Stat(out var stat, 1);
        var length = checked((int)stat.cbSize);
        var buffer = new byte[length];
        stream.Read(buffer, length, IntPtr.Zero);
        return buffer;
    }

    private static byte[] ReadFromGlobalHandle(IntPtr handle)
    {
        var size = checked((int)GlobalSize(handle).ToInt64());
        var buffer = new byte[size];
        var pointer = GlobalLock(handle);

        try
        {
            Marshal.Copy(pointer, buffer, 0, size);
            return buffer;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                GlobalUnlock(handle);
            }
        }
    }

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalSize(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileDescriptorUnicode
    {
        public uint Flags;
        public Guid ClassId;
        public SizeL Size;
        public PointL Point;
        public uint FileAttributes;
        public ComTypes.FILETIME CreationTime;
        public ComTypes.FILETIME LastAccessTime;
        public ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct FileDescriptorAnsi
    {
        public uint Flags;
        public Guid ClassId;
        public SizeL Size;
        public PointL Point;
        public uint FileAttributes;
        public ComTypes.FILETIME CreationTime;
        public ComTypes.FILETIME LastAccessTime;
        public ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }
}

internal sealed record VirtualFileData(string FileName, byte[] Content);
