using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DesktopMemo
{
    public class StorageService
    {
        public const string StoragePathIniName = "storage_path.ini";
        public const string RegistryConfigKeyPath = @"Software\DesktopMemo";

        public string BaseDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string BackupsDirectory { get; private set; }
        public string AttachmentsDirectory { get; private set; }
        public string DataFilePath { get; private set; }
        public string ConfigFilePath { get; private set; }
        public string ErrorLogPath { get; private set; }

        private readonly object _fileLock = new object();

        public StorageService(string customBaseDirectory) : this(customBaseDirectory, null)
        {
        }

        public StorageService(string customBaseDirectory, string explicitDataDirectory)
        {
            if (!string.IsNullOrWhiteSpace(customBaseDirectory))
            {
                BaseDirectory = customBaseDirectory;
            }
            else
            {
                BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            DataDirectory = ResolveDataDirectory(customBaseDirectory, explicitDataDirectory);
            BackupsDirectory = Path.Combine(DataDirectory, "backups");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            DataFilePath = Path.Combine(DataDirectory, "data.json");
            ConfigFilePath = Path.Combine(DataDirectory, "config.json");
            ErrorLogPath = Path.Combine(DataDirectory, "error.log");

            EnsureDirectories();
        }

        public static string ResolveDataDirectory(string customBaseDirectory, string explicitDataDirectory)
        {
            if (!string.IsNullOrWhiteSpace(explicitDataDirectory))
            {
                return Path.GetFullPath(explicitDataDirectory.Trim().Trim('"'));
            }
            if (!string.IsNullOrWhiteSpace(customBaseDirectory))
            {
                return Path.Combine(customBaseDirectory.Trim().Trim('"'), "data");
            }

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                string iniPath = Path.Combine(exeDir, StoragePathIniName);
                if (File.Exists(iniPath))
                {
                    string[] lines = File.ReadAllLines(iniPath, new UTF8Encoding(false));
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = (lines[i] ?? "").Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                        if (line.StartsWith("DataDir=", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = line.Substring("DataDir=".Length).Trim().Trim('"');
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                return Path.IsPathRooted(val)
                                    ? Path.GetFullPath(val)
                                    : Path.GetFullPath(Path.Combine(exeDir, val));
                            }
                        }
                    }
                }
            }
            catch { }

            string defaultLocalData = Path.Combine(exeDir, "data");
            bool hasPortableLocal = File.Exists(Path.Combine(defaultLocalData, "data.json"))
                || File.Exists(Path.Combine(defaultLocalData, "config.json"));
            if (!hasPortableLocal)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryConfigKeyPath, false))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("DataDir");
                            if (val != null)
                            {
                                string regDir = val.ToString().Trim().Trim('"');
                                if (!string.IsNullOrWhiteSpace(regDir))
                                {
                                    return Path.GetFullPath(regDir);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return defaultLocalData;
        }

        public void EnsureDirectories()
        {
            if (!Directory.Exists(DataDirectory))
            {
                Directory.CreateDirectory(DataDirectory);
            }
            if (!Directory.Exists(BackupsDirectory))
            {
                Directory.CreateDirectory(BackupsDirectory);
            }
            if (!Directory.Exists(AttachmentsDirectory))
            {
                Directory.CreateDirectory(AttachmentsDirectory);
            }
        }

        public AppData LoadData()
        {
            lock (_fileLock)
            {
                EnsureDirectories();
                if (!File.Exists(DataFilePath))
                {
                    AppData starter = CreateDefaultData();
                    SaveData(starter);
                    return starter;
                }

                try
                {
                    string json = File.ReadAllText(DataFilePath, new UTF8Encoding(false));
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        AppData starter = CreateDefaultData();
                        SaveData(starter);
                        return starter;
                    }
                    var ser = CreateSerializer();
                    AppData data = ser.Deserialize<AppData>(json);
                    if (data == null) data = new AppData();
                    if (data.Tasks == null) data.Tasks = new List<MemoTask>();
                    if (data.StickyNotes == null) data.StickyNotes = new List<StickyNote>();
                    if (data.Timetable == null) data.Timetable = new TimetableData();
                    if (data.Timetable.TotalWeeks < 1 || data.Timetable.TotalWeeks > 30) data.Timetable.TotalWeeks = 20;
                    if (data.Timetable.SectionsPerDay < 0 || data.Timetable.SectionsPerDay > 16) data.Timetable.SectionsPerDay = 0;
                    if (data.Timetable.RowMinHeight < 36 || data.Timetable.RowMinHeight > 80) data.Timetable.RowMinHeight = 52;
                    if (data.Timetable.Courses == null) data.Timetable.Courses = new List<CourseItem>();
                    if (data.Timetable.SectionTimes == null) data.Timetable.SectionTimes = new List<SectionTimeSlot>();
                    var cleanedSlots = new List<SectionTimeSlot>();
                    for (int i = 0; i < data.Timetable.SectionTimes.Count; i++)
                    {
                        SectionTimeSlot st = data.Timetable.SectionTimes[i];
                        if (st == null || st.Section < 1 || st.Section > 16) continue;
                        st.StartTime = CourseItem.NormalizeTimeToken(st.StartTime);
                        st.EndTime = CourseItem.NormalizeTimeToken(st.EndTime);
                        if (!string.IsNullOrEmpty(st.StartTime) || !string.IsNullOrEmpty(st.EndTime))
                        {
                            cleanedSlots.Add(st);
                        }
                    }
                    cleanedSlots.Sort(delegate(SectionTimeSlot a, SectionTimeSlot b) { return a.Section.CompareTo(b.Section); });
                    data.Timetable.SectionTimes = cleanedSlots;

                    for (int i = 0; i < data.Timetable.Courses.Count; i++)
                    {
                        CourseItem c = data.Timetable.Courses[i];
                        if (string.IsNullOrEmpty(c.Id)) c.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
                        if (c.Name == null) c.Name = "";
                        if (c.Teacher == null) c.Teacher = "";
                        if (c.Location == null) c.Location = "";
                        if (c.DayOfWeek < 1 || c.DayOfWeek > 7) c.DayOfWeek = 1;
                        if (c.StartSection < 1) c.StartSection = 1;
                        if (c.StartSection > 16) c.StartSection = 16;
                        if (c.EndSection < c.StartSection) c.EndSection = c.StartSection;
                        if (c.EndSection > 16) c.EndSection = 16;
                        c.WeekType = CourseItem.NormalizeWeekType(c.WeekType);
                        if (c.CustomWeeks == null) c.CustomWeeks = new List<int>();
                        if (c.ExcludedWeeks == null) c.ExcludedWeeks = new List<int>();
                        if (string.IsNullOrEmpty(c.ColorHex)) c.ColorHex = "#3B82F6";
                    }

                    for (int i = 0; i < data.Tasks.Count; i++)
                    {
                        MemoTask t = data.Tasks[i];
                        if (string.IsNullOrEmpty(t.Id)) t.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
                        // Preserve 0 (unclassified) for independent sticky note items!
                        t.Quadrant = MemoTask.NormalizeQuadrant(t.Quadrant);
                        if (t.Title == null) t.Title = "";
                        if (t.Notes == null) t.Notes = "";
                        if (t.AiReason == null) t.AiReason = "";
                        if (t.NoteId == null) t.NoteId = "";
                        if (t.AttachmentFiles == null)
                        {
                            t.AttachmentFiles = new List<string>();
                        }
                        else
                        {
                            var cleanedAtt = new List<string>();
                            for (int j = 0; j < t.AttachmentFiles.Count; j++)
                            {
                                string safeAtt = SanitizeAttachmentFileName(t.AttachmentFiles[j]);
                                if (!string.IsNullOrEmpty(safeAtt) && !cleanedAtt.Contains(safeAtt))
                                {
                                    cleanedAtt.Add(safeAtt);
                                }
                            }
                            t.AttachmentFiles = cleanedAtt;
                        }
                    }
                    for (int i = 0; i < data.StickyNotes.Count; i++)
                    {
                        StickyNote n = data.StickyNotes[i];
                        if (string.IsNullOrEmpty(n.Id)) n.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
                        if (string.IsNullOrEmpty(n.FilterMode)) n.FilterMode = "custom";
                        n.Theme = NormalizeThemeName(n.Theme);
                        if (string.IsNullOrEmpty(n.PinMode)) n.PinMode = "desktop";
                        if (n.Opacity < 0.60 || n.Opacity > 1.0) n.Opacity = 0.96;
                        if (n.FontSize < 10 || n.FontSize > 24) n.FontSize = 13;
                        if (n.Width < 220) n.Width = 320;
                        if (n.Height < 160) n.Height = 360;
                    }
                    return data;
                }
                catch (Exception ex)
                {
                    LogError("LoadData failed", ex);
                    try
                    {
                        string badPath = Path.Combine(BackupsDirectory, "corrupt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
                        File.Copy(DataFilePath, badPath, true);
                    }
                    catch { }
                    return new AppData();
                }
            }
        }

        public void SaveData(AppData data)
        {
            if (data == null) return;
            lock (_fileLock)
            {
                EnsureDirectories();
                data.SchemaVersion = 2;
                var ser = CreateSerializer();
                string raw = ser.Serialize(data);
                string pretty = FormatJson(raw);
                WriteAllTextAtomic(DataFilePath, pretty);
            }
        }

        public AppConfig LoadConfig()
        {
            lock (_fileLock)
            {
                EnsureDirectories();
                if (!File.Exists(ConfigFilePath))
                {
                    AppConfig cfg = new AppConfig();
                    SaveConfig(cfg);
                    return cfg;
                }

                try
                {
                    string json = File.ReadAllText(ConfigFilePath, new UTF8Encoding(false));
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        AppConfig cfg = new AppConfig();
                        SaveConfig(cfg);
                        return cfg;
                    }
                    var ser = CreateSerializer();
                    AppConfig loaded = ser.Deserialize<AppConfig>(json);
                    if (loaded == null) loaded = new AppConfig();
                    NormalizeConfig(loaded);
                    return loaded;
                }
                catch (Exception ex)
                {
                    LogError("LoadConfig failed", ex);
                    return new AppConfig();
                }
            }
        }

        public void SaveConfig(AppConfig config)
        {
            if (config == null) return;
            lock (_fileLock)
            {
                EnsureDirectories();
                var ser = CreateSerializer();
                string raw = ser.Serialize(config);
                string pretty = FormatJson(raw);
                WriteAllTextAtomic(ConfigFilePath, pretty);
            }
        }

        public string CreateBackup()
        {
            lock (_fileLock)
            {
                EnsureDirectories();
                if (!File.Exists(DataFilePath)) return "";
                string name = "data_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
                string dest = Path.Combine(BackupsDirectory, name);
                File.Copy(DataFilePath, dest, true);

                try
                {
                    var files = new DirectoryInfo(BackupsDirectory)
                        .GetFiles("data_*.json")
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .ToList();
                    for (int i = 20; i < files.Count; i++)
                    {
                        files[i].Delete();
                    }
                }
                catch { }
                return dest;
            }
        }

        public void LogError(string context, Exception ex)
        {
            try
            {
                EnsureDirectories();
                string msg = string.Format("[{0}] {1}: {2}\r\n{3}\r\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    context,
                    ex != null ? ex.Message : "",
                    ex != null ? ex.StackTrace : "");
                File.AppendAllText(ErrorLogPath, msg, new UTF8Encoding(false));
            }
            catch { }
        }

        private static string NormalizeThemeName(string theme)
        {
            if (string.IsNullOrWhiteSpace(theme)) return "claude_warm";
            switch (theme)
            {
                case "warm_editorial":
                case "warm_yellow":
                case "claude_warm":
                    return "claude_warm";
                case "frost_glass":
                case "snow_white":
                case "notion_light":
                    return "notion_light";
                case "obsidian_glass":
                case "dark_slate":
                case "linear_dark":
                    return "linear_dark";
                case "mist_blue_glass":
                case "mist_blue":
                case "nord_slate":
                    return "nord_slate";
                case "sage_glass":
                case "mint_green":
                case "matcha_flat":
                    return "matcha_flat";
                case "rose_quartz_glass":
                case "sakura_pink":
                case "rose_flat":
                    return "rose_flat";
                default:
                    return "claude_warm";
            }
        }

        private static void NormalizeConfig(AppConfig cfg)
        {
            if (cfg.DisplayMode != "panel" && cfg.DisplayMode != "sticky" && cfg.DisplayMode != "both")
                cfg.DisplayMode = "both";
            cfg.PanelTheme = NormalizeThemeName(cfg.PanelTheme);
            if (cfg.PanelOpacity < 0.60 || cfg.PanelOpacity > 1.0) cfg.PanelOpacity = 0.96;
            if (cfg.PanelFontSize < 11 || cfg.PanelFontSize > 20) cfg.PanelFontSize = 13;
            if (cfg.UiScale != "compact" && cfg.UiScale != "standard" && cfg.UiScale != "large")
                cfg.UiScale = "standard";
            if (cfg.PanelWidth < 540) cfg.PanelWidth = 890;
            if (cfg.PanelHeight < 420) cfg.PanelHeight = 630;
            if (cfg.PanelPinMode != "desktop" && cfg.PanelPinMode != "topmost" && cfg.PanelPinMode != "normal")
                cfg.PanelPinMode = "desktop";
            if (string.IsNullOrEmpty(cfg.SortBy)) cfg.SortBy = "ai";
            if (cfg.HotkeyNewTask == null) cfg.HotkeyNewTask = "Ctrl+Alt+N";
            if (cfg.HotkeyToggleAll == null) cfg.HotkeyToggleAll = "Ctrl+Alt+M";
            if (cfg.HotkeyShowPanel == null) cfg.HotkeyShowPanel = "Ctrl+Alt+P";
            if (cfg.HotkeyNewSticky == null) cfg.HotkeyNewSticky = "Ctrl+Alt+S";
            if (cfg.HotkeyVoiceMemo == null) cfg.HotkeyVoiceMemo = "Ctrl+Alt+V";
            if (cfg.VoiceTriggerMode != "hold") cfg.VoiceTriggerMode = "toggle";
            if (string.IsNullOrEmpty(cfg.AiProviderPreset)) cfg.AiProviderPreset = "deepseek_flash";
            if (string.IsNullOrEmpty(cfg.AiBaseUrl)) cfg.AiBaseUrl = "https://api.deepseek.com/v1";
            if (string.IsNullOrEmpty(cfg.AiModel)) cfg.AiModel = "deepseek-flash";
            if (cfg.AiApiKey == null) cfg.AiApiKey = "";
            if (cfg.AiCustomInstruction == null) cfg.AiCustomInstruction = "";
            if (cfg.MisUrl == null) cfg.MisUrl = "";
            if (cfg.WeChatExportExePath == null) cfg.WeChatExportExePath = "";
            if (cfg.WeChatExportOutputDir == null) cfg.WeChatExportOutputDir = "";
            if (cfg.ServerPort < 1024 || cfg.ServerPort > 65535) cfg.ServerPort = 8765;
            if (cfg.ServerPassword == null) cfg.ServerPassword = "";
            if (cfg.RemoteServerUrl == null) cfg.RemoteServerUrl = "";
            if (cfg.RemoteServerPassword == null) cfg.RemoteServerPassword = "";
        }

        public static string SanitizeAttachmentFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string fileOnly = Path.GetFileName(name.Trim());
            if (string.IsNullOrWhiteSpace(fileOnly) || fileOnly.IndexOf("..", StringComparison.Ordinal) >= 0) return "";
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < bad.Length; i++)
            {
                if (fileOnly.IndexOf(bad[i]) >= 0) return "";
            }
            return fileOnly;
        }

        public string GetAttachmentPath(string fileName)
        {
            string safe = SanitizeAttachmentFileName(fileName);
            if (string.IsNullOrEmpty(safe)) return "";
            return Path.Combine(AttachmentsDirectory, safe);
        }

        public bool AttachmentExists(string fileName)
        {
            string p = GetAttachmentPath(fileName);
            return !string.IsNullOrEmpty(p) && File.Exists(p);
        }

        public byte[] ReadAttachmentBytes(string fileName)
        {
            string p = GetAttachmentPath(fileName);
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
            lock (_fileLock)
            {
                try { return File.ReadAllBytes(p); }
                catch { return null; }
            }
        }

        public string SaveAttachmentBytes(byte[] bytes, string preferredExt)
        {
            if (bytes == null || bytes.Length == 0) return "";
            string ext = string.IsNullOrWhiteSpace(preferredExt) ? ".jpg" : preferredExt.Trim().ToLowerInvariant();
            if (!ext.StartsWith(".")) ext = "." + ext;
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".gif" && ext != ".webp") ext = ".jpg";
            string name = "att_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext;
            return SaveAttachmentWithExactName(name, bytes);
        }

        public string SaveAttachmentWithExactName(string fileName, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            string safe = SanitizeAttachmentFileName(fileName);
            if (string.IsNullOrEmpty(safe)) return SaveAttachmentBytes(bytes, ".jpg");
            lock (_fileLock)
            {
                EnsureDirectories();
                string dest = Path.Combine(AttachmentsDirectory, safe);
                WriteAllBytesAtomic(dest, bytes);
                return safe;
            }
        }

        private static void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path))
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 32 * 1024 * 1024;
            return ser;
        }

        private static void WriteAllTextAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        public static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "{}";
            var sb = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (inString)
                {
                    sb.Append(ch);
                    if (escaped) escaped = false;
                    else if (ch == '\\') escaped = true;
                    else if (ch == '"') inString = false;
                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inString = true;
                        sb.Append(ch);
                        break;
                    case '{':
                    case '[':
                        sb.Append(ch);
                        if (i + 1 < json.Length && json[i + 1] != '}' && json[i + 1] != ']')
                        {
                            sb.Append("\r\n");
                            indent++;
                            AppendIndent(sb, indent);
                        }
                        break;
                    case '}':
                    case ']':
                        if (i > 0 && json[i - 1] != '{' && json[i - 1] != '[')
                        {
                            sb.Append("\r\n");
                            indent = Math.Max(0, indent - 1);
                            AppendIndent(sb, indent);
                        }
                        else
                        {
                            indent = Math.Max(0, indent - 1);
                        }
                        sb.Append(ch);
                        break;
                    case ',':
                        sb.Append(",\r\n");
                        AppendIndent(sb, indent);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(ch)) sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        private static void AppendIndent(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++) sb.Append("  ");
        }

        public static AppData CreateDefaultData()
        {
            DateTime now = DateTime.Now;
            DateTime todayEvening = now.Date.AddHours(22);
            if (todayEvening <= now) todayEvening = now.AddHours(2);

            DateTime tomorrowNoon = now.Date.AddDays(1).AddHours(12);
            DateTime inThreeDays = now.Date.AddDays(3).AddHours(20);

            var data = new AppData();

            var note1 = new StickyNote();
            note1.Title = "桌面灵感便签";
            note1.Content = "";
            note1.ShowScratchpad = false;
            note1.FilterMode = "custom";
            note1.Theme = "claude_warm";
            note1.Opacity = 0.96;
            note1.Left = 48;
            note1.Top = 64;
            note1.Width = 320;
            note1.Height = 360;
            note1.PinMode = "desktop";
            data.StickyNotes.Add(note1);

            var t1 = new MemoTask();
            t1.Title = "完成今日最紧急的核心任务（示例）";
            t1.Notes = "双击可修改 DDL、提醒时间与四象限";
            t1.Quadrant = 1;
            t1.DeadlineIso = MemoTask.FormatIso(todayEvening);
            data.Tasks.Add(t1);

            var t2 = new MemoTask();
            t2.Title = "制定本周学习/项目进度规划";
            t2.Notes = "可在「AI 与设置」填入 DeepSeek Key，点击「AI 智能排序」自动归类";
            t2.Quadrant = 2;
            t2.DeadlineIso = MemoTask.FormatIso(inThreeDays);
            data.Tasks.Add(t2);

            var t3 = new MemoTask();
            t3.Title = "回复消息与报销/材料确认";
            t3.Notes = "";
            t3.Quadrant = 3;
            t3.DeadlineIso = MemoTask.FormatIso(tomorrowNoon);
            data.Tasks.Add(t3);

            var t4 = new MemoTask();
            t4.Title = "整理桌面文件与常用工具书签";
            t4.Notes = "有空余时间再顺手清理";
            t4.Quadrant = 4;
            data.Tasks.Add(t4);

            var tNote = new MemoTask();
            tNote.Title = "尝试粘贴一段微信聊天记录用 💬 AI 提取";
            tNote.Notes = "";
            tNote.Quadrant = 0; // Unclassified on independent sticky note!
            tNote.NoteId = note1.Id;
            data.Tasks.Add(tNote);

            return data;
        }
    }
}
