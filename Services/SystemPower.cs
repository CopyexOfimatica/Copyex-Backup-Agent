// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Diagnostics;
using System.IO;
using CopyexBackupAgent.Models;

namespace CopyexBackupAgent.Services
{
    public static class SystemPower
    {
        private const int ERROR_SHUTDOWN_IS_SCHEDULED = 1190;

        /// <summary>
        /// Decide si hay que apagar. No se apaga si el usuario canceló la copia o si había otra copia en curso.
        /// Con "solo si no hay errores" activado, solo se apaga tras una copia perfecta.
        /// </summary>
        public static bool ShouldShutdown(BackupConfig config, BackupResult result)
        {
            if (config == null || result == null || !config.ShutdownAfterBackup)
                return false;
            if (result.Status == BackupStatus.Cancelled || result.Status == BackupStatus.Skipped)
                return false;
            if (config.ShutdownOnlyIfNoErrors && result.Status != BackupStatus.Ok)
                return false;
            return true;
        }

        /// <summary>
        /// Programa el apagado en Windows. Se programa en el propio sistema (no en un temporizador del programa)
        /// para que el equipo se apague aunque esta aplicación se cierre.
        /// </summary>
        public static bool ScheduleShutdown(int seconds)
        {
            string args = "/s /f /t " + seconds + " /d p:0:0 /c \"Copia de seguridad terminada. El equipo se apagará automáticamente.\"";
            int code = Run(args);
            if (code == ERROR_SHUTDOWN_IS_SCHEDULED)
            {
                Run("/a");
                code = Run(args);
            }
            return code == 0;
        }

        public static void ShutdownNow()
        {
            Run("/a");
            Run("/s /f /t 0 /d p:0:0");
        }

        public static void AbortShutdown()
        {
            Run("/a");
        }

        private static int Run(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(15000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch
            {
                return -1;
            }
        }
    }
}
