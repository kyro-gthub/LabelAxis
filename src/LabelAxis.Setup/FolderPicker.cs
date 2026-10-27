using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LabelAxis.Setup;

/// <summary>Windows' own "choose a folder" dialog (the one File Explorer uses), opened in <c>start</c>.</summary>
internal static class FolderPicker
{
    private const uint PickFolders = 0x20, ForceFileSystem = 0x40, PathMustExist = 0x800;
    private const uint FileSystemPath = 0x80058000;
    private const int Cancelled = unchecked((int)0x800704C7);

    public static string? Pick(Window owner, string title, string start)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        try
        {
            dialog.SetOptions(PickFolders | ForceFileSystem | PathMustExist);
            dialog.SetTitle(title);
            string? existing = Existing(start);
            if (existing is not null && NativeMethods.SHCreateItemFromParsingName(existing, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem folder) == 0)
            {
                dialog.SetFolder(folder);
                Marshal.ReleaseComObject(folder);
            }

            int result = dialog.Show(new WindowInteropHelper(owner).Handle);
            if (result == Cancelled || result != 0)
            {
                return null;
            }

            dialog.GetResult(out IShellItem item);
            try
            {
                item.GetDisplayName(FileSystemPath, out IntPtr name);
                try
                {
                    return Marshal.PtrToStringUni(name);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    /// <summary>The nearest folder of <paramref name="path"/> that exists (LabelAxis's own is not there yet).</summary>
    private static string? Existing(string path)
    {
        try
        {
            string? folder = System.IO.Path.GetFullPath(path);
            while (folder is not null && !System.IO.Directory.Exists(folder))
            {
                folder = System.IO.Path.GetDirectoryName(folder);
            }

            return folder;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint count, IntPtr specs);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem item);

        void SetFolder(IShellItem item);

        void GetFolder(out IShellItem item);

        void GetCurrentSelection(out IShellItem item);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem item);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid id, out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint form, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid id, out IShellItem item);
    }
}
