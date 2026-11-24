using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopMemo
{
    public static class AppMeta
    {
        public const string Version = "2.05";
        public const string VersionTag = "v2.05";
    }

    public class DeadlineDisplayInfo
    {
        public string Text { get; set; }
        public int UrgencyLevel { get; set; } // 0=None, 1=Normal, 2=Soon(<24h), 3=Urgent(<3h), 4=Overdue, 5=Done
        public string BgHex { get; set; }
        public string FgHex { get; set; }

        public DeadlineDisplayInfo()
        {
            Text = "";
            UrgencyLevel = 0;
            BgHex = "#E5E7EB";
            FgHex = "#4B5563";
        }
    }

    public class MemoTask
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Notes { get; set; }
        public string DeadlineIso { get; set; }
        public string RemindAtIso { get; set; }
        public bool Reminded { get; set; }
        public int Quadrant { get; set; } // 0=Unclassified, 1=Q1, 2=Q2, 3=Q3, 4=Q4
        public int AiScore { get; set; }
        public string AiReason { get; set; }
        public bool Completed { get; set; }
        public string CompletedAtIso { get; set; }
        public string CreatedAtIso { get; set; }
        public string NoteId { get; set; }
        public List<string> AttachmentFiles { get; set; }

        public MemoTask()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            Title = "";
            Notes = "";
            DeadlineIso = "";
            RemindAtIso = "";
            Reminded = false;
            Quadrant = 0;
            AiScore = 0;
            AiReason = "";
            Completed = false;
            CompletedAtIso = "";
            CreatedAtIso = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            NoteId = "";
            AttachmentFiles = new List<string>();
        }

        public DateTime? GetDeadline()
        {
            return ParseIsoDateTime(DeadlineIso);
        }

        public DateTime? GetRemindAt()
        {
            return ParseIsoDateTime(RemindAtIso);
        }

        public DateTime GetCreatedAt()
        {
            DateTime? dt = ParseIsoDateTime(CreatedAtIso);
            return dt.HasValue ? dt.Value : DateTime.MinValue;
        }

        public DeadlineDisplayInfo GetDeadlineDisplay(DateTime now)
        {
            return FormatDeadlineCountdown(GetDeadline(), Completed, now);
        }

        public static DateTime? ParseIsoDateTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            DateTime dt;
            string[] formats = new string[]
            {
                "yyyy-MM-dd HH:mm",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-dd"
            };
            if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
            {
                return dt;
            }
            if (DateTime.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
            {
                return dt;
            }
            return null;
        }

        public static string FormatIso(DateTime? dt)
        {
            if (!dt.HasValue) return "";
            return dt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public static DeadlineDisplayInfo FormatDeadlineCountdown(DateTime? ddl, bool completed, DateTime now)
        {
            var info = new DeadlineDisplayInfo();
            if (!ddl.HasValue)
            {
                info.Text = "";
                info.UrgencyLevel = 0;
                return info;
            }

            DateTime target = ddl.Value;
            string dateLabel = target.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (completed)
            {
                info.Text = "已完成 · " + dateLabel;
                info.UrgencyLevel = 5;
                info.BgHex = "#DCFCE7";
                info.FgHex = "#166534";
                return info;
            }

            TimeSpan diff = target - now;
            if (diff.TotalSeconds < 0)
            {
                TimeSpan over = now - target;
                string overText;
                if (over.TotalMinutes < 60)
                {
                    int mins = Math.Max(1, (int)Math.Ceiling(over.TotalMinutes));
                    overText = string.Format("已逾期 {0}分钟", mins);
                }
                else if (over.TotalHours < 24)
                {
                    int hrs = (int)Math.Floor(over.TotalHours);
                    int mins = over.Minutes;
                    overText = mins > 0
                        ? string.Format("已逾期 {0}小时{1}分", hrs, mins)
                        : string.Format("已逾期 {0}小时", hrs);
                }
                else
                {
                    int days = (int)Math.Floor(over.TotalDays);
                    overText = string.Format("已逾期 {0}天", days);
                }
                info.Text = overText + " (" + dateLabel + ")";
                info.UrgencyLevel = 4;
                info.BgHex = "#FEE2E2";
                info.FgHex = "#B91C1C";
                return info;
            }

            if (diff.TotalHours < 3)
            {
                string leftText;
                if (diff.TotalMinutes < 60)
                {
                    int mins = Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes));
                    leftText = string.Format("剩 {0}分钟", mins);
                }
                else
                {
                    int hrs = (int)Math.Floor(diff.TotalHours);
                    int mins = diff.Minutes;
                    leftText = mins > 0
                        ? string.Format("剩 {0}小时{1}分", hrs, mins)
                        : string.Format("剩 {0}小时", hrs);
                }
                info.Text = leftText + " (" + target.ToString("HH:mm", CultureInfo.InvariantCulture) + ")";
                info.UrgencyLevel = 3;
                info.BgHex = "#FEE2E2";
                info.FgHex = "#DC2626";
                return info;
            }

            if (diff.TotalHours < 24)
            {
                int hrs = (int)Math.Floor(diff.TotalHours);
                int mins = diff.Minutes;
                string prefix = target.Date == now.Date ? "今天" : "明天";
                string leftText = mins > 0
                    ? string.Format("剩 {0}小时{1}分", hrs, mins)
                    : string.Format("剩 {0}小时", hrs);
                info.Text = string.Format("{0} ({1} {2})", leftText, prefix, target.ToString("HH:mm", CultureInfo.InvariantCulture));
                info.UrgencyLevel = 2;
                info.BgHex = "#FEF3C7";
                info.FgHex = "#B45309";
                return info;
            }

            int dayDiff = (target.Date - now.Date).Days;
            if (dayDiff == 1)
            {
                info.Text = string.Format("明天 {0} (剩{1}天)", target.ToString("HH:mm", CultureInfo.InvariantCulture), dayDiff);
                info.UrgencyLevel = 2;
                info.BgHex = "#FEF3C7";
                info.FgHex = "#B45309";
                return info;
            }

            info.Text = string.Format("{0} (剩{1}天)", dateLabel, dayDiff);
            info.UrgencyLevel = 1;
            info.BgHex = "#DBEAFE";
            info.FgHex = "#1D4ED8";
            return info;
        }

        public static int ClampQuadrant(int q)
        {
            if (q < 1) return 1;
            if (q > 4) return 4;
            return q;
        }

        public static int NormalizeQuadrant(int q)
        {
            if (q < 0) return 0;
            if (q > 4) return 4;
            return q;
        }

        public static string GetQuadrantName(int q)
        {
            switch (q)
            {
                case 0: return "未分类（独立便签）";
                case 1: return "Q1 重要且紧急";
                case 2: return "Q2 重要不紧急";
                case 3: return "Q3 紧急不重要";
                case 4: return "Q4 不重要不紧急";
                default: return "未分类（独立便签）";
            }
        }

        public static string GetQuadrantShort(int q)
        {
            switch (q)
            {
                case 0: return "未分类";
                case 1: return "Q1 立刻做";
                case 2: return "Q2 计划做";
                case 3: return "Q3 尽快办";
                case 4: return "Q4 稍后做";
                default: return "未分类";
            }
        }
    }

    public class StickyNote
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public bool ShowScratchpad { get; set; }
        public string FilterMode { get; set; } // custom, all, q1, q2, q3, q4
        public string Theme { get; set; }
        public double Opacity { get; set; }
        public double GlassTransparency { get; set; }
        public double FontSize { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string PinMode { get; set; } // desktop, topmost, normal
        public bool Locked { get; set; }
        public bool Collapsed { get; set; }
        public bool Visible { get; set; }

        public StickyNote()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            Title = "桌面便签";
            Content = "";
            ShowScratchpad = false;
            FilterMode = "custom";
            Theme = "claude_warm";
            Opacity = 0.96;
            GlassTransparency = 0.96;
            FontSize = 13;
            Left = 60;
            Top = 60;
            Width = 320;
            Height = 360;
            PinMode = "desktop";
            Locked = false;
            Collapsed = false;
            Visible = true;
        }

        public static string GetFilterModeLabel(string filterMode)
        {
            switch (filterMode)
            {
                case "all": return "全部日程";
                case "q1": return "Q1 重要紧急";
                case "q2": return "Q2 重要不紧急";
                case "q3": return "Q3 紧急不重要";
                case "q4": return "Q4 不重要不紧急";
                default: return "独立便签";
            }
        }
    }

    public class AppData
    {
        public int SchemaVersion { get; set; }
        public List<MemoTask> Tasks { get; set; }
        public List<StickyNote> StickyNotes { get; set; }

        public AppData()
        {
            SchemaVersion = 2;
            Tasks = new List<MemoTask>();
            StickyNotes = new List<StickyNote>();
        }
    }

    public class AppConfig
    {
        public string DisplayMode { get; set; } // both, panel, sticky
        public double PanelLeft { get; set; }
        public double PanelTop { get; set; }
        public double PanelWidth { get; set; }
        public double PanelHeight { get; set; }
        public string PanelTheme { get; set; }
        public double PanelOpacity { get; set; }
        public double GlassTransparency { get; set; }
        public string GlassBackdropMode { get; set; } // liquid_acrylic, blur_clear, win11_acrylic, win11_mica
        public double PanelFontSize { get; set; }
        public string UiScale { get; set; } // compact, standard, large
        public string PanelPinMode { get; set; } // desktop, topmost, normal
        public bool PanelLocked { get; set; }
        public bool PanelCollapsed { get; set; }
        public bool PanelVisible { get; set; }

        public string SortBy { get; set; } // ai, ddl, quadrant, created
        public bool AutoStart { get; set; }
        public bool HotkeysEnabled { get; set; }
        public bool ReminderSoundEnabled { get; set; }

        public string AiProviderPreset { get; set; }
        public string AiBaseUrl { get; set; }
        public string AiApiKey { get; set; }
        public string AiModel { get; set; }
        public string AiCustomInstruction { get; set; }

        // WeChatExportPlus Integration
        public string WeChatExportExePath { get; set; }
        public string WeChatExportOutputDir { get; set; }

        // Web Server & Remote Sync
        public int ServerPort { get; set; }
        public string ServerPassword { get; set; }
        public bool RemoteSyncEnabled { get; set; }
        public string RemoteServerUrl { get; set; }
        public string RemoteServerPassword { get; set; }

        public AppConfig()
        {
            DisplayMode = "both";
            PanelLeft = 400;
            PanelTop = 64;
            PanelWidth = 890;
            PanelHeight = 630;
            PanelTheme = "claude_warm";
            PanelOpacity = 0.96;
            GlassTransparency = 0.96;
            GlassBackdropMode = "flat";
            PanelFontSize = 13;
            UiScale = "standard";
            PanelPinMode = "desktop";
            PanelLocked = false;
            PanelCollapsed = false;
            PanelVisible = true;

            SortBy = "ai";
            AutoStart = false;
            HotkeysEnabled = true;
            ReminderSoundEnabled = true;

            AiProviderPreset = "deepseek_flash";
            AiBaseUrl = "https://api.deepseek.com/v1";
            AiApiKey = "";
            AiModel = "deepseek-flash";
            AiCustomInstruction = "";
            WeChatExportExePath = "";
            WeChatExportOutputDir = "";

            ServerPort = 8765;
            ServerPassword = "";
            RemoteSyncEnabled = false;
            RemoteServerUrl = "";
            RemoteServerPassword = "";
        }
    }

    public class AiSortItem
    {
        public string Id { get; set; }
        public int Quadrant { get; set; }
        public int Score { get; set; }
        public string Reason { get; set; }

        public AiSortItem()
        {
            Id = "";
            Quadrant = 2;
            Score = 50;
            Reason = "";
        }
    }

    public class AiExtractedTask
    {
        public string Title { get; set; }
        public string Notes { get; set; }
        public string DeadlineIso { get; set; }
        public string RemindAtIso { get; set; }
        public int Quadrant { get; set; }
        public string Reason { get; set; }
        public bool Selected { get; set; }

        public AiExtractedTask()
        {
            Title = "";
            Notes = "";
            DeadlineIso = "";
            RemindAtIso = "";
            Quadrant = 0;
            Reason = "";
            Selected = true;
        }
    }

    public class AiSortSnapshotItem
    {
        public string Id { get; set; }
        public int Quadrant { get; set; }
        public int AiScore { get; set; }
        public string AiReason { get; set; }
    }

    public class AiSortSnapshot
    {
        public DateTime CreatedAt { get; set; }
        public string PreviousSortBy { get; set; }
        public List<AiSortSnapshotItem> Items { get; set; }

        public AiSortSnapshot()
        {
            CreatedAt = DateTime.Now;
            PreviousSortBy = "ai";
            Items = new List<AiSortSnapshotItem>();
        }
    }
}
