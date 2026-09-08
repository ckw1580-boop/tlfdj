using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ElectricalSim
{
    public static class WindowsFileDialog
    {
        public static string OpenCc3d(string initialDirectory)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFilePanel("打开接线", initialDirectory, "cc3d");
#elif UNITY_STANDALONE_WIN
            return ShowDialog(false, initialDirectory, string.Empty);
#else
            throw new PlatformNotSupportedException("当前平台不支持本地文件选择。");
#endif
        }

        public static string SaveCc3d(string initialDirectory, string fileName = "接线.cc3d")
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.SaveFilePanel("保存接线", initialDirectory, fileName, "cc3d");
#elif UNITY_STANDALONE_WIN
            return ShowDialog(true, initialDirectory, fileName);
#else
            throw new PlatformNotSupportedException("当前平台不支持本地文件选择。");
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr dlgOwner;
            public IntPtr instance;
            public IntPtr filter;
            public IntPtr customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public IntPtr initialDir;
            public IntPtr title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public IntPtr defExt;
            public IntPtr custData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reservedPtr;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("Comdlg32.dll", EntryPoint = "GetOpenFileNameW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName(ref OpenFileName openFileName);

        [DllImport("Comdlg32.dll", EntryPoint = "GetSaveFileNameW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSaveFileName(ref OpenFileName openFileName);

        [DllImport("Comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static string ShowDialog(bool save, string initialDirectory, string fileName)
        {
            // OPENFILENAME must be blittable. Unity's Mono marshaler cannot
            // reliably marshal StringBuilder fields embedded in this structure.
            var allocations = new List<IntPtr>();
            IntPtr Allocate(string value)
            {
                var pointer = Marshal.StringToHGlobalUni(value);
                allocations.Add(pointer);
                return pointer;
            }
            try
            {
                const int capacity = 4096;
                if (fileName.Length >= capacity) throw new PathTooLongException("文件名过长。");
                var data = new OpenFileName
                {
                    structSize = Marshal.SizeOf(typeof(OpenFileName)),
                    dlgOwner = GetActiveWindow(),
                    filter = Allocate("CC3D 接线 (*.cc3d)\0*.cc3d\0所有文件 (*.*)\0*.*\0\0"),
                    filterIndex = 1,
                    file = Allocate(fileName.PadRight(capacity, '\0')),
                    maxFile = capacity,
                    initialDir = Allocate(Path.GetFullPath(Directory.Exists(initialDirectory) ? initialDirectory : Application.persistentDataPath)),
                    title = Allocate(save ? "保存接线" : "打开接线"),
                    defExt = Allocate("cc3d"),
                    flags = 0x00080000 | 0x00000800 | 0x00000008 | (save ? 0x00000002 : 0x00001000)
                };
                var success = save ? GetSaveFileName(ref data) : GetOpenFileName(ref data);
                if (!success)
                {
                    var error = CommDlgExtendedError();
                    if (error != 0) throw new IOException($"无法显示文件窗口（错误码 0x{error:X}）。");
                }
                return success ? Marshal.PtrToStringUni(data.file) : string.Empty;
            }
            finally
            {
                foreach (var allocation in allocations) Marshal.FreeHGlobal(allocation);
            }
        }
#endif
    }
}
