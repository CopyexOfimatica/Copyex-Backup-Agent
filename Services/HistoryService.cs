// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml.Serialization;
using CopyexBackupAgent.Models;

namespace CopyexBackupAgent.Services
{
    /// <summary>Guarda un resumen de las últimas ejecuciones (no el registro detallado).</summary>
    public static class HistoryService
    {
        private const int MaxRecords = 100;
        private static readonly object Sync = new object();

        private static string HistoryFile
        {
            get { return Path.Combine(ConfigService.AppDirectory, "history.xml"); }
        }

        public static List<BackupRecord> Load()
        {
            lock (Sync)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (!File.Exists(HistoryFile))
                            return new List<BackupRecord>();

                        XmlSerializer serializer = new XmlSerializer(typeof(List<BackupRecord>));
                        using (FileStream fs = new FileStream(HistoryFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            List<BackupRecord> list = serializer.Deserialize(fs) as List<BackupRecord>;
                            return list ?? new List<BackupRecord>();
                        }
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(200);
                    }
                    catch
                    {
                        return new List<BackupRecord>();
                    }
                }
                return new List<BackupRecord>();
            }
        }

        public static BackupRecord GetLast()
        {
            List<BackupRecord> list = Load();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Status != BackupStatus.Skipped)
                    return list[i];
            }
            return null;
        }

        public static void Add(BackupRecord record)
        {
            lock (Sync)
            {
                try
                {
                    List<BackupRecord> list = Load();
                    list.Insert(0, record);
                    if (list.Count > MaxRecords)
                        list.RemoveRange(MaxRecords, list.Count - MaxRecords);

                    Directory.CreateDirectory(ConfigService.AppDirectory);
                    string temp = HistoryFile + ".tmp";
                    XmlSerializer serializer = new XmlSerializer(typeof(List<BackupRecord>));
                    using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        serializer.Serialize(fs, list);
                    }

                    if (File.Exists(HistoryFile))
                        File.Replace(temp, HistoryFile, null);
                    else
                        File.Move(temp, HistoryFile);
                }
                catch
                {
                    // El histórico es informativo: nunca debe afectar a la copia.
                }
            }
        }
    }
}
