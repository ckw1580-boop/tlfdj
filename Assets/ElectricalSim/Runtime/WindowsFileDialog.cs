using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ElectricalSim
{
    public static class WindowsFileDialog
    {
        public static string OpenCc3d(string initialDirectory)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFilePanel("打开 CC3D 场景", initialDirectory, "cc3d");
#elif UNITY_STANDALONE_WIN
            return ShowDialog(false, initialDirectory);
#else
            return Path.Combine(initialDirectory, "scene.cc3d");
#endif
        }

        public static string SaveCc3d(string initialDirectory)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.SaveFilePanel("导出 CC3D 场景", initialDirectory, "scene", "cc3d");
#elif UNITY_STANDALONE_WIN
            return ShowDialog(true, initialDirectory);
#else
            return Path.Combine(initialDirectory, "scene.cc3d");
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr dlgOwner;
            public IntPtr instance;
            public string filter;
            public string customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public StringBuilder file;
            public int maxFile;
            public StringBuilder fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr custData;
            public IntPtr hook;
            public string templateName;
            public IntPtr reservedPtr;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("Comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName(ref OpenFileName openFileName);

        [DllImport("Comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSaveFileName(ref OpenFileName openFileName);

        private static string ShowDialog(bool save, string initialDirectory)
        {
            var buffer = new StringBuilder(4096);
            var titleBuffer = new StringBuilder(512);
            var data = new OpenFileName
            {
                structSize = Marshal.SizeOf(typeof(OpenFileName)),
                filter = "CC3D 场景 (*.cc3d)\0*.cc3d\0所有文件 (*.*)\0*.*\0",
                file = buffer,
                maxFile = buffer.Capacity,
                fileTitle = titleBuffer,
                maxFileTitle = titleBuffer.Capacity,
                initialDir = Directory.Exists(initialDirectory) ? initialDirectory : Application.persistentDataPath,
                title = save ? "导出 CC3D 场景" : "打开 CC3D 场景",
                defExt = "cc3d",
                flags = 0x00080000 | 0x00000800 | (save ? 0x00000002 : 0x00001000)
            };
            var success = save ? GetSaveFileName(ref data) : GetOpenFileName(ref data);
            return success ? buffer.ToString() : string.Empty;
        }
#endif
    }
}
