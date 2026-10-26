// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CopyexBackupAgent.Services
{
    internal static class NativeMethods
    {
        public const uint PROGRESS_CONTINUE = 0;
        public const uint PROGRESS_CANCEL = 1;
        public const uint COPY_FILE_ALLOW_DECRYPTED_DESTINATION = 0x00000008;

        public const int ERROR_FILE_NOT_FOUND = 2;
        public const int ERROR_PATH_NOT_FOUND = 3;
        public const int ERROR_NO_MORE_FILES = 18;
        public const int ERROR_ALREADY_EXISTS = 183;
        public const int ERROR_REQUEST_ABORTED = 1235;

        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        public const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
        public const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;

        public const uint ES_CONTINUOUS = 0x80000000;
        public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        public const uint ES_AWAYMODE_REQUIRED = 0x00000040;

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        public delegate uint CopyProgressRoutine(
            long totalFileSize,
            long totalBytesTransferred,
            long streamSize,
            long streamBytesTransferred,
            uint streamNumber,
            uint callbackReason,
            IntPtr sourceFile,
            IntPtr destinationFile,
            IntPtr data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CopyFileEx(
            string existingFileName,
            string newFileName,
            CopyProgressRoutine progressRoutine,
            IntPtr data,
            ref int cancel,
            uint copyFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        // infoLevel 1 = FindExInfoBasic, additionalFlags 2 = FIND_FIRST_EX_LARGE_FETCH
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileEx(
            string fileName,
            int infoLevel,
            out WIN32_FIND_DATA findData,
            int searchOp,
            IntPtr searchFilter,
            int additionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindNextFile(IntPtr findFile, out WIN32_FIND_DATA findData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindClose(IntPtr findFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateDirectory(string path, IntPtr securityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveDirectory(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteFile(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetFileAttributes(string path, uint attributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFileAttributes(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(
            string directoryName,
            out ulong freeBytesAvailable,
            out ulong totalNumberOfBytes,
            out ulong totalNumberOfFreeBytes);

        [DllImport("kernel32.dll")]
        public static extern uint SetThreadExecutionState(uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetAddConnection2(NETRESOURCE netResource, string password, string username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
