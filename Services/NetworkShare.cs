// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.IO;
using System.Text;

namespace CopyexBackupAgent.Services
{
    internal static class NetworkShare
    {
        private const int RESOURCETYPE_DISK = 1;
        private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
        private const int ERROR_ALREADY_ASSIGNED = 85;

        /// <summary>
        /// Inicia sesión en el recurso del NAS con usuario y contraseña.
        /// Devuelve null si ha ido bien o el texto del problema.
        /// </summary>
        public static string Connect(string path, string user, string password)
        {
            string root = FileSystem.GetUncShareRoot(path);
            if (root == null)
                return "Las credenciales solo se usan con rutas de red del tipo \\\\servidor\\carpeta.";

            NativeMethods.NETRESOURCE nr = new NativeMethods.NETRESOURCE();
            nr.dwType = RESOURCETYPE_DISK;
            nr.lpRemoteName = root;

            int code = NativeMethods.WNetAddConnection2(nr, password ?? string.Empty, user, 0);
            if (code == 0 || code == ERROR_SESSION_CREDENTIAL_CONFLICT || code == ERROR_ALREADY_ASSIGNED)
                return null;

            return FileSystem.Win32Message(code);
        }

        /// <summary>
        /// Convierte una unidad de red mapeada (Z:\Copias) en su ruta UNC (\\nas\copias\Copias).
        /// Las tareas programadas no siempre ven las unidades mapeadas; la ruta UNC funciona siempre.
        /// </summary>
        public static string ToUncPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || path.Length < 2 || path[1] != ':')
                    return path;

                string drive = path.Substring(0, 2);
                DriveInfo info = new DriveInfo(drive);
                if (info.DriveType != DriveType.Network)
                    return path;

                StringBuilder sb = new StringBuilder(512);
                int len = sb.Capacity;
                if (NativeMethods.WNetGetConnection(drive, sb, ref len) != 0)
                    return path;

                string rest = path.Length > 2 ? path.Substring(2).TrimStart('\\') : string.Empty;
                return rest.Length == 0 ? sb.ToString() : FileSystem.Combine(sb.ToString(), rest);
            }
            catch
            {
                return path;
            }
        }
    }
}
