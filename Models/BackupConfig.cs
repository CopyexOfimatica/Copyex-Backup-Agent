// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Collections.Generic;

namespace CopyexBackupAgent.Models
{
    public enum ScheduleMode
    {
        Disabled = 0,
        Daily = 1,
        Weekly = 2
    }

    [Serializable]
    public class BackupConfig
    {
        public const string DefaultExclusions = "~$*; *.tmp; Thumbs.db; .DS_Store";

        public BackupConfig()
        {
            Sources = new List<string>();
            Destination = string.Empty;
            CopiesToKeep = 7;
            Schedule = ScheduleMode.Disabled;
            WeeklyDay = DayOfWeek.Monday;
            WeeklyDays = new List<DayOfWeek>();
            ScheduleTime = "20:00";
            ShutdownAfterBackup = false;
            ShutdownOnlyIfNoErrors = false;
            StartWithWindows = true;
            WakeToRun = true;
            RunMissedBackup = true;
            Exclusions = DefaultExclusions;
            NasUser = string.Empty;
            NasPassword = string.Empty;
        }

        public List<string> Sources { get; set; }
        public string Destination { get; set; }
        public int CopiesToKeep { get; set; }
        public ScheduleMode Schedule { get; set; }

        // Se mantiene para leer configuraciones antiguas (un solo día semanal).
        public DayOfWeek WeeklyDay { get; set; }
        public List<DayOfWeek> WeeklyDays { get; set; }

        public string ScheduleTime { get; set; }
        public bool ShutdownAfterBackup { get; set; }
        public bool ShutdownOnlyIfNoErrors { get; set; }
        public bool StartWithWindows { get; set; }
        public bool WakeToRun { get; set; }
        public bool RunMissedBackup { get; set; }
        public string Exclusions { get; set; }
        public string NasUser { get; set; }

        // Cifrada con DPAPI (solo la puede leer el mismo usuario de Windows).
        public string NasPassword { get; set; }

        public BackupConfig Clone()
        {
            BackupConfig c = (BackupConfig)MemberwiseClone();
            c.Sources = new List<string>(Sources ?? new List<string>());
            c.WeeklyDays = new List<DayOfWeek>(WeeklyDays ?? new List<DayOfWeek>());
            return c;
        }
    }
}
