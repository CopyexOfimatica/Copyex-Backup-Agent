// Copyex Backup Agent
// Copyright (C) 2026 Copyex Ofimática S.L.
// SPDX-License-Identifier: AGPL-3.0-only
// Software libre bajo la licencia GNU AGPL v3 (ver LICENSE). Se distribuye SIN NINGUNA GARANTÍA.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CopyexBackupAgent.Models;

namespace CopyexBackupAgent.Services
{
    /// <summary>
    /// Hace una copia completa de los orígenes en una carpeta nueva con fecha y hora dentro del destino
    /// y conserva solo las N copias más recientes de este equipo.
    ///
    /// La copia se escribe primero en una carpeta temporal "._CopyexBackup_EQUIPO_..._INCOMPLETE" y solo se
    /// renombra a su nombre definitivo cuando ha terminado. Así nunca se confunde una copia a medias con una buena
    /// y nunca se borra una copia antigua antes de tener la nueva.
    /// </summary>
    public class BackupService
    {
        public const string MarkerFileName = "_CopyexBackup.txt";
        private const string StagingPrefix = "._CopyexBackup_";
        private const string LegacyStagingPrefix = "._CopyexBackupAgent_";
        private const string StagingSuffix = "_INCOMPLETE";
        private const string FolderFormat = "yyyy-MM-dd_HH-mm-ss";
        private const int MaxLoggedErrors = 300;
        private const int MaxReportedErrors = 5000;

        private static readonly string[] AlwaysExcludedDirs =
        {
            "System Volume Information", "$RECYCLE.BIN", "RECYCLER", "$WinREAgent"
        };

        private static readonly int[] NetworkErrors = { 51, 53, 59, 64, 65, 67, 121, 240, 1231, 1232, 1236 };
        private static readonly int[] RetryableErrors = { 32, 33, 51, 53, 59, 64, 65, 67, 121, 240, 1231, 1232, 1236 };

        private readonly NativeMethods.CopyProgressRoutine _copyRoutine;
        private readonly Stopwatch _clock = new Stopwatch();

        private Action<BackupProgress> _progress;
        private Action<BackupLogEntry> _log;
        private Func<bool> _cancelRequested;
        private List<Regex> _exclusions;
        private List<string> _errorDetails;
        private string _staging;

        private BackupPhase _phase;
        private string _currentItem;
        private long _lastReportMs;
        private long _copyStartMs;
        private long _totalFiles;
        private long _totalBytes;
        private long _filesCopied;
        private long _filesFailed;
        private long _bytesCopied;
        private long _bytesProcessed;
        private long _currentTransferred;
        private int _errorsCount;
        private int _errorsLogged;
        private int _linksSkipped;
        private int _consecutiveNetworkErrors;

        public BackupService()
        {
            _copyRoutine = CopyProgressCallback;
        }

        public BackupResult Run(
            BackupConfig config,
            bool unattended,
            Action<BackupProgress> progress,
            Func<bool> cancelRequested,
            Action<BackupLogEntry> log)
        {
            _progress = progress;
            _log = log;
            _cancelRequested = cancelRequested ?? delegate { return false; };
            _errorDetails = new List<string>();
            _clock.Restart();
            _lastReportMs = -10000;

            BackupResult result = new BackupResult();
            result.StartTime = DateTime.Now;

            // Evita que el equipo entre en suspensión mientras se copia.
            NativeMethods.SetThreadExecutionState(
                NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED | NativeMethods.ES_AWAYMODE_REQUIRED);

            try
            {
                ValidateConfig(config);
                string destination = config.Destination.Trim();

                SetPhase(BackupPhase.Preparing, "Conectando con el destino...");
                ConnectDestination(config);
                WaitForDestination(destination, unattended);
                Log(BackupLogLevel.Success, "Destino accesible: " + destination);
                CleanupIncompleteBackups(destination);

                _exclusions = BuildExclusions(config.Exclusions);

                SetPhase(BackupPhase.Scanning, "Analizando los archivos a copiar...");
                Scan(config.Sources);
                Log(BackupLogLevel.Info, "Se van a copiar " + _totalFiles.ToString("N0") + " archivos (" +
                    FileSystem.FormatBytes(_totalBytes) + ").");

                EnsureFreeSpace(destination, config.CopiesToKeep);

                string backupName = GetUniqueBackupFolderName(destination, result.StartTime);
                string finalFolder = FileSystem.Combine(destination, backupName);
                _staging = FileSystem.Combine(destination,
                    StagingPrefix + SafeMachineName() + "_" + backupName + StagingSuffix);

                FileSystem.DeleteTree(_staging);
                FileSystem.CreateDirectoryTree(_staging);

                SetPhase(BackupPhase.Copying, string.Empty);
                _copyStartMs = _clock.ElapsedMilliseconds;

                Dictionary<string, int> usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                usedNames[MarkerFileName] = 1;

                for (int i = 0; i < config.Sources.Count; i++)
                {
                    CheckCancelled();
                    string source = config.Sources[i].Trim();
                    uint attrs = FileSystem.GetAttributes(source);

                    if (attrs == NativeMethods.INVALID_FILE_ATTRIBUTES)
                    {
                        RecordError("No existe el origen o no está disponible: " + source);
                        continue;
                    }

                    if ((attrs & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        string baseName = FileSystem.GetLeafName(source);
                        if (string.IsNullOrEmpty(baseName) || baseName.EndsWith(":"))
                            baseName = "Unidad " + source.Substring(0, 1).ToUpperInvariant();

                        string target = FileSystem.Combine(_staging, UniqueName(baseName, usedNames));
                        Log(BackupLogLevel.Info, "Copiando carpeta: " + source);
                        long before = _filesCopied;
                        CopyDirectory(source, target);
                        Log(BackupLogLevel.Success, "Carpeta terminada: " + source + " (" +
                            (_filesCopied - before).ToString("N0") + " archivos)");
                    }
                    else
                    {
                        FsEntry entry = FileSystem.GetEntry(source);
                        long size = entry != null ? entry.Size : 0;
                        string target = FileSystem.Combine(_staging, UniqueName(FileSystem.GetLeafName(source), usedNames));
                        if (CopyOneFile(source, target, size))
                            Log(BackupLogLevel.Success, "Archivo copiado: " + source);
                    }
                }

                CheckCancelled();
                SetPhase(BackupPhase.Finishing, "Finalizando la copia...");

                result.EndTime = DateTime.Now;
                FillCounters(result);
                WriteMarker(_staging, config, result);

                MoveWithRetry(_staging, finalFolder);
                _staging = null;
                result.BackupFolder = finalFolder;
                Log(BackupLogLevel.Success, "Copia guardada en: " + finalFolder);

                result.DeletedOldBackups = ApplyRetention(destination, config.CopiesToKeep, finalFolder);

                result.EndTime = DateTime.Now;
                FillCounters(result);

                if (_errorsCount == 0)
                {
                    result.Status = BackupStatus.Ok;
                    result.Message = "Copia correcta: " + _filesCopied.ToString("N0") + " archivos, " +
                        FileSystem.FormatBytes(_bytesCopied) + " en " + FormatDuration(result.EndTime - result.StartTime) + ".";
                    Log(BackupLogLevel.Success, result.Message);
                }
                else
                {
                    result.Status = BackupStatus.CompletedWithErrors;
                    result.Message = "Copia terminada con " + _errorsCount.ToString("N0") + " incidencia(s): " +
                        _filesCopied.ToString("N0") + " archivos copiados" +
                        (_filesFailed > 0 ? ", " + _filesFailed.ToString("N0") + " no se pudieron copiar" : string.Empty) +
                        ". Detalle en el Registro.";
                    Log(BackupLogLevel.Warning, result.Message);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                DeleteStagingQuietly();
                result.Status = BackupStatus.Cancelled;
                result.Message = "Copia cancelada. No se ha guardado nada y se conservan las copias anteriores.";
                Log(BackupLogLevel.Warning, result.Message);
                return result;
            }
            catch (Exception ex)
            {
                DeleteStagingQuietly();
                _errorsCount++;
                result.Status = BackupStatus.Failed;
                result.Message = "La copia ha fallado: " + ex.Message;
                Log(BackupLogLevel.Error, result.Message);
                return result;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                FillCounters(result);
                NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
            }
        }

        // ------------------------------------------------------------------ comprobaciones

        public static void TestDestination(string destination)
        {
            if (string.IsNullOrWhiteSpace(destination))
                throw new InvalidOperationException("Indica primero una carpeta de destino.");
            EnsureDestinationWritable(destination.Trim());
        }

        private static void ValidateConfig(BackupConfig config)
        {
            if (config == null)
                throw new InvalidOperationException("La configuración no es válida.");
            if (config.Sources == null || config.Sources.Count == 0)
                throw new InvalidOperationException("No hay carpetas de origen configuradas.");
            if (string.IsNullOrWhiteSpace(config.Destination))
                throw new InvalidOperationException("No hay carpeta de destino configurada.");
            if (config.CopiesToKeep < 1)
                throw new InvalidOperationException("El número de copias a conservar debe ser al menos 1.");

            string destFull = NormalizeForComparison(config.Destination);
            for (int i = 0; i < config.Sources.Count; i++)
            {
                string source = config.Sources[i];
                if (!FileSystem.DirectoryExists(source))
                    continue;

                string sourceFull = NormalizeForComparison(source);
                if (destFull.StartsWith(sourceFull, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "El destino no puede estar dentro de una carpeta de origen (" + source + ").");
                }
            }
        }

        private static string NormalizeForComparison(string path)
        {
            string value = FileSystem.FromLong(path).Replace('/', '\\').Trim();
            try
            {
                value = Path.GetFullPath(value);
            }
            catch
            {
            }
            return value.TrimEnd('\\') + "\\";
        }

        private void ConnectDestination(BackupConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.NasUser))
                return;

            string password = ConfigService.DecryptPassword(config.NasPassword);
            string error = NetworkShare.Connect(config.Destination, config.NasUser.Trim(), password);
            if (error == null)
                Log(BackupLogLevel.Info, "Sesión iniciada en el NAS como " + config.NasUser.Trim() + ".");
            else
                Log(BackupLogLevel.Warning, "No se pudo iniciar sesión en el NAS: " + error);
        }

        private void WaitForDestination(string destination, bool unattended)
        {
            // En copias programadas el NAS puede estar despertando: se espera hasta ~2 minutos.
            int attempts = unattended ? 8 : 2;
            int delaySeconds = unattended ? 15 : 4;
            Exception last = null;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                CheckCancelled();
                try
                {
                    EnsureDestinationWritable(destination);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < attempts)
                    {
                        Log(BackupLogLevel.Warning, "El destino no responde (" + ex.Message + "). Reintento " +
                            attempt + " de " + (attempts - 1) + " en " + delaySeconds + " s...");
                        SleepCancellable(delaySeconds * 1000);
                    }
                }
            }

            throw new InvalidOperationException("No se puede acceder al destino " + destination + ". " +
                (last != null ? last.Message : string.Empty));
        }

        private static void EnsureDestinationWritable(string destination)
        {
            FileSystem.CreateDirectoryTree(destination);
            string test = FileSystem.Combine(destination, ".copyexbackup_test_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(test, "Copyex");
            }
            finally
            {
                NativeMethods.DeleteFile(FileSystem.ToLong(test));
            }
        }

        private void EnsureFreeSpace(string destination, int copiesToKeep)
        {
            long free, total;
            if (!FileSystem.TryGetFreeSpace(destination, out free, out total))
            {
                Log(BackupLogLevel.Warning, "No se pudo comprobar el espacio libre del destino.");
                return;
            }

            long needed = _totalBytes + _totalBytes / 50 + 64L * 1024 * 1024;
            Log(BackupLogLevel.Info, "Espacio libre en destino: " + FileSystem.FormatBytes(free) +
                " (necesario aprox. " + FileSystem.FormatBytes(needed) + ").");
            if (free >= needed)
                return;

            // Falta espacio: se adelanta el borrado de las copias que se eliminarían igualmente al terminar.
            List<BackupFolderInfo> own = ListBackups(destination).Where(b => b.IsOwn).OrderBy(b => b.Date).ToList();
            int keepBefore = Math.Max(copiesToKeep - 1, 0);

            while (free < needed && own.Count > keepBefore)
            {
                CheckCancelled();
                BackupFolderInfo oldest = own[0];
                own.RemoveAt(0);
                Log(BackupLogLevel.Warning, "Falta espacio: se elimina antes la copia más antigua " + oldest.Name);
                try
                {
                    FileSystem.DeleteTree(oldest.Path);
                }
                catch (Exception ex)
                {
                    Log(BackupLogLevel.Warning, "No se pudo eliminar " + oldest.Name + ": " + ex.Message);
                }
                FileSystem.TryGetFreeSpace(destination, out free, out total);
            }

            if (free < needed)
            {
                throw new InvalidOperationException("No hay espacio suficiente en el destino. Libre: " +
                    FileSystem.FormatBytes(free) + ", necesario: " + FileSystem.FormatBytes(needed) + ".");
            }
        }

        // ------------------------------------------------------------------ análisis y copia

        private void Scan(List<string> sources)
        {
            _totalFiles = 0;
            _totalBytes = 0;

            for (int i = 0; i < sources.Count; i++)
            {
                string source = sources[i].Trim();
                uint attrs = FileSystem.GetAttributes(source);
                if (attrs == NativeMethods.INVALID_FILE_ATTRIBUTES)
                    continue;

                if ((attrs & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) == 0)
                {
                    FsEntry e = FileSystem.GetEntry(source);
                    _totalFiles++;
                    if (e != null) _totalBytes += e.Size;
                    continue;
                }

                Stack<string> stack = new Stack<string>();
                stack.Push(source);
                while (stack.Count > 0)
                {
                    CheckCancelled();
                    string dir = stack.Pop();
                    _currentItem = dir;
                    Report(false);

                    List<FsEntry> entries;
                    try
                    {
                        entries = FileSystem.List(dir);
                    }
                    catch
                    {
                        continue; // el error se anota durante la copia
                    }

                    for (int j = 0; j < entries.Count; j++)
                    {
                        FsEntry e = entries[j];
                        if (e.IsDirectory)
                        {
                            if (!e.IsLink && !IsExcludedDirectory(e.Name))
                                stack.Push(FileSystem.Combine(dir, e.Name));
                        }
                        else if (!IsExcluded(e.Name))
                        {
                            _totalFiles++;
                            _totalBytes += e.Size;
                        }
                    }
                }
            }

            Report(true);
        }

        private void CopyDirectory(string sourceRoot, string targetRoot)
        {
            Stack<KeyValuePair<string, string>> stack = new Stack<KeyValuePair<string, string>>();
            stack.Push(new KeyValuePair<string, string>(sourceRoot, targetRoot));

            while (stack.Count > 0)
            {
                CheckCancelled();
                KeyValuePair<string, string> pair = stack.Pop();
                string src = pair.Key;
                string dst = pair.Value;

                try
                {
                    FileSystem.CreateDirectoryTree(dst);
                }
                catch (Exception ex)
                {
                    RecordError("No se pudo crear la carpeta en el destino para: " + src + " — " + ex.Message);
                    CheckDestinationAlive();
                    continue;
                }

                List<FsEntry> entries;
                try
                {
                    entries = FileSystem.List(src);
                }
                catch (Exception ex)
                {
                    RecordError("No se puede leer la carpeta: " + src + " — " + ex.Message);
                    continue;
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    FsEntry e = entries[i];
                    if (e.IsDirectory || IsExcluded(e.Name))
                        continue;
                    CopyOneFile(FileSystem.Combine(src, e.Name), FileSystem.Combine(dst, e.Name), e.Size);
                }

                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    FsEntry e = entries[i];
                    if (!e.IsDirectory || IsExcludedDirectory(e.Name))
                        continue;

                    if (e.IsLink)
                    {
                        _linksSkipped++;
                        if (_linksSkipped <= 20)
                            Log(BackupLogLevel.Warning, "Omitido acceso directo de carpeta (enlace): " + FileSystem.Combine(src, e.Name));
                        continue;
                    }

                    stack.Push(new KeyValuePair<string, string>(FileSystem.Combine(src, e.Name), FileSystem.Combine(dst, e.Name)));
                }
            }
        }

        private bool CopyOneFile(string source, string target, long size)
        {
            _currentItem = source;
            int attempt = 0;

            while (true)
            {
                CheckCancelled();
                _currentTransferred = 0;
                Report(false);

                int cancelFlag = 0;
                bool ok = NativeMethods.CopyFileEx(
                    FileSystem.ToLong(source),
                    FileSystem.ToLong(target),
                    _copyRoutine,
                    IntPtr.Zero,
                    ref cancelFlag,
                    NativeMethods.COPY_FILE_ALLOW_DECRYPTED_DESTINATION);

                if (ok)
                {
                    _filesCopied++;
                    _bytesCopied += size;
                    _bytesProcessed += size;
                    _currentTransferred = 0;
                    _consecutiveNetworkErrors = 0;
                    Report(false);
                    return true;
                }

                int err = Marshal.GetLastWin32Error();
                _currentTransferred = 0;

                if (err == NativeMethods.ERROR_REQUEST_ABORTED && _cancelRequested())
                    throw new OperationCanceledException();

                attempt++;
                if (attempt < 3 && Array.IndexOf(RetryableErrors, err) >= 0)
                {
                    SleepCancellable(1500 * attempt);
                    continue;
                }

                _filesFailed++;
                _bytesProcessed += size;
                RecordError("No se pudo copiar: " + source + " — " + FileSystem.Win32Message(err));

                if (Array.IndexOf(NetworkErrors, err) >= 0)
                {
                    _consecutiveNetworkErrors++;
                    if (_consecutiveNetworkErrors >= 10)
                        CheckDestinationAlive();
                }

                return false;
            }
        }

        private void CheckDestinationAlive()
        {
            if (_staging != null && !FileSystem.DirectoryExists(_staging))
                throw new IOException("Se ha perdido la conexión con el destino.");
            _consecutiveNetworkErrors = 0;
        }

        private uint CopyProgressCallback(
            long totalFileSize, long totalBytesTransferred, long streamSize, long streamBytesTransferred,
            uint streamNumber, uint callbackReason, IntPtr sourceFile, IntPtr destinationFile, IntPtr data)
        {
            _currentTransferred = totalBytesTransferred;
            Report(false);
            return _cancelRequested() ? NativeMethods.PROGRESS_CANCEL : NativeMethods.PROGRESS_CONTINUE;
        }

        // ------------------------------------------------------------------ histórico / retención

        public static List<BackupFolderInfo> ListBackups(string destination)
        {
            List<BackupFolderInfo> result = new List<BackupFolderInfo>();
            List<FsEntry> entries = FileSystem.List(destination);
            string machine = Environment.MachineName;

            for (int i = 0; i < entries.Count; i++)
            {
                FsEntry e = entries[i];
                if (!e.IsDirectory || e.Name.StartsWith("._", StringComparison.Ordinal))
                    continue;

                BackupFolderInfo info = new BackupFolderInfo();
                info.Name = e.Name;
                info.Path = FileSystem.Combine(destination, e.Name);

                DateTime nameDate;
                bool hasNameDate = TryParseFolderDate(e.Name, out nameDate);
                string marker = FileSystem.Combine(info.Path, MarkerFileName);

                if (FileSystem.FileExists(marker))
                {
                    info.HasMarker = true;
                    info.Date = hasNameDate ? nameDate : DateTime.MinValue;
                    ReadMarker(marker, info);
                    if (info.Date == DateTime.MinValue)
                        continue;
                }
                else if (hasNameDate)
                {
                    // Copias hechas por la versión anterior del programa (sin fichero de control).
                    info.Date = nameDate;
                    info.Files = -1;
                    info.Bytes = -1;
                }
                else
                {
                    continue; // carpeta ajena: nunca se toca
                }

                info.IsOwn = string.IsNullOrEmpty(info.Machine) ||
                             string.Equals(info.Machine, machine, StringComparison.OrdinalIgnoreCase);
                result.Add(info);
            }

            result.Sort(delegate(BackupFolderInfo a, BackupFolderInfo b) { return b.Date.CompareTo(a.Date); });
            return result;
        }

        private int ApplyRetention(string destination, int copiesToKeep, string currentFolder)
        {
            List<BackupFolderInfo> own;
            try
            {
                own = ListBackups(destination).Where(b => b.IsOwn).ToList();
            }
            catch (Exception ex)
            {
                Log(BackupLogLevel.Warning, "No se pudo revisar el histórico de copias: " + ex.Message);
                return 0;
            }

            // La copia recién hecha siempre cuenta como la más reciente.
            own.RemoveAll(b => string.Equals(b.Path, currentFolder, StringComparison.OrdinalIgnoreCase));
            int keepOthers = Math.Max(copiesToKeep - 1, 0);
            int deleted = 0;

            for (int i = keepOthers; i < own.Count; i++)
            {
                try
                {
                    FileSystem.DeleteTree(own[i].Path);
                    deleted++;
                    Log(BackupLogLevel.Info, "Histórico: eliminada la copia antigua " + own[i].Name);
                }
                catch (Exception ex)
                {
                    Log(BackupLogLevel.Warning, "Histórico: no se pudo eliminar " + own[i].Name + " — " + ex.Message);
                }
            }

            int kept = Math.Min(own.Count, keepOthers) + 1;
            Log(BackupLogLevel.Info, "Histórico: se conservan " + kept + " de " + copiesToKeep + " copias.");
            return deleted;
        }

        private void CleanupIncompleteBackups(string destination)
        {
            string ownPrefix = StagingPrefix + SafeMachineName() + "_";
            try
            {
                List<FsEntry> entries = FileSystem.List(destination);
                for (int i = 0; i < entries.Count; i++)
                {
                    FsEntry e = entries[i];
                    if (!e.IsDirectory || !e.Name.EndsWith(StagingSuffix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool own = e.Name.StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase) ||
                               e.Name.StartsWith(LegacyStagingPrefix, StringComparison.OrdinalIgnoreCase);
                    if (!own)
                        continue;

                    try
                    {
                        FileSystem.DeleteTree(FileSystem.Combine(destination, e.Name));
                        Log(BackupLogLevel.Info, "Eliminada una copia incompleta anterior: " + e.Name);
                    }
                    catch (Exception ex)
                    {
                        Log(BackupLogLevel.Warning, "No se pudo eliminar la copia incompleta " + e.Name + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(BackupLogLevel.Warning, "No se pudieron revisar copias incompletas: " + ex.Message);
            }
        }

        private static bool TryParseFolderDate(string name, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrEmpty(name))
                return false;

            if (Regex.IsMatch(name, @"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(_\d{2})?$"))
                return DateTime.TryParseExact(name.Substring(0, 19), FolderFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

            if (Regex.IsMatch(name, @"^\d{8}_\d{6}(_\d{2})?$"))
                return DateTime.TryParseExact(name.Substring(0, 15), "ddMMyyyy_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

            if (Regex.IsMatch(name, @"^\d{8}$"))
                return DateTime.TryParseExact(name, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

            return false;
        }

        private static string GetUniqueBackupFolderName(string destination, DateTime timestamp)
        {
            string baseName = timestamp.ToString(FolderFormat, CultureInfo.InvariantCulture);
            string candidate = baseName;
            int suffix = 2;
            while (FileSystem.DirectoryExists(FileSystem.Combine(destination, candidate)))
            {
                candidate = baseName + "_" + suffix.ToString("00", CultureInfo.InvariantCulture);
                suffix++;
            }
            return candidate;
        }

        private void WriteMarker(string folder, BackupConfig config, BackupResult result)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Copyex Backup Agent - copia de seguridad");
                sb.AppendLine("========================================");
                sb.AppendLine("Equipo: " + Environment.MachineName);
                sb.AppendLine("Usuario: " + Environment.UserDomainName + "\\" + Environment.UserName);
                sb.AppendLine("Inicio: " + result.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("Fin: " + result.EndTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("Archivos: " + _filesCopied.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Bytes: " + _bytesCopied.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Errores: " + _errorsCount.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Tamaño: " + FileSystem.FormatBytes(_bytesCopied));
                sb.AppendLine();
                sb.AppendLine("Orígenes:");
                for (int i = 0; i < config.Sources.Count; i++)
                    sb.AppendLine("  " + config.Sources[i]);

                if (_errorDetails.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Incidencias:");
                    for (int i = 0; i < _errorDetails.Count; i++)
                        sb.AppendLine("  " + _errorDetails[i]);
                    if (_errorsCount > _errorDetails.Count)
                        sb.AppendLine("  ... y " + (_errorsCount - _errorDetails.Count) + " más.");
                }

                File.WriteAllText(FileSystem.Combine(folder, MarkerFileName), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log(BackupLogLevel.Warning, "No se pudo escribir el resumen de la copia: " + ex.Message);
            }
        }

        private static void ReadMarker(string path, BackupFolderInfo info)
        {
            try
            {
                foreach (string line in File.ReadLines(path).Take(20))
                {
                    int idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    string key = line.Substring(0, idx).Trim();
                    string value = line.Substring(idx + 1).Trim();
                    long n;
                    DateTime d;

                    if (key == "Equipo") info.Machine = value;
                    else if (key == "Archivos" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) info.Files = n;
                    else if (key == "Bytes" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) info.Bytes = n;
                    else if (key == "Errores" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) info.Errors = (int)n;
                    else if (key == "Inicio" && DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) info.Date = d;
                }
            }
            catch
            {
            }
        }

        private void MoveWithRetry(string source, string target)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    FileSystem.MoveDirectory(source, target);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(2000);
                }
            }
            throw new IOException("No se pudo cerrar la copia (renombrar la carpeta temporal): " + last.Message);
        }

        private void DeleteStagingQuietly()
        {
            if (string.IsNullOrEmpty(_staging))
                return;
            try
            {
                FileSystem.DeleteTree(_staging);
            }
            catch
            {
                // Se volverá a intentar al empezar la próxima copia.
            }
            _staging = null;
        }

        // ------------------------------------------------------------------ exclusiones

        private static List<Regex> BuildExclusions(string patterns)
        {
            List<Regex> list = new List<Regex>();
            if (string.IsNullOrWhiteSpace(patterns))
                return list;

            string[] parts = patterns.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                string rx = "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                list.Add(new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            return list;
        }

        private bool IsExcluded(string name)
        {
            if (_exclusions == null) return false;
            for (int i = 0; i < _exclusions.Count; i++)
            {
                if (_exclusions[i].IsMatch(name))
                    return true;
            }
            return false;
        }

        private bool IsExcludedDirectory(string name)
        {
            for (int i = 0; i < AlwaysExcludedDirs.Length; i++)
            {
                if (string.Equals(AlwaysExcludedDirs[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return IsExcluded(name);
        }

        // ------------------------------------------------------------------ utilidades

        private void FillCounters(BackupResult result)
        {
            result.FilesCopied = _filesCopied;
            result.FilesFailed = _filesFailed;
            result.BytesCopied = _bytesCopied;
            result.ErrorsCount = _errorsCount;
        }

        private void RecordError(string message)
        {
            _errorsCount++;
            if (_errorDetails.Count < MaxReportedErrors)
                _errorDetails.Add(message);

            _errorsLogged++;
            if (_errorsLogged <= MaxLoggedErrors)
                Log(BackupLogLevel.Error, message);
            else if (_errorsLogged == MaxLoggedErrors + 1)
                Log(BackupLogLevel.Warning, "Hay demasiados errores para mostrarlos todos. La lista completa queda en el fichero " + MarkerFileName + " de la copia.");
        }

        private void Log(BackupLogLevel level, string message)
        {
            if (_log == null) return;
            BackupLogEntry entry = new BackupLogEntry();
            entry.Level = level;
            entry.Message = message ?? string.Empty;
            try
            {
                _log(entry);
            }
            catch
            {
            }
        }

        private void SetPhase(BackupPhase phase, string item)
        {
            _phase = phase;
            _currentItem = item;
            Report(true);
        }

        private void Report(bool force)
        {
            if (_progress == null) return;
            long now = _clock.ElapsedMilliseconds;
            if (!force && now - _lastReportMs < 200)
                return;
            _lastReportMs = now;

            BackupProgress p = new BackupProgress();
            p.Phase = _phase;
            p.CurrentItem = _currentItem ?? string.Empty;
            p.FilesTotal = _totalFiles;
            p.FilesDone = _filesCopied + _filesFailed;
            p.BytesTotal = _totalBytes;
            p.BytesDone = _bytesProcessed + _currentTransferred;

            if (_phase == BackupPhase.Copying)
            {
                double seconds = (now - _copyStartMs) / 1000.0;
                long copied = _bytesCopied + _currentTransferred;
                if (seconds >= 3 && copied > 0)
                {
                    p.BytesPerSecond = (long)(copied / seconds);
                    long remainingBytes = Math.Max(0, _totalBytes - p.BytesDone);
                    if (p.BytesPerSecond > 0)
                    {
                        p.Remaining = TimeSpan.FromSeconds(remainingBytes / (double)p.BytesPerSecond);
                        p.HasRemaining = true;
                    }
                }
            }

            try
            {
                _progress(p);
            }
            catch
            {
            }
        }

        private void CheckCancelled()
        {
            if (_cancelRequested())
                throw new OperationCanceledException();
        }

        private void SleepCancellable(int milliseconds)
        {
            int waited = 0;
            while (waited < milliseconds)
            {
                CheckCancelled();
                Thread.Sleep(250);
                waited += 250;
            }
        }

        private static string UniqueName(string original, Dictionary<string, int> used)
        {
            int count;
            if (!used.TryGetValue(original, out count))
            {
                used[original] = 1;
                return original;
            }

            count++;
            used[original] = count;
            string name = Path.GetFileNameWithoutExtension(original);
            string ext = Path.GetExtension(original);
            if (string.IsNullOrEmpty(name)) name = original;
            return name + " (" + count + ")" + ext;
        }

        private static string SafeMachineName()
        {
            string name = Environment.MachineName;
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '-');
            return sb.ToString();
        }

        public static string FormatDuration(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return Math.Max(1, (int)t.TotalSeconds) + " s";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min " + t.Seconds + " s";
            return (int)t.TotalHours + " h " + t.Minutes + " min";
        }
    }
}
