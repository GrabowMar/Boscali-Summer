using System;
using System.Runtime.InteropServices;

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>The Windows open / save file dialog (comdlg32; Proton's Wine has it too). Modal: the game waits while it is open.
    /// Null when cancelled or when the dialog cannot run.</summary>
    internal static class FileDialog
    {
        private const string ImageFilter = "Images (*.png;*.jpg;*.jpeg)\0*.png;*.jpg;*.jpeg\0All files\0*.*\0\0";

        public static string OpenImage(string title, string folder) => Run(title, folder, null, save: false);

        public static string SavePng(string title, string folder, string fileName) => Run(title, folder, fileName, save: true);

        private static string Run(string title, string folder, string fileName, bool save)
        {
            const int Chars = 1024;
            IntPtr buffer = Marshal.AllocHGlobal(Chars * 2);
            try
            {
                byte[] zero = new byte[Chars * 2];
                Marshal.Copy(zero, 0, buffer, zero.Length);
                if (!string.IsNullOrEmpty(fileName))
                {
                    char[] name = fileName.ToCharArray();
                    Marshal.Copy(name, 0, buffer, Math.Min(name.Length, Chars - 1));
                }
                var ofn = new OpenFileName
                {
                    lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = ImageFilter,
                    nFilterIndex = 1,
                    lpstrFile = buffer,
                    nMaxFile = Chars,
                    lpstrInitialDir = folder,
                    lpstrTitle = title,
                    lpstrDefExt = save ? "png" : null,
                    Flags = Explorer | NoChangeDir | PathMustExist | (save ? OverwritePrompt : FileMustExist),
                };
                bool ok = save ? GetSaveFileNameW(ref ofn) : GetOpenFileNameW(ref ofn);
                return ok ? Marshal.PtrToStringUni(buffer) : null;
            }
            catch (Exception e)
            {
                WingLog.Logger.LogWarning("[Pilots] the file dialog could not open: " + e.Message);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private const int OverwritePrompt = 0x2, NoChangeDir = 0x8, PathMustExist = 0x800, FileMustExist = 0x1000, Explorer = 0x80000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetSaveFileNameW(ref OpenFileName ofn);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
    }
}
