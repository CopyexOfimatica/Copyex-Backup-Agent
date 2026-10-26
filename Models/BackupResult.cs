// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;

namespace CopyexBackupAgent.Models
{
    public enum BackupLogLevel
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }

    public enum BackupStatus
    {
        Ok = 0,
        CompletedWithErrors = 1,
        Failed = 2,
        Cancelled = 3,
        Skipped = 4
    }

    public enum BackupPhase
    {
        Preparing = 0,
        Scanning = 1,
        Copying = 2,
        Finishing = 3
    }

    public class BackupLogEntry
    {
        public BackupLogEntry()
        {
            Timestamp = DateTime.Now;
            Message = string.Empty;
        }

        public DateTime Timestamp { get; set; }
        public BackupLogLevel Level { get; set; }
        public string Message { get; set; }
    }

    public class BackupProgress
    {
        public BackupProgress()
        {
            CurrentItem = string.Empty;
        }

        public BackupPhase Phase { get; set; }
        public string CurrentItem { get; set; }
        public long FilesDone { get; set; }
        public long FilesTotal { get; set; }
        public long BytesDone { get; set; }
        public long BytesTotal { get; set; }
        public long BytesPerSecond { get; set; }
        public TimeSpan Remaining { get; set; }
        public bool HasRemaining { get; set; }

        public double Fraction
        {
            get
            {
                if (BytesTotal > 0)
                    return Math.Min(1.0, (double)BytesDone / BytesTotal);
                if (FilesTotal > 0)
                    return Math.Min(1.0, (double)FilesDone / FilesTotal);
                return 0;
            }
        }
    }

    public class BackupResult
    {
        public BackupResult()
        {
            Message = string.Empty;
            BackupFolder = string.Empty;
            StartTime = DateTime.Now;
            EndTime = DateTime.Now;
        }

        public BackupStatus Status { get; set; }
        public string Message { get; set; }
        public long FilesCopied { get; set; }
        public long FilesFailed { get; set; }
        public long BytesCopied { get; set; }
        public int ErrorsCount { get; set; }
        public int DeletedOldBackups { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string BackupFolder { get; set; }

        public bool Success
        {
            get { return Status == BackupStatus.Ok; }
        }
    }

    public class SchedulerResult
    {
        public SchedulerResult()
        {
            Message = string.Empty;
        }

        public bool Success { get; set; }
        public string Message { get; set; }
    }

    [Serializable]
    public class BackupRecord
    {
        public BackupRecord()
        {
            Message = string.Empty;
            Folder = string.Empty;
        }

        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public BackupStatus Status { get; set; }
        public long Files { get; set; }
        public long Bytes { get; set; }
        public int Errors { get; set; }
        public string Message { get; set; }
        public string Folder { get; set; }
        public bool Scheduled { get; set; }

        public static BackupRecord From(BackupResult r, bool scheduled)
        {
            BackupRecord rec = new BackupRecord();
            rec.Start = r.StartTime;
            rec.End = r.EndTime;
            rec.Status = r.Status;
            rec.Files = r.FilesCopied;
            rec.Bytes = r.BytesCopied;
            rec.Errors = r.ErrorsCount;
            rec.Message = r.Message ?? string.Empty;
            rec.Folder = r.BackupFolder ?? string.Empty;
            rec.Scheduled = scheduled;
            return rec;
        }
    }

    public class BackupFolderInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime Date { get; set; }
        public string Machine { get; set; }
        public long Files { get; set; }
        public long Bytes { get; set; }
        public int Errors { get; set; }
        public bool HasMarker { get; set; }
        public bool IsOwn { get; set; }
    }
}
