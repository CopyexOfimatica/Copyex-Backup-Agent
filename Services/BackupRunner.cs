// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.ComponentModel;
using System.Threading;
using CopyexBackupAgent.Models;

namespace CopyexBackupAgent.Services
{
    /// <summary>
    /// Ejecuta una copia en segundo plano, impide que haya dos copias a la vez en el mismo equipo
    /// (por ejemplo, una manual y otra programada) y anota el resultado en el histórico.
    /// </summary>
    public class BackupRunner : IDisposable
    {
        public const string BackupMutexName = @"Local\CopyexBackupAgent_Backup";

        private readonly BackgroundWorker _worker;

        public event Action<BackupProgress> Progress;
        public event Action<BackupLogEntry> Log;
        public event Action<BackupResult> Completed;

        public BackupRunner()
        {
            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += Worker_DoWork;
            _worker.ProgressChanged += Worker_ProgressChanged;
            _worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
        }

        public bool IsBusy
        {
            get { return _worker.IsBusy; }
        }

        public bool CancelRequested
        {
            get { return _worker.CancellationPending; }
        }

        public void Start(BackupConfig config, bool scheduled)
        {
            _worker.RunWorkerAsync(new object[] { config.Clone(), scheduled });
        }

        public void Cancel()
        {
            if (_worker.IsBusy)
                _worker.CancelAsync();
        }

        /// <summary>Indica si hay una copia en marcha en otro proceso (por ejemplo, la programada).</summary>
        public static bool IsBackupRunningElsewhere()
        {
            try
            {
                using (Mutex m = new Mutex(false, BackupMutexName))
                {
                    bool owned;
                    try
                    {
                        owned = m.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        owned = true;
                    }

                    if (owned)
                    {
                        m.ReleaseMutex();
                        return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = (BackgroundWorker)sender;
            object[] args = (object[])e.Argument;
            BackupConfig config = (BackupConfig)args[0];
            bool scheduled = (bool)args[1];
            BackupResult result;

            using (Mutex mutex = new Mutex(false, BackupMutexName))
            {
                bool owned;
                try
                {
                    owned = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }

                if (!owned)
                {
                    result = new BackupResult();
                    result.Status = BackupStatus.Skipped;
                    result.Message = "Ya hay otra copia de seguridad en curso en este equipo.";
                }
                else
                {
                    try
                    {
                        BackupService service = new BackupService();
                        result = service.Run(
                            config,
                            scheduled,
                            delegate(BackupProgress p) { worker.ReportProgress(0, p); },
                            delegate { return worker.CancellationPending; },
                            delegate(BackupLogEntry entry) { worker.ReportProgress(0, entry); });
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }

            HistoryService.Add(BackupRecord.From(result, scheduled));
            e.Result = result;
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            BackupProgress p = e.UserState as BackupProgress;
            if (p != null)
            {
                if (Progress != null) Progress(p);
                return;
            }

            BackupLogEntry entry = e.UserState as BackupLogEntry;
            if (entry != null && Log != null)
                Log(entry);
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            BackupResult result = e.Error == null ? e.Result as BackupResult : null;
            if (result == null)
            {
                result = new BackupResult();
                result.Status = BackupStatus.Failed;
                result.Message = "Error inesperado durante la copia" + (e.Error != null ? ": " + e.Error.Message : ".");
            }

            if (Completed != null)
                Completed(result);
        }

        public void Dispose()
        {
            _worker.Dispose();
        }
    }
}
