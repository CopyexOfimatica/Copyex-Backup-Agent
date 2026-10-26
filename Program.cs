// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Threading;
using System.Windows.Forms;
using CopyexBackupAgent.Models;
using CopyexBackupAgent.Services;
using CopyexBackupAgent.UI;

namespace CopyexBackupAgent
{
    internal static class Program
    {
        private const string UiMutexName = @"Local\CopyexBackupAgent_UI";
        private static bool _unattended;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;

            if (HasArgument(args, "--run-backup"))
            {
                _unattended = true;
                RunScheduledBackup();
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, UiMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Ya está abierto (normalmente en la bandeja): se le pide que se muestre.
                    NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, MainForm.ShowMessageId, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.Run(new MainForm(HasArgument(args, "--startup")));
                GC.KeepAlive(mutex);
            }
        }

        private static void RunScheduledBackup()
        {
            BackupConfig config = ConfigService.Load();
            if (config.Sources == null || config.Sources.Count == 0 || string.IsNullOrWhiteSpace(config.Destination))
            {
                BackupResult skipped = new BackupResult();
                skipped.Status = BackupStatus.Failed;
                skipped.Message = "Copia programada sin orígenes o sin destino configurado.";
                HistoryService.Add(BackupRecord.From(skipped, true));
                return;
            }

            Application.Run(new BackupRunForm(config));
        }

        private static bool HasArgument(string[] args, string value)
        {
            if (args == null) return false;
            foreach (string a in args)
                if (string.Equals(a, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // En la copia desatendida no se bloquea con ventanas: el resultado ya queda en el histórico.
            if (_unattended)
                return;

            MessageBox.Show("Se ha producido un error inesperado.\r\n\r\n" + e.Exception.Message,
                "Copyex Backup Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
