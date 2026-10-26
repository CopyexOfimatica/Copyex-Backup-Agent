// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using CopyexBackupAgent.Models;

namespace CopyexBackupAgent.Services
{
    public static class ConfigService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Copyex.BackupAgent.Nas");

        public static string AppDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CopyexBackupAgent");
            }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(AppDirectory, "config.xml"); }
        }

        private static string LegacyConfigFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CopyexBackup", "config.xml");
            }
        }

        public static BackupConfig Load()
        {
            try
            {
                string file = File.Exists(ConfigFile) ? ConfigFile : (File.Exists(LegacyConfigFile) ? LegacyConfigFile : null);
                if (file == null)
                    return new BackupConfig();

                BackupConfig config = Deserialize(file);
                return Normalize(config ?? new BackupConfig());
            }
            catch (Exception)
            {
                return new BackupConfig();
            }
        }

        private static BackupConfig Deserialize(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(BackupConfig));
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return serializer.Deserialize(fs) as BackupConfig;
            }
        }

        private static BackupConfig Normalize(BackupConfig config)
        {
            if (config.Sources == null)
                config.Sources = new List<string>();
            if (config.WeeklyDays == null)
                config.WeeklyDays = new List<DayOfWeek>();
            if (config.Schedule == ScheduleMode.Weekly && config.WeeklyDays.Count == 0)
                config.WeeklyDays.Add(config.WeeklyDay);
            if (config.CopiesToKeep < 1)
                config.CopiesToKeep = 1;
            if (config.CopiesToKeep > 999)
                config.CopiesToKeep = 999;
            if (config.Exclusions == null)
                config.Exclusions = string.Empty;
            if (config.NasUser == null)
                config.NasUser = string.Empty;
            if (config.NasPassword == null)
                config.NasPassword = string.Empty;
            if (string.IsNullOrWhiteSpace(config.ScheduleTime))
                config.ScheduleTime = "20:00";
            return config;
        }

        public static void Save(BackupConfig config)
        {
            Directory.CreateDirectory(AppDirectory);
            string temp = ConfigFile + ".tmp";
            XmlSerializer serializer = new XmlSerializer(typeof(BackupConfig));

            using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(fs, config);
            }

            if (File.Exists(ConfigFile))
                File.Replace(temp, ConfigFile, null);
            else
                File.Move(temp, ConfigFile);
        }

        public static string EncryptPassword(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return string.Empty;
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        public static string DecryptPassword(string stored)
        {
            if (string.IsNullOrEmpty(stored))
                return string.Empty;
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
