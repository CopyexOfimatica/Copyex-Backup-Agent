// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace CopyexBackupAgent.Services
{
    internal sealed class FsEntry
    {
        public string Name;
        public bool IsDirectory;
        public FileAttributes Attributes;
        public long Size;
        public uint ReparseTag;

        // Solo los enlaces simbólicos y las uniones se consideran "enlaces".
        // Las carpetas de OneDrive también son puntos de reanálisis y SÍ deben copiarse.
        public bool IsLink
        {
            get
            {
                return (Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint &&
                       (ReparseTag == NativeMethods.IO_REPARSE_TAG_SYMLINK ||
                        ReparseTag == NativeMethods.IO_REPARSE_TAG_MOUNT_POINT);
            }
        }
    }

    /// <summary>
    /// Operaciones de disco con la API de Windows y prefijo \\?\, válidas para rutas de más de 260 caracteres
    /// tanto en discos locales como en recursos de red (NAS).
    /// </summary>
    internal static class FileSystem
    {
        public static string ToLong(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string value = path.Replace('/', '\\');
            if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
                return value;
            if (value.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + value.Substring(2);
            if (value.Length >= 2 && value[1] == ':')
                return @"\\?\" + value;

            try
            {
                value = Path.GetFullPath(value);
            }
            catch
            {
                return value;
            }

            return ToLong(value);
        }

        public static string FromLong(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                return path.Substring(4);
            return path;
        }

        public static string Combine(string left, string right)
        {
            if (string.IsNullOrEmpty(left)) return right;
            if (string.IsNullOrEmpty(right)) return left;
            return left.TrimEnd('\\', '/') + "\\" + right.TrimStart('\\', '/');
        }

        public static string GetLeafName(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string value = FromLong(path).Replace('/', '\\').TrimEnd('\\');
            int index = value.LastIndexOf('\\');
            return index < 0 ? value : value.Substring(index + 1);
        }

        public static string GetParent(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string value = path.TrimEnd('\\', '/');
            int index = value.LastIndexOf('\\');
            return index < 0 ? string.Empty : value.Substring(0, index);
        }

        /// <summary>Devuelve \\servidor\recurso para una ruta UNC, o null si no lo es.</summary>
        public static string GetUncShareRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string value = FromLong(path).Replace('/', '\\');
            if (!value.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            string[] parts = value.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            return @"\\" + parts[0] + @"\" + parts[1];
        }

        private static bool IsRoot(string path)
        {
            string value = FromLong(path).TrimEnd('\\');
            if (value.Length == 2 && value[1] == ':') return true;
            string share = GetUncShareRoot(value);
            return share != null && string.Equals(share, value, StringComparison.OrdinalIgnoreCase);
        }

        public static uint GetAttributes(string path)
        {
            return NativeMethods.GetFileAttributes(ToLong(path));
        }

        public static bool DirectoryExists(string path)
        {
            uint a = GetAttributes(path);
            return a != NativeMethods.INVALID_FILE_ATTRIBUTES && (a & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
        }

        public static bool FileExists(string path)
        {
            uint a = GetAttributes(path);
            return a != NativeMethods.INVALID_FILE_ATTRIBUTES && (a & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) == 0;
        }

        public static FsEntry GetEntry(string path)
        {
            NativeMethods.WIN32_FIND_DATA fd;
            IntPtr h = NativeMethods.FindFirstFileEx(ToLong(path.TrimEnd('\\')), 1, out fd, 0, IntPtr.Zero, 0);
            if (h == NativeMethods.INVALID_HANDLE_VALUE)
                return null;
            NativeMethods.FindClose(h);
            return ToEntry(fd);
        }

        public static List<FsEntry> List(string directory)
        {
            List<FsEntry> result = new List<FsEntry>();
            NativeMethods.WIN32_FIND_DATA fd;
            string pattern = Combine(ToLong(directory), "*");
            IntPtr h = NativeMethods.FindFirstFileEx(pattern, 1, out fd, 0, IntPtr.Zero, 2);
            if (h == NativeMethods.INVALID_HANDLE_VALUE)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == NativeMethods.ERROR_FILE_NOT_FOUND || err == NativeMethods.ERROR_NO_MORE_FILES)
                    return result;
                throw new Win32Exception(err);
            }

            try
            {
                do
                {
                    if (fd.cFileName == "." || fd.cFileName == "..")
                        continue;
                    result.Add(ToEntry(fd));
                }
                while (NativeMethods.FindNextFile(h, out fd));

                int last = Marshal.GetLastWin32Error();
                if (last != NativeMethods.ERROR_NO_MORE_FILES && last != 0)
                    throw new Win32Exception(last);
            }
            finally
            {
                NativeMethods.FindClose(h);
            }

            return result;
        }

        private static FsEntry ToEntry(NativeMethods.WIN32_FIND_DATA fd)
        {
            FsEntry e = new FsEntry();
            e.Name = fd.cFileName;
            e.Attributes = (FileAttributes)fd.dwFileAttributes;
            e.IsDirectory = (fd.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
            e.Size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
            e.ReparseTag = (e.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint ? fd.dwReserved0 : 0;
            return e;
        }

        public static void CreateDirectoryTree(string path)
        {
            if (string.IsNullOrEmpty(path) || DirectoryExists(path))
                return;

            if (NativeMethods.CreateDirectory(ToLong(path), IntPtr.Zero))
                return;

            int err = Marshal.GetLastWin32Error();
            if (err == NativeMethods.ERROR_ALREADY_EXISTS)
                return;

            if (err == NativeMethods.ERROR_PATH_NOT_FOUND && !IsRoot(path))
            {
                string parent = GetParent(path);
                if (!string.IsNullOrEmpty(parent) && !IsRoot(parent))
                {
                    CreateDirectoryTree(parent);
                    if (NativeMethods.CreateDirectory(ToLong(path), IntPtr.Zero))
                        return;
                    err = Marshal.GetLastWin32Error();
                    if (err == NativeMethods.ERROR_ALREADY_EXISTS)
                        return;
                }
            }

            throw new Win32Exception(err);
        }

        public static void MoveDirectory(string source, string target)
        {
            if (!NativeMethods.MoveFileEx(ToLong(source), ToLong(target), 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>
        /// Borra una carpeta completa. Quita antes los atributos de solo lectura/oculto/sistema,
        /// que son los que hacían fallar Directory.Delete y dejaban copias antiguas sin borrar.
        /// </summary>
        public static void DeleteTree(string path)
        {
            if (!DirectoryExists(path))
                return;

            string firstError = null;
            DeleteTreeCore(path, ref firstError);

            if (DirectoryExists(path))
            {
                // Segundo intento: a veces el antivirus o el NAS liberan los ficheros con un pequeño retraso.
                System.Threading.Thread.Sleep(1500);
                DeleteTreeCore(path, ref firstError);
            }

            if (DirectoryExists(path))
                throw new IOException(firstError ?? "No se pudo eliminar la carpeta.");
        }

        private static void DeleteTreeCore(string directory, ref string firstError)
        {
            List<FsEntry> entries;
            try
            {
                entries = List(directory);
            }
            catch (Exception ex)
            {
                if (firstError == null) firstError = ex.Message;
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                FsEntry e = entries[i];
                string full = Combine(directory, e.Name);
                string lp = ToLong(full);

                if (e.IsDirectory)
                {
                    if (e.IsLink)
                    {
                        NativeMethods.SetFileAttributes(lp, NativeMethods.FILE_ATTRIBUTE_NORMAL);
                        if (!NativeMethods.RemoveDirectory(lp) && firstError == null)
                            firstError = Win32Message(Marshal.GetLastWin32Error()) + " (" + FromLong(full) + ")";
                    }
                    else
                    {
                        DeleteTreeCore(full, ref firstError);
                    }
                }
                else
                {
                    const FileAttributes blocking = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
                    if ((e.Attributes & blocking) != 0)
                        NativeMethods.SetFileAttributes(lp, NativeMethods.FILE_ATTRIBUTE_NORMAL);

                    if (!NativeMethods.DeleteFile(lp) && firstError == null)
                        firstError = Win32Message(Marshal.GetLastWin32Error()) + " (" + FromLong(full) + ")";
                }
            }

            string dl = ToLong(directory);
            NativeMethods.SetFileAttributes(dl, NativeMethods.FILE_ATTRIBUTE_NORMAL);
            if (!NativeMethods.RemoveDirectory(dl) && firstError == null)
                firstError = Win32Message(Marshal.GetLastWin32Error()) + " (" + FromLong(directory) + ")";
        }

        public static bool TryGetFreeSpace(string path, out long freeBytes, out long totalBytes)
        {
            freeBytes = 0;
            totalBytes = 0;
            try
            {
                string dir = FromLong(path).TrimEnd('\\') + "\\";
                ulong free, total, totalFree;
                if (!NativeMethods.GetDiskFreeSpaceEx(dir, out free, out total, out totalFree))
                    return false;
                freeBytes = (long)free;
                totalBytes = (long)total;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string Win32Message(int code)
        {
            return new Win32Exception(code).Message;
        }

        public static string FormatBytes(long bytes)
        {
            double value = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }
            return value.ToString(unit == 0 ? "N0" : "N1") + " " + units[unit];
        }
    }
}
