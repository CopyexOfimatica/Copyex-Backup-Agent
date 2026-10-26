// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CopyexBackupAgent.Services
{
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CopyexBackupAgent";

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --startup"; }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    object value = key.GetValue(ValueName);
                    return value != null && !string.IsNullOrWhiteSpace(Convert.ToString(value));
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Apply(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                        throw new InvalidOperationException("No se pudo abrir la clave de inicio de Windows.");

                    if (enabled)
                        key.SetValue(ValueName, Command, RegistryValueKind.String);
                    else
                        key.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("No se pudo cambiar el inicio con Windows. " + ex.Message, ex);
            }
        }

        /// <summary>Si el programa se ha movido o reinstalado, actualiza la ruta guardada en el registro.</summary>
        public static void RefreshPathIfEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    string current = Convert.ToString(key.GetValue(ValueName));
                    if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(ValueName, Command, RegistryValueKind.String);
                }
            }
            catch
            {
            }
        }
    }
}
