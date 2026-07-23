using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ConfigKeeper
{
    /// <summary>
    /// ConfigKeeper — Export and import all Vintage Story settings for backup/restore.
    /// </summary>
    public class ConfigKeeperModSystem : ModSystem
    {
        private ICoreClientAPI capi;
        private ConfigKeeperGui gui;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;

            ResumePendingRestores();

            // Register hotkey (default: Ctrl+Shift+K)
            capi.Input.RegisterHotKey("configkeeper", Lang.Get("configkeeper:hotkey-desc"), GlKeys.K, HotkeyType.GUIOrOtherControls, false, true, true);
            capi.Input.SetHotKeyHandler("configkeeper", OnHotkeyPressed);

            // Register chat command as backup access method
            capi.ChatCommands.Create("ck")
                .WithDescription("Open ConfigKeeper settings backup window")
                .HandleWith(args =>
                {
                    ToggleGui();
                    return TextCommandResult.Success();
                });
        }

        private bool OnHotkeyPressed(KeyCombination comb)
        {
            ToggleGui();
            return true;
        }

        private void ToggleGui()
        {
            if (gui == null)
            {
                gui = new ConfigKeeperGui(capi, this);
            }

            if (gui.IsOpened())
            {
                gui.TryClose();
            }
            else
            {
                gui.TryOpen();
            }
        }

        /// <summary>
        /// Performs the export of selected setting categories into a timestamped .zip archive.
        /// </summary>
        public ExportResult ExportSettings(Dictionary<string, bool> categories)
        {
            var result = new ExportResult();
            string dataPath = GamePaths.DataPath;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string exportFileName = $"ConfigKeeper_Backup_{timestamp}.zip";
            string exportPath = Path.Combine(dataPath, exportFileName);

            try
            {
                using (var zipStream = new FileStream(exportPath, FileMode.Create))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    // Manifest — stores what was exported and when
                    var manifest = new Dictionary<string, string>
                    {
                        { "exportDate", DateTime.Now.ToString("o") },
                        { "gameVersion", GameVersion.ShortGameVersion },
                        { "modVersion", Mod.Info.Version }
                    };

                    // Client Settings
                    if (categories.GetValueOrDefault("clientsettings", true))
                    {
                        string clientSettingsPath = Path.Combine(dataPath, "clientsettings.json");
                        if (File.Exists(clientSettingsPath))
                        {
                            AddFileToZip(archive, clientSettingsPath, "clientsettings.json");
                            result.ItemsExported.Add("Client Settings");
                        }
                    }

                    // Server Settings
                    if (categories.GetValueOrDefault("serversettings", true))
                    {
                        string serverConfigPath = Path.Combine(dataPath, "serverconfig.json");
                        if (File.Exists(serverConfigPath))
                        {
                            AddFileToZip(archive, serverConfigPath, "serverconfig.json");
                            result.ItemsExported.Add("Server Settings");
                        }
                    }

                    // Mod Configurations (entire ModConfig folder)
                    if (categories.GetValueOrDefault("modconfig", true))
                    {
                        string modConfigPath = Path.Combine(dataPath, "ModConfig");
                        if (Directory.Exists(modConfigPath))
                        {
                            int count = AddDirectoryToZip(archive, modConfigPath, "ModConfig");
                            result.ItemsExported.Add($"Mod Configs ({count} files)");
                        }
                    }

                    // Macros
                    if (categories.GetValueOrDefault("macros", true))
                    {
                        string macrosPath = Path.Combine(dataPath, "Macros");
                        if (Directory.Exists(macrosPath))
                        {
                            int count = AddDirectoryToZip(archive, macrosPath, "Macros");
                            result.ItemsExported.Add($"Macros ({count} files)");
                        }

                        // Also check for macros stored in clientsettings (already captured above)
                    }

                    // Server Favorites
                    if (categories.GetValueOrDefault("serverfavorites", true))
                    {
                        string favoritesPath = Path.Combine(dataPath, "serverlist.json");
                        if (File.Exists(favoritesPath))
                        {
                            AddFileToZip(archive, favoritesPath, "serverlist.json");
                            result.ItemsExported.Add("Server Favorites");
                        }

                        // Also try the servers.json variant
                        string serversPath = Path.Combine(dataPath, "servers.json");
                        if (File.Exists(serversPath))
                        {
                            AddFileToZip(archive, serversPath, "servers.json");
                            result.ItemsExported.Add("Servers List");
                        }
                    }

                    // Installed Mods (the actual mod .zip/.cs files from the Mods folder)
                    if (categories.GetValueOrDefault("installedmods", true))
                    {
                        string modsPath = Path.Combine(dataPath, "Mods");
                        if (Directory.Exists(modsPath))
                        {
                            int count = 0;
                            // Grab .zip mods, .cs mods, and .dll mods
                            string[] modExtensions = { "*.zip", "*.cs", "*.dll" };
                            foreach (string pattern in modExtensions)
                            {
                                foreach (string modFile in Directory.GetFiles(modsPath, pattern))
                                {
                                    string fileName = Path.GetFileName(modFile);
                                    // Skip ourselves to avoid recursion issues on import
                                    if (fileName.StartsWith("ConfigKeeper", StringComparison.OrdinalIgnoreCase)) continue;

                                    AddFileToZip(archive, modFile, "Mods/" + fileName);
                                    count++;
                                }
                            }

                            // Also grab folder-based mods (directories with modinfo.json inside)
                            foreach (string dir in Directory.GetDirectories(modsPath))
                            {
                                string dirName = Path.GetFileName(dir);
                                if (dirName.StartsWith("ConfigKeeper", StringComparison.OrdinalIgnoreCase)) continue;

                                if (File.Exists(Path.Combine(dir, "modinfo.json")))
                                {
                                    int subCount = AddDirectoryToZip(archive, dir, "Mods/" + dirName);
                                    count += subCount;
                                }
                            }

                            if (count > 0)
                            {
                                result.ItemsExported.Add($"Installed Mods ({count} files)");
                            }
                        }
                    }

                    // Write the manifest
                    manifest["categories"] = string.Join(",", categories.Where(kv => kv.Value).Select(kv => kv.Key));
                    manifest["itemCount"] = result.ItemsExported.Count.ToString();
                    var manifestEntry = archive.CreateEntry("_manifest.json");
                    using (var writer = new StreamWriter(manifestEntry.Open()))
                    {
                        writer.Write(SerializeManifest(manifest));
                    }
                }

                result.Success = true;
                result.FilePath = exportPath;
                result.Message = Lang.GetL(Lang.CurrentLocale, "configkeeper:export-success", exportPath);

                capi.Logger.Notification($"[ConfigKeeper] Exported settings to {exportPath}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = Lang.GetL(Lang.CurrentLocale, "configkeeper:export-fail", ex.Message);
                capi.Logger.Error($"[ConfigKeeper] Export failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Imports settings from a ConfigKeeper backup .zip file.
        /// </summary>
        public ImportResult ImportSettings(string zipFilePath, Dictionary<string, bool> categories)
        {
            var result = new ImportResult();
            string dataPath = GamePaths.DataPath;

            try
            {
                if (!File.Exists(zipFilePath))
                {
                    result.Success = false;
                    result.Message = Lang.Get("configkeeper:import-nofile");
                    return result;
                }

                string dataRoot = Path.GetFullPath(dataPath);

                using (var archive = ZipFile.OpenRead(zipFilePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        // Normalize entry names — backups made on Windows may contain backslashes
                        string entryName = entry.FullName.Replace('\\', '/');

                        // Skip manifest
                        if (entryName == "_manifest.json") continue;
                        // Skip directories
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        // Determine category of this entry
                        if (!ShouldImportEntry(entryName, categories)) continue;

                        string targetPath = Path.GetFullPath(Path.Combine(dataPath, entryName));

                        // Guard against zip-slip: entries must resolve inside the data folder
                        if (!targetPath.StartsWith(dataRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        {
                            capi.Logger.Warning($"[ConfigKeeper] Skipping unsafe zip entry outside data folder: {entry.FullName}");
                            continue;
                        }

                        string targetDir = Path.GetDirectoryName(targetPath);

                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        // Create backup of existing file before overwriting
                        if (File.Exists(targetPath))
                        {
                            string backupPath = targetPath + ".ckbackup";
                            File.Copy(targetPath, backupPath, true);
                        }

                        // clientsettings.json and serverconfig.json are owned by the game,
                        // which rewrites them from memory on shutdown — extracting them
                        // directly would be silently undone. Stage them and copy at process
                        // exit instead; client settings are additionally applied live.
                        if (entryName == "clientsettings.json" || entryName == "serverconfig.json")
                        {
                            string stagedPath = targetPath + ".ckpending";
                            entry.ExtractToFile(stagedPath, true);

                            if (entryName == "clientsettings.json")
                            {
                                int applied = ApplyClientSettingsLive(File.ReadAllText(stagedPath));
                                result.ItemsImported.Add($"clientsettings.json ({applied} settings applied now; keybinds apply on next start)");
                            }
                            else
                            {
                                result.ItemsImported.Add("serverconfig.json (applies on next start)");
                            }

                            PendingFileRestores.Stage(targetPath, stagedPath, capi.Logger);
                            continue;
                        }

                        entry.ExtractToFile(targetPath, true);
                        result.ItemsImported.Add(entryName);
                    }
                }

                result.Success = true;
                result.Message = Lang.Get("configkeeper:import-success");
                result.RequiresRestart = true;

                capi.Logger.Notification($"[ConfigKeeper] Imported {result.ItemsImported.Count} files from {zipFilePath}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = Lang.GetL(Lang.CurrentLocale, "configkeeper:import-fail", ex.Message);
                capi.Logger.Error($"[ConfigKeeper] Import failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Scans the data path for any ConfigKeeper backup .zip files.
        /// </summary>
        public List<BackupInfo> FindBackups()
        {
            var backups = new List<BackupInfo>();
            string dataPath = GamePaths.DataPath;

            try
            {
                foreach (string file in Directory.GetFiles(dataPath, "ConfigKeeper_Backup_*.zip"))
                {
                    var info = new FileInfo(file);
                    backups.Add(new BackupInfo
                    {
                        FilePath = file,
                        FileName = info.Name,
                        FileSize = info.Length,
                        Created = info.CreationTime
                    });
                }

                backups.Sort((a, b) => b.Created.CompareTo(a.Created)); // newest first
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[ConfigKeeper] Error scanning for backups: {ex.Message}");
            }

            return backups;
        }

        #region Helpers

        private bool ShouldImportEntry(string entryPath, Dictionary<string, bool> categories)
        {
            if (entryPath == "clientsettings.json")
                return categories.GetValueOrDefault("clientsettings", true);

            if (entryPath == "serverconfig.json")
                return categories.GetValueOrDefault("serversettings", true);

            if (entryPath.StartsWith("ModConfig/"))
                return categories.GetValueOrDefault("modconfig", true);

            if (entryPath.StartsWith("Macros/"))
                return categories.GetValueOrDefault("macros", true);

            if (entryPath == "serverlist.json" || entryPath == "servers.json")
                return categories.GetValueOrDefault("serverfavorites", true);

            if (entryPath.StartsWith("Mods/"))
                return categories.GetValueOrDefault("installedmods", true);

            return true; // import unknown entries by default
        }

        /// <summary>
        /// Settings never applied from a backup: session credentials would log the
        /// user out (or into a stale session), and settingsVersion belongs to the
        /// currently installed game.
        /// </summary>
        private static readonly HashSet<string> SkippedLiveSettings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sessionkey", "sessionsignature", "useremail", "playeruid", "settingsVersion"
        };

        /// <summary>
        /// Applies the typed sections of a backed-up clientsettings.json through the
        /// live settings API, so the game's in-memory state matches the backup and
        /// persists it on shutdown. keyMapping/dialogPositions have no live API and
        /// are covered by the staged file copy at process exit.
        /// </summary>
        private int ApplyClientSettingsLive(string json)
        {
            var root = JObject.Parse(json);
            int applied = 0;

            applied += ApplySettingsSection<int>(root["intSettings"] as JObject, capi.Settings.Int);
            applied += ApplySettingsSection<bool>(root["boolSettings"] as JObject, capi.Settings.Bool);
            applied += ApplySettingsSection<float>(root["floatSettings"] as JObject, capi.Settings.Float);
            applied += ApplySettingsSection<string>(root["stringSettings"] as JObject, capi.Settings.String);

            if (root["stringListSettings"] is JObject lists)
            {
                foreach (var prop in lists.Properties())
                {
                    if (SkippedLiveSettings.Contains(prop.Name)) continue;
                    try
                    {
                        capi.Settings.Strings[prop.Name] = prop.Value.ToObject<List<string>>();
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        capi.Logger.Warning($"[ConfigKeeper] Could not apply setting '{prop.Name}': {ex.Message}");
                    }
                }
            }

            return applied;
        }

        private int ApplySettingsSection<T>(JObject section, ISettingsClass<T> target)
        {
            if (section == null) return 0;

            int applied = 0;
            foreach (var prop in section.Properties())
            {
                if (SkippedLiveSettings.Contains(prop.Name)) continue;
                try
                {
                    target[prop.Name] = prop.Value.ToObject<T>();
                    applied++;
                }
                catch (Exception ex)
                {
                    capi.Logger.Warning($"[ConfigKeeper] Could not apply setting '{prop.Name}': {ex.Message}");
                }
            }
            return applied;
        }

        /// <summary>
        /// If a previous session crashed before its staged restores were written
        /// (process killed before the exit hook ran), pick them up again: re-apply
        /// client settings live and re-stage the files for this session's exit.
        /// </summary>
        private void ResumePendingRestores()
        {
            foreach (string fileName in new[] { "clientsettings.json", "serverconfig.json" })
            {
                string targetPath = Path.Combine(GamePaths.DataPath, fileName);
                string stagedPath = targetPath + ".ckpending";
                if (!File.Exists(stagedPath)) continue;

                try
                {
                    if (fileName == "clientsettings.json")
                    {
                        ApplyClientSettingsLive(File.ReadAllText(stagedPath));
                    }
                    PendingFileRestores.Stage(targetPath, stagedPath, capi.Logger);
                    capi.Logger.Notification($"[ConfigKeeper] Resumed pending restore of {fileName} from a previous session");
                }
                catch (Exception ex)
                {
                    capi.Logger.Error($"[ConfigKeeper] Failed to resume pending restore of {fileName}: {ex}");
                }
            }
        }

        private void AddFileToZip(ZipArchive archive, string sourceFilePath, string entryName)
        {
            archive.CreateEntryFromFile(sourceFilePath, entryName, CompressionLevel.Optimal);
        }

        private int AddDirectoryToZip(ZipArchive archive, string sourceDir, string zipFolderName)
        {
            int count = 0;
            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = GetRelativePath(sourceDir, file);
                string entryName = Path.Combine(zipFolderName, relativePath).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                count++;
            }
            return count;
        }

        private string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            Uri baseUri = new Uri(basePath);
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        private string SerializeManifest(Dictionary<string, string> manifest)
        {
            // Simple JSON serializer — no external dependencies needed
            var entries = manifest.Select(kv => $"  \"{EscapeJson(kv.Key)}\": \"{EscapeJson(kv.Value)}\"");
            return "{\n" + string.Join(",\n", entries) + "\n}";
        }

        private string EscapeJson(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") ?? "";
        }

        #endregion

        public override void Dispose()
        {
            gui?.TryClose();
            gui?.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// Files the game rewrites from memory on shutdown (clientsettings.json,
    /// serverconfig.json) can't simply be overwritten during play — the shutdown
    /// save would undo the restore. Staged copies are written over the targets at
    /// process exit, after the game has done its own save.
    /// </summary>
    internal static class PendingFileRestores
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> Pending = new Dictionary<string, string>();
        private static bool hooked;

        public static void Stage(string targetPath, string stagedPath, ILogger logger)
        {
            lock (Sync)
            {
                Pending[targetPath] = stagedPath;
                if (!hooked)
                {
                    AppDomain.CurrentDomain.ProcessExit += (s, e) => Flush();
                    hooked = true;
                }
            }
            logger?.Notification($"[ConfigKeeper] Staged {Path.GetFileName(targetPath)} for restore at game exit");
        }

        private static void Flush()
        {
            lock (Sync)
            {
                foreach (var kv in Pending)
                {
                    try
                    {
                        File.Copy(kv.Value, kv.Key, true);
                        File.Delete(kv.Value);
                    }
                    catch
                    {
                        // Process is exiting — nowhere sane to report; the .ckpending
                        // file is left behind and resumed on next start.
                    }
                }
                Pending.Clear();
            }
        }
    }

    #region Data Classes

    public class ExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string FilePath { get; set; } = "";
        public List<string> ItemsExported { get; set; } = new List<string>();
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool RequiresRestart { get; set; }
        public List<string> ItemsImported { get; set; } = new List<string>();
    }

    public class BackupInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public DateTime Created { get; set; }

        public string DisplayName => $"{FileName} ({FormatSize(FileSize)}) - {Created:yyyy-MM-dd HH:mm}";

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1048576) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / 1048576.0:F1} MB";
        }
    }

    #endregion
}
