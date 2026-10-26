// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using CopyexBackupAgent.Models;

namespace CopyexBackupAgent.Services
{
    /// <summary>
    /// Crea la tarea en el Programador de tareas de Windows a partir de un XML, para poder fijar opciones
    /// que schtasks no permite por línea de comandos:
    ///  - se ejecuta también con el portátil a batería,
    ///  - sin límite de 72 horas,
    ///  - despierta el equipo si está en suspensión,
    ///  - si el equipo estaba apagado a la hora, se ejecuta al encenderlo.
    /// </summary>
    public static class SchedulerService
    {
        public const string TaskName = "CopyexBackupAgent_Backup";
        private const string LegacyTaskName = "CopyexBackup_Backup";

        public static SchedulerResult Apply(BackupConfig config)
        {
            SchedulerResult result = new SchedulerResult();
            try
            {
                RunSchtasks("/Delete /TN \"" + LegacyTaskName + "\" /F");

                if (config.Schedule == ScheduleMode.Disabled)
                {
                    RunSchtasks("/Delete /TN \"" + TaskName + "\" /F");
                    result.Success = true;
                    result.Message = "Copia automática desactivada.";
                    return result;
                }

                DateTime time;
                if (!TryParseTime(config.ScheduleTime, out time))
                {
                    result.Message = "La hora indicada no es válida.";
                    return result;
                }

                if (config.Schedule == ScheduleMode.Weekly && (config.WeeklyDays == null || config.WeeklyDays.Count == 0))
                {
                    result.Message = "Selecciona al menos un día de la semana.";
                    return result;
                }

                string xml = BuildTaskXml(config, time);
                string file = Path.Combine(Path.GetTempPath(), "CopyexBackupAgent_task_" + Guid.NewGuid().ToString("N") + ".xml");
                File.WriteAllText(file, xml, Encoding.Unicode);

                ProcessResult pr;
                try
                {
                    pr = RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + file + "\" /F");
                }
                finally
                {
                    try { File.Delete(file); } catch { }
                }

                if (pr.ExitCode != 0)
                {
                    result.Message = "No se pudo crear la tarea programada de Windows.\r\n\r\n" + pr.Output;
                    return result;
                }

                result.Success = true;
                result.Message = "Copia automática " + Describe(config) + ".";
                return result;
            }
            catch (Exception ex)
            {
                result.Message = "Error configurando el Programador de tareas: " + ex.Message;
                return result;
            }
        }

        public static bool IsInstalled()
        {
            try
            {
                return RunSchtasks("/Query /TN \"" + TaskName + "\"").ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryParseTime(string value, out DateTime time)
        {
            return DateTime.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }

        public static string Describe(BackupConfig config)
        {
            DateTime time;
            string hour = TryParseTime(config.ScheduleTime, out time) ? time.ToString("HH:mm") : config.ScheduleTime;

            if (config.Schedule == ScheduleMode.Daily)
                return "todos los días a las " + hour;
            if (config.Schedule == ScheduleMode.Weekly)
                return "los " + DayList(config.WeeklyDays) + " a las " + hour;
            return "desactivada";
        }

        public static DateTime? GetNextRun(BackupConfig config, DateTime now)
        {
            DateTime time;
            if (config.Schedule == ScheduleMode.Disabled || !TryParseTime(config.ScheduleTime, out time))
                return null;

            for (int i = 0; i < 8; i++)
            {
                DateTime candidate = now.Date.AddDays(i).Add(time.TimeOfDay);
                if (candidate <= now)
                    continue;
                if (config.Schedule == ScheduleMode.Daily)
                    return candidate;
                if (config.WeeklyDays != null && config.WeeklyDays.Contains(candidate.DayOfWeek))
                    return candidate;
            }
            return null;
        }

        private static string BuildTaskXml(BackupConfig config, DateTime time)
        {
            string exe = Application.ExecutablePath;
            string user = WindowsIdentity.GetCurrent().Name;
            string start = DateTime.Today.Add(time.TimeOfDay).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.AppendLine("  <RegistrationInfo>");
            sb.AppendLine("    <Author>Copyex Ofimática S.L.</Author>");
            sb.AppendLine("    <Description>Copia de seguridad automática de Copyex Backup Agent.</Description>");
            sb.AppendLine("  </RegistrationInfo>");
            sb.AppendLine("  <Triggers>");
            sb.AppendLine("    <CalendarTrigger>");
            sb.AppendLine("      <StartBoundary>" + start + "</StartBoundary>");
            sb.AppendLine("      <Enabled>true</Enabled>");
            if (config.Schedule == ScheduleMode.Daily)
            {
                sb.AppendLine("      <ScheduleByDay>");
                sb.AppendLine("        <DaysInterval>1</DaysInterval>");
                sb.AppendLine("      </ScheduleByDay>");
            }
            else
            {
                sb.AppendLine("      <ScheduleByWeek>");
                sb.AppendLine("        <DaysOfWeek>");
                foreach (DayOfWeek d in OrderedDays(config.WeeklyDays))
                    sb.AppendLine("          <" + d + " />");
                sb.AppendLine("        </DaysOfWeek>");
                sb.AppendLine("        <WeeksInterval>1</WeeksInterval>");
                sb.AppendLine("      </ScheduleByWeek>");
            }
            sb.AppendLine("    </CalendarTrigger>");
            sb.AppendLine("  </Triggers>");
            sb.AppendLine("  <Principals>");
            sb.AppendLine("    <Principal id=\"Author\">");
            sb.AppendLine("      <UserId>" + SecurityElement.Escape(user) + "</UserId>");
            sb.AppendLine("      <LogonType>InteractiveToken</LogonType>");
            sb.AppendLine("      <RunLevel>LeastPrivilege</RunLevel>");
            sb.AppendLine("    </Principal>");
            sb.AppendLine("  </Principals>");
            sb.AppendLine("  <Settings>");
            sb.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            sb.AppendLine("    <StartWhenAvailable>" + (config.RunMissedBackup ? "true" : "false") + "</StartWhenAvailable>");
            sb.AppendLine("    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.AppendLine("    <IdleSettings>");
            sb.AppendLine("      <StopOnIdleEnd>false</StopOnIdleEnd>");
            sb.AppendLine("      <RestartOnIdle>false</RestartOnIdle>");
            sb.AppendLine("    </IdleSettings>");
            sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("    <Hidden>false</Hidden>");
            sb.AppendLine("    <RunOnlyIfIdle>false</RunOnlyIfIdle>");
            sb.AppendLine("    <WakeToRun>" + (config.WakeToRun ? "true" : "false") + "</WakeToRun>");
            sb.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            sb.AppendLine("    <Priority>6</Priority>");
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\">");
            sb.AppendLine("    <Exec>");
            sb.AppendLine("      <Command>" + SecurityElement.Escape("\"" + exe + "\"") + "</Command>");
            sb.AppendLine("      <Arguments>--run-backup</Arguments>");
            sb.AppendLine("    </Exec>");
            sb.AppendLine("  </Actions>");
            sb.AppendLine("</Task>");
            return sb.ToString();
        }

        private static List<DayOfWeek> OrderedDays(List<DayOfWeek> days)
        {
            List<DayOfWeek> list = new List<DayOfWeek>();
            DayOfWeek[] order = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                                  DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            foreach (DayOfWeek d in order)
            {
                if (days != null && days.Contains(d))
                    list.Add(d);
            }
            return list;
        }

        private static string DayList(List<DayOfWeek> days)
        {
            List<DayOfWeek> ordered = OrderedDays(days);
            if (ordered.Count == 0) return "(ningún día)";
            if (ordered.Count == 5 && !ordered.Contains(DayOfWeek.Saturday) && !ordered.Contains(DayOfWeek.Sunday))
                return "días laborables (lunes a viernes)";

            List<string> names = new List<string>();
            foreach (DayOfWeek d in ordered) names.Add(DayName(d));
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " y " + names[names.Count - 1];
        }

        public static string DayName(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return "lunes";
                case DayOfWeek.Tuesday: return "martes";
                case DayOfWeek.Wednesday: return "miércoles";
                case DayOfWeek.Thursday: return "jueves";
                case DayOfWeek.Friday: return "viernes";
                case DayOfWeek.Saturday: return "sábados";
                default: return "domingos";
            }
        }

        private static ProcessResult RunSchtasks(string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "schtasks.exe";
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process p = Process.Start(psi))
            {
                string stderr = string.Empty;
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) stderr += e.Data + "\r\n"; };
                p.BeginErrorReadLine();
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);

                ProcessResult r = new ProcessResult();
                r.ExitCode = p.HasExited ? p.ExitCode : -1;
                r.Output = (stdout + "\r\n" + stderr).Trim();
                return r;
            }
        }

        private class ProcessResult
        {
            public int ExitCode;
            public string Output;
        }
    }
}
