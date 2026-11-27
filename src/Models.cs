using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopMemo
{
    public static class AppMeta
    {
        public const string Version = "2.11";
        public const string VersionTag = "v2.11";
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
                case "timetable": return "今日课表";
                case "q1": return "Q1 重要紧急";
                case "q2": return "Q2 重要不紧急";
                case "q3": return "Q3 紧急不重要";
                case "q4": return "Q4 不重要不紧急";
                default: return "独立便签";
            }
        }
    }

    public class SectionTimeSlot
    {
        public int Section { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }

        public SectionTimeSlot()
        {
            Section = 1;
            StartTime = "";
            EndTime = "";
        }
    }

    public class CourseItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Teacher { get; set; }
        public string Location { get; set; }
        public int DayOfWeek { get; set; } // 1=Mon..7=Sun
        public int StartSection { get; set; } // 1..12
        public int EndSection { get; set; } // 1..12
        public string StartTime { get; set; } // HH:mm
        public string EndTime { get; set; } // HH:mm
        public int StartWeek { get; set; }
        public int EndWeek { get; set; }
        public string WeekType { get; set; } // all, odd, even
        public List<int> CustomWeeks { get; set; }
        public List<int> ExcludedWeeks { get; set; }
        public string ColorHex { get; set; }

        public CourseItem()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            Name = "";
            Teacher = "";
            Location = "";
            DayOfWeek = 1;
            StartSection = 1;
            EndSection = 2;
            StartTime = "";
            EndTime = "";
            StartWeek = 1;
            EndWeek = 16;
            WeekType = "all";
            CustomWeeks = new List<int>();
            ExcludedWeeks = new List<int>();
            ColorHex = "#3B82F6";
        }

        public bool OccursInWeek(int week)
        {
            if (week < 1) return false;
            if (ExcludedWeeks != null && ExcludedWeeks.Contains(week)) return false;
            if (CustomWeeks != null && CustomWeeks.Count > 0)
            {
                return CustomWeeks.Contains(week);
            }
            int sw = StartWeek > 0 ? StartWeek : 1;
            int ew = EndWeek >= sw ? EndWeek : sw;
            if (week < sw || week > ew) return false;
            if (WeekType == "odd") return (week % 2) == 1;
            if (WeekType == "even") return (week % 2) == 0;
            return true;
        }

        public bool IsSingleWeekOnly(int week)
        {
            if (week < 1) return false;
            if (CustomWeeks != null && CustomWeeks.Count > 0)
            {
                return CustomWeeks.Count == 1 && CustomWeeks[0] == week;
            }
            return StartWeek == week && EndWeek == week;
        }

        public CourseItem CloneForSingleWeek(int week, int newDay, int newStartSection, int newEndSection)
        {
            return new CourseItem
            {
                Name = Name ?? "",
                Teacher = Teacher ?? "",
                Location = Location ?? "",
                DayOfWeek = Math.Max(1, Math.Min(7, newDay)),
                StartSection = Math.Max(1, Math.Min(16, newStartSection)),
                EndSection = Math.Max(Math.Max(1, Math.Min(16, newStartSection)), Math.Min(16, newEndSection)),
                StartTime = "",
                EndTime = "",
                StartWeek = week,
                EndWeek = week,
                WeekType = "all",
                CustomWeeks = new List<int>(),
                ExcludedWeeks = new List<int>(),
                ColorHex = string.IsNullOrEmpty(ColorHex) ? "#3B82F6" : ColorHex
            };
        }

        public static string DefaultSectionStartTime(int section)
        {
            switch (section)
            {
                case 1: return "08:00";
                case 2: return "08:55";
                case 3: return "10:00";
                case 4: return "10:55";
                case 5: return "14:00";
                case 6: return "14:55";
                case 7: return "16:00";
                case 8: return "16:55";
                case 9: return "19:00";
                case 10: return "19:55";
                case 11: return "20:50";
                case 12: return "21:40";
                case 13: return "22:30";
                case 14: return "23:20";
                default: return "08:00";
            }
        }

        public static string DefaultSectionEndTime(int section)
        {
            switch (section)
            {
                case 1: return "08:45";
                case 2: return "09:40";
                case 3: return "10:45";
                case 4: return "11:40";
                case 5: return "14:45";
                case 6: return "15:40";
                case 7: return "16:45";
                case 8: return "17:40";
                case 9: return "19:45";
                case 10: return "20:40";
                case 11: return "21:35";
                case 12: return "22:25";
                case 13: return "23:15";
                case 14: return "23:55";
                default: return "09:40";
            }
        }

        public string GetEffectiveStartTime()
        {
            if (!string.IsNullOrWhiteSpace(StartTime)) return StartTime.Trim();
            int s = StartSection >= 1 && StartSection <= 12 ? StartSection : 1;
            return DefaultSectionStartTime(s);
        }

        public string GetEffectiveEndTime()
        {
            if (!string.IsNullOrWhiteSpace(EndTime)) return EndTime.Trim();
            int e = EndSection >= 1 && EndSection <= 12 ? EndSection : Math.Max(1, StartSection);
            if (e > 12) e = 12;
            return DefaultSectionEndTime(e);
        }

        public string GetSectionLabel()
        {
            int s = Math.Max(1, StartSection);
            int e = Math.Max(s, EndSection);
            return s == e ? string.Format("第{0}节", s) : string.Format("第{0}-{1}节", s, e);
        }

        public string GetWeeksLabel()
        {
            if (CustomWeeks != null && CustomWeeks.Count > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < CustomWeeks.Count; i++) parts.Add(CustomWeeks[i].ToString());
                return string.Join(",", parts.ToArray()) + "周";
            }
            string suffix = WeekType == "odd" ? "(单)" : (WeekType == "even" ? "(双)" : "");
            return StartWeek == EndWeek
                ? string.Format("第{0}周{1}", StartWeek, suffix)
                : string.Format("{0}-{1}周{2}", StartWeek, EndWeek, suffix);
        }

        public static string NormalizeWeekType(string raw)
        {
            string wt = (raw ?? "").Trim().ToLowerInvariant();
            if (wt == "odd" || wt == "1" || wt == "单" || wt == "单周" || wt == "(单)" || wt == "（单）") return "odd";
            if (wt == "even" || wt == "2" || wt == "双" || wt == "双周" || wt == "(双)" || wt == "（双）") return "even";
            if (wt.Contains("odd") || wt.Contains("单")) return "odd";
            if (wt.Contains("even") || wt.Contains("双")) return "even";
            return "all";
        }

        public static string NormalizeTimeToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim().Replace('：', ':');
            Match m = Regex.Match(s, @"^(\d{1,2}):(\d{2})$");
            if (!m.Success) return "";
            int h, min;
            if (!int.TryParse(m.Groups[1].Value, out h) || !int.TryParse(m.Groups[2].Value, out min)) return "";
            if (h < 0 || h > 23 || min < 0 || min > 59) return "";
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", h, min);
        }

        public static bool TryParseWeekSpec(
            string text,
            out int startWeek,
            out int endWeek,
            out string weekType,
            out List<int> customWeeks)
        {
            startWeek = 1;
            endWeek = 16;
            weekType = "all";
            customWeeks = new List<int>();
            if (string.IsNullOrWhiteSpace(text)) return false;

            string norm = text
                .Replace('（', '(').Replace('）', ')')
                .Replace('【', '[').Replace('】', ']')
                .Replace('，', ',').Replace('、', ',').Replace('；', ',')
                .Replace('至', '-').Replace('到', '-').Replace('—', '-').Replace('–', '-').Replace('~', '-');

            // Strip clock times and section ranges so they never pollute week numbers
            norm = Regex.Replace(norm, @"\d{1,2}[:：]\d{2}(?:\s*-\s*\d{1,2}[:：]\d{2})?", " ");
            norm = Regex.Replace(norm, @"第?\s*\d{1,2}(?:\s*[-,]\s*\d{1,2})*\s*节", " ");

            bool pureNumeric = Regex.IsMatch(norm.Trim(), @"^[\d\s,\-]+$");
            var clauses = new List<string>();
            if (pureNumeric)
            {
                clauses.Add(norm.Trim());
            }
            else
            {
                MatchCollection mc = Regex.Matches(
                    norm,
                    @"(?:第\s*)?\d{1,2}(?:\s*[-,]\s*\d{1,2})*(?:\s*周)?\s*(?:[\(\[]\s*(?:单周?|双周?|odd|even)\s*[\)\]]|(?:单周?|双周?|odd|even))?",
                    RegexOptions.IgnoreCase);
                for (int i = 0; i < mc.Count; i++)
                {
                    string val = mc[i].Value;
                    bool hasWeekCue = val.IndexOf("周", StringComparison.Ordinal) >= 0 ||
                                      val.IndexOf("单", StringComparison.Ordinal) >= 0 ||
                                      val.IndexOf("双", StringComparison.Ordinal) >= 0 ||
                                      val.IndexOf("第", StringComparison.Ordinal) >= 0 ||
                                      val.IndexOf("odd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      val.IndexOf("even", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (hasWeekCue) clauses.Add(val);
                }
            }

            if (clauses.Count == 0) return false;

            var weekSet = new HashSet<int>();
            bool sawOddMarker = false;
            bool sawEvenMarker = false;

            for (int c = 0; c < clauses.Count; c++)
            {
                string clause = clauses[c];
                string clauseParity = "all";
                if (Regex.IsMatch(clause, @"单|odd", RegexOptions.IgnoreCase)) clauseParity = "odd";
                else if (Regex.IsMatch(clause, @"双|even", RegexOptions.IgnoreCase)) clauseParity = "even";

                // Split by comma to allow per-segment parity like "1-4周(单),6-12周(双)"
                string[] parts = clause.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int p = 0; p < parts.Length; p++)
                {
                    string part = parts[p];
                    string partParity = clauseParity;
                    if (Regex.IsMatch(part, @"单|odd", RegexOptions.IgnoreCase)) partParity = "odd";
                    else if (Regex.IsMatch(part, @"双|even", RegexOptions.IgnoreCase)) partParity = "even";
                    if (partParity == "odd") sawOddMarker = true;
                    if (partParity == "even") sawEvenMarker = true;

                    MatchCollection segs = Regex.Matches(part, @"(\d{1,2})(?:\s*-\s*(\d{1,2}))?");
                    for (int s = 0; s < segs.Count; s++)
                    {
                        int a, b;
                        if (!int.TryParse(segs[s].Groups[1].Value, out a)) continue;
                        if (segs[s].Groups[2].Success && int.TryParse(segs[s].Groups[2].Value, out b))
                        {
                            if (b < a) { int tmp = a; a = b; b = tmp; }
                        }
                        else
                        {
                            b = a;
                        }
                        if (a < 1) a = 1;
                        if (b > 30) b = 30;
                        for (int w = a; w <= b; w++)
                        {
                            if (partParity == "odd" && (w % 2) == 0) continue;
                            if (partParity == "even" && (w % 2) == 1) continue;
                            weekSet.Add(w);
                        }
                    }
                }
            }

            if (weekSet.Count == 0) return false;

            var sorted = new List<int>(weekSet);
            sorted.Sort();
            int minW = sorted[0];
            int maxW = sorted[sorted.Count - 1];

            bool allConsecutive = true;
            for (int w = minW; w <= maxW; w++)
            {
                if (!weekSet.Contains(w)) { allConsecutive = false; break; }
            }

            if (allConsecutive)
            {
                startWeek = minW;
                endWeek = maxW;
                weekType = "all";
                customWeeks = new List<int>();
                return true;
            }

            bool allOddMatch = true;
            int expectedOdd = 0;
            for (int w = minW; w <= maxW; w++)
            {
                if ((w % 2) == 1)
                {
                    expectedOdd++;
                    if (!weekSet.Contains(w)) { allOddMatch = false; break; }
                }
                else if (weekSet.Contains(w))
                {
                    allOddMatch = false;
                    break;
                }
            }
            if (allOddMatch && (sorted.Count >= 2 || sawOddMarker) && !sawEvenMarker && expectedOdd == sorted.Count)
            {
                startWeek = minW;
                endWeek = maxW;
                weekType = "odd";
                customWeeks = new List<int>();
                return true;
            }

            bool allEvenMatch = true;
            int expectedEven = 0;
            for (int w = minW; w <= maxW; w++)
            {
                if ((w % 2) == 0)
                {
                    expectedEven++;
                    if (!weekSet.Contains(w)) { allEvenMatch = false; break; }
                }
                else if (weekSet.Contains(w))
                {
                    allEvenMatch = false;
                    break;
                }
            }
            if (allEvenMatch && (sorted.Count >= 2 || sawEvenMarker) && !sawOddMarker && expectedEven == sorted.Count)
            {
                startWeek = minW;
                endWeek = maxW;
                weekType = "even";
                customWeeks = new List<int>();
                return true;
            }

            startWeek = minW;
            endWeek = maxW;
            weekType = "all";
            customWeeks = sorted;
            return true;
        }

        public static string GetWeekdayName(int dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case 1: return "周一";
                case 2: return "周二";
                case 3: return "周三";
                case 4: return "周四";
                case 5: return "周五";
                case 6: return "周六";
                case 7: return "周日";
                default: return "周一";
            }
        }
    }

    public class TimetableData
    {
        public string SemesterName { get; set; }
        public string SemesterStartMondayIso { get; set; } // yyyy-MM-dd
        public int TotalWeeks { get; set; }
        public int SectionsPerDay { get; set; } // 0 = auto from courses, 4..16 = custom fixed
        public bool ShowWeekends { get; set; }
        public bool ShowTimeOnCard { get; set; }
        public bool ShowLocationOnCard { get; set; }
        public bool ShowTeacherOnCard { get; set; }
        public int RowMinHeight { get; set; } // 36..80, default 52
        public List<CourseItem> Courses { get; set; }
        public List<SectionTimeSlot> SectionTimes { get; set; }
        public string LastSyncIso { get; set; }

        private static readonly Regex WeekDayRegex = new Regex(
            @"第?\s*([0-9]{1,2}|[一二三四五六七八九十廿]{1,3})\s*周(?:\s*(?:的)?\s*(?:星期|礼拜|周)\s*([1-7一二三四五六日天]))?",
            RegexOptions.Compiled);

        public TimetableData()
        {
            SemesterName = "";
            SemesterStartMondayIso = "";
            TotalWeeks = 20;
            SectionsPerDay = 0;
            ShowWeekends = true;
            ShowTimeOnCard = true;
            ShowLocationOnCard = true;
            ShowTeacherOnCard = false;
            RowMinHeight = 52;
            Courses = new List<CourseItem>();
            SectionTimes = new List<SectionTimeSlot>();
            LastSyncIso = "";
        }

        public SectionTimeSlot FindSectionSlot(int section)
        {
            if (SectionTimes == null) return null;
            for (int i = 0; i < SectionTimes.Count; i++)
            {
                if (SectionTimes[i] != null && SectionTimes[i].Section == section) return SectionTimes[i];
            }
            return null;
        }

        public string GetSectionStartTime(int section)
        {
            int sec = Math.Max(1, Math.Min(16, section));
            SectionTimeSlot slot = FindSectionSlot(sec);
            if (slot != null)
            {
                string norm = CourseItem.NormalizeTimeToken(slot.StartTime);
                if (!string.IsNullOrEmpty(norm)) return norm;
            }
            return CourseItem.DefaultSectionStartTime(sec);
        }

        public string GetSectionEndTime(int section)
        {
            int sec = Math.Max(1, Math.Min(16, section));
            SectionTimeSlot slot = FindSectionSlot(sec);
            if (slot != null)
            {
                string norm = CourseItem.NormalizeTimeToken(slot.EndTime);
                if (!string.IsNullOrEmpty(norm)) return norm;
            }
            return CourseItem.DefaultSectionEndTime(sec);
        }

        public string GetCourseStartTime(CourseItem c)
        {
            if (c == null) return "08:00";
            string explicitStart = CourseItem.NormalizeTimeToken(c.StartTime);
            if (!string.IsNullOrEmpty(explicitStart)) return explicitStart;
            return GetSectionStartTime(c.StartSection);
        }

        public string GetCourseEndTime(CourseItem c)
        {
            if (c == null) return "09:40";
            string explicitEnd = CourseItem.NormalizeTimeToken(c.EndTime);
            if (!string.IsNullOrEmpty(explicitEnd)) return explicitEnd;
            return GetSectionEndTime(c.EndSection >= c.StartSection ? c.EndSection : c.StartSection);
        }

        public void SetSectionTime(int section, string startTime, string endTime)
        {
            if (section < 1 || section > 16) return;
            if (SectionTimes == null) SectionTimes = new List<SectionTimeSlot>();
            string ns = CourseItem.NormalizeTimeToken(startTime);
            string ne = CourseItem.NormalizeTimeToken(endTime);
            SectionTimeSlot existing = FindSectionSlot(section);
            if (existing == null)
            {
                if (string.IsNullOrEmpty(ns) && string.IsNullOrEmpty(ne)) return;
                SectionTimes.Add(new SectionTimeSlot
                {
                    Section = section,
                    StartTime = ns,
                    EndTime = ne
                });
            }
            else
            {
                if (!string.IsNullOrEmpty(ns)) existing.StartTime = ns;
                if (!string.IsNullOrEmpty(ne)) existing.EndTime = ne;
            }
            SectionTimes.Sort(delegate(SectionTimeSlot a, SectionTimeSlot b) { return a.Section.CompareTo(b.Section); });
        }

        public int GetMaxSectionCount()
        {
            if (SectionsPerDay >= 4 && SectionsPerDay <= 16)
            {
                return SectionsPerDay;
            }
            int maxSec = 10;
            if (Courses != null)
            {
                for (int i = 0; i < Courses.Count; i++)
                {
                    if (Courses[i] != null && Courses[i].EndSection > maxSec) maxSec = Courses[i].EndSection;
                }
            }
            if (maxSec > 16) maxSec = 16;
            return maxSec;
        }

        public void MoveCourse(CourseItem course, int targetDay, int targetStartSection, int currentWeek, bool singleWeekOnly)
        {
            if (course == null) return;
            if (Courses == null) Courses = new List<CourseItem>();

            int day = Math.Max(1, Math.Min(7, targetDay));
            int span = Math.Max(0, course.EndSection - course.StartSection);
            int sSec = Math.Max(1, Math.Min(16, targetStartSection));
            int eSec = Math.Min(16, sSec + span);

            if (!singleWeekOnly || course.IsSingleWeekOnly(currentWeek))
            {
                course.DayOfWeek = day;
                course.StartSection = sSec;
                course.EndSection = eSec;
                course.StartTime = "";
                course.EndTime = "";
            }
            else
            {
                if (course.ExcludedWeeks == null) course.ExcludedWeeks = new List<int>();
                if (!course.ExcludedWeeks.Contains(currentWeek)) course.ExcludedWeeks.Add(currentWeek);
                CourseItem moved = course.CloneForSingleWeek(currentWeek, day, sSec, eSec);
                Courses.Add(moved);
            }
        }

        public void SkipCourseInWeek(CourseItem course, int week)
        {
            if (course == null || week < 1) return;
            if (course.IsSingleWeekOnly(week))
            {
                if (Courses != null) Courses.Remove(course);
            }
            else
            {
                if (course.ExcludedWeeks == null) course.ExcludedWeeks = new List<int>();
                if (!course.ExcludedWeeks.Contains(week)) course.ExcludedWeeks.Add(week);
            }
        }

        public void BackfillCourseTimesFromSections()
        {
            if (Courses == null) return;
            for (int i = 0; i < Courses.Count; i++)
            {
                CourseItem c = Courses[i];
                if (c == null) continue;
                string ns = CourseItem.NormalizeTimeToken(c.StartTime);
                string ne = CourseItem.NormalizeTimeToken(c.EndTime);
                if (c.StartSection == c.EndSection && c.StartSection >= 1 && c.StartSection <= 16)
                {
                    SectionTimeSlot slot = FindSectionSlot(c.StartSection);
                    if (slot == null && (!string.IsNullOrEmpty(ns) || !string.IsNullOrEmpty(ne)))
                    {
                        SetSectionTime(c.StartSection, ns, ne);
                    }
                }
            }
        }

        public static int ToIsoWeekday(DayOfWeek dw)
        {
            return dw == DayOfWeek.Sunday ? 7 : (int)dw;
        }

        public DateTime? GetSemesterStartMonday()
        {
            if (string.IsNullOrWhiteSpace(SemesterStartMondayIso)) return null;
            DateTime dt;
            if (!DateTime.TryParseExact(SemesterStartMondayIso.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                if (!DateTime.TryParse(SemesterStartMondayIso.Trim(), out dt)) return null;
            }
            int delta = ((int)dt.DayOfWeek + 6) % 7;
            return dt.Date.AddDays(-delta);
        }

        public int GetWeekNumberForDate(DateTime date)
        {
            DateTime? start = GetSemesterStartMonday();
            if (!start.HasValue) return 0;
            int days = (int)(date.Date - start.Value.Date).TotalDays;
            if (days < 0) return 0;
            return (days / 7) + 1;
        }

        public DateTime? GetDateForWeekAndDay(int week, int dayOfWeek)
        {
            DateTime? start = GetSemesterStartMonday();
            if (!start.HasValue || week < 1 || dayOfWeek < 1 || dayOfWeek > 7) return null;
            return start.Value.Date.AddDays((week - 1) * 7 + (dayOfWeek - 1));
        }

        public void SetCurrentWeekForDate(int currentWeek, DateTime referenceDate)
        {
            int w = currentWeek < 1 ? 1 : (currentWeek > 30 ? 30 : currentWeek);
            int delta = ((int)referenceDate.DayOfWeek + 6) % 7;
            DateTime thisMonday = referenceDate.Date.AddDays(-delta);
            DateTime startMonday = thisMonday.AddDays(-(w - 1) * 7);
            SemesterStartMondayIso = startMonday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public List<CourseItem> GetCoursesForWeekAndDay(int week, int dayOfWeek)
        {
            var res = new List<CourseItem>();
            if (Courses == null || week < 1 || dayOfWeek < 1 || dayOfWeek > 7) return res;
            for (int i = 0; i < Courses.Count; i++)
            {
                CourseItem c = Courses[i];
                if (c != null && c.DayOfWeek == dayOfWeek && c.OccursInWeek(week))
                {
                    res.Add(c);
                }
            }
            res.Sort(delegate(CourseItem a, CourseItem b)
            {
                int cmp = a.StartSection.CompareTo(b.StartSection);
                if (cmp != 0) return cmp;
                return string.Compare(a.GetEffectiveStartTime(), b.GetEffectiveStartTime(), StringComparison.Ordinal);
            });
            return res;
        }

        public List<CourseItem> GetCoursesForDate(DateTime date)
        {
            int w = GetWeekNumberForDate(date);
            if (w < 1) return new List<CourseItem>();
            return GetCoursesForWeekAndDay(w, ToIsoWeekday(date.DayOfWeek));
        }

        public string BuildAiWeekContext(DateTime now)
        {
            var sb = new StringBuilder();
            DateTime? start = GetSemesterStartMonday();
            if (start.HasValue)
            {
                int curW = GetWeekNumberForDate(now);
                sb.Append("【校历与教学周信息】\n");
                if (!string.IsNullOrWhiteSpace(SemesterName))
                {
                    sb.Append("学期：").Append(SemesterName.Trim()).Append("\n");
                }
                sb.Append("第1周周一：").Append(start.Value.ToString("yyyy-MM-dd")).Append("；");
                if (curW >= 1)
                {
                    sb.Append("今天属于第").Append(curW).Append("周").Append(CourseItem.GetWeekdayName(ToIsoWeekday(now.DayOfWeek))).Append("\n");
                }
                else
                {
                    sb.Append("当前尚未进入第1教学周\n");
                }

                int maxW = TotalWeeks > 0 ? TotalWeeks : 20;
                int fromW = curW >= 1 ? curW : 1;
                int toW = Math.Min(maxW, fromW + 5);
                sb.Append("近期教学周对应公历：");
                for (int w = fromW; w <= toW; w++)
                {
                    DateTime? mon = GetDateForWeekAndDay(w, 1);
                    DateTime? sun = GetDateForWeekAndDay(w, 7);
                    if (mon.HasValue && sun.HasValue)
                    {
                        sb.Append("第").Append(w).Append("周(")
                          .Append(mon.Value.ToString("MM-dd")).Append("~").Append(sun.Value.ToString("MM-dd")).Append(") ");
                    }
                }
                sb.Append("\n");
            }

            if (Courses != null && Courses.Count > 0)
            {
                List<CourseItem> todayCourses = GetCoursesForDate(now);
                if (todayCourses.Count > 0)
                {
                    sb.Append("今日课程：");
                    for (int i = 0; i < todayCourses.Count; i++)
                    {
                        CourseItem c = todayCourses[i];
                        if (i > 0) sb.Append("；");
                        sb.Append(c.Name).Append(" ").Append(GetCourseStartTime(c)).Append("-").Append(GetCourseEndTime(c));
                        if (!string.IsNullOrWhiteSpace(c.Location)) sb.Append(" @").Append(c.Location);
                    }
                    sb.Append("\n");
                }

                sb.Append("本学期课程清单（节选）：");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int count = 0;
                for (int i = 0; i < Courses.Count && count < 18; i++)
                {
                    CourseItem c = Courses[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.Name)) continue;
                    string key = c.Name + "|" + c.DayOfWeek + "|" + c.StartSection;
                    if (!seen.Add(key)) continue;
                    if (count > 0) sb.Append("；");
                    sb.Append(c.Name).Append("(").Append(CourseItem.GetWeekdayName(c.DayOfWeek))
                      .Append(" ").Append(GetCourseStartTime(c)).Append(",").Append(c.GetWeeksLabel()).Append(")");
                    count++;
                }
                sb.Append("\n");
            }

            string text = sb.ToString().Trim();
            if (text.Length > 1200) text = text.Substring(0, 1200);
            return text;
        }

        public string AlignWeekTextToDateIso(string text, string existingDeadlineIso, int? explicitWeek, int? explicitWeekday, DateTime now)
        {
            DateTime? start = GetSemesterStartMonday();
            if (!start.HasValue) return existingDeadlineIso ?? "";

            string timePart = "18:00";
            if (!string.IsNullOrWhiteSpace(existingDeadlineIso))
            {
                DateTime? parsedExisting = MemoTask.ParseIsoDateTime(existingDeadlineIso);
                if (parsedExisting.HasValue)
                {
                    timePart = parsedExisting.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
                }
            }

            if (explicitWeek.HasValue && explicitWeek.Value >= 1 && explicitWeekday.HasValue && explicitWeekday.Value >= 1 && explicitWeekday.Value <= 7)
            {
                DateTime? aligned = GetDateForWeekAndDay(explicitWeek.Value, explicitWeekday.Value);
                if (aligned.HasValue)
                {
                    return aligned.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + timePart;
                }
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                Match m = WeekDayRegex.Match(text);
                if (m.Success)
                {
                    int weekNum = ParseWeekToken(m.Groups[1].Value);
                    if (weekNum >= 1 && weekNum <= 30)
                    {
                        int dayNum = m.Groups[2].Success && !string.IsNullOrEmpty(m.Groups[2].Value)
                            ? ParseWeekdayToken(m.Groups[2].Value)
                            : 0;
                        if (dayNum >= 1 && dayNum <= 7)
                        {
                            DateTime? aligned = GetDateForWeekAndDay(weekNum, dayNum);
                            if (aligned.HasValue)
                            {
                                return aligned.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + timePart;
                            }
                        }
                        else if (string.IsNullOrWhiteSpace(existingDeadlineIso))
                        {
                            DateTime? friday = GetDateForWeekAndDay(weekNum, 5);
                            if (friday.HasValue)
                            {
                                return friday.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + timePart;
                            }
                        }
                    }
                }
            }

            return existingDeadlineIso ?? "";
        }

        private static int ParseWeekToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return 0;
            int n;
            if (int.TryParse(token.Trim(), out n)) return n;
            string t = token.Trim();
            int val = 0;
            if (t == "十") return 10;
            if (t.StartsWith("十"))
            {
                val = 10 + ChineseDigit(t.Length > 1 ? t[1] : '\0');
                return val;
            }
            if (t.StartsWith("廿"))
            {
                val = 20 + ChineseDigit(t.Length > 1 ? t[1] : '\0');
                return val;
            }
            if (t.Length == 2 && t[1] == '十') return ChineseDigit(t[0]) * 10;
            if (t.Length == 3 && t[1] == '十') return ChineseDigit(t[0]) * 10 + ChineseDigit(t[2]);
            if (t.Length == 1) return ChineseDigit(t[0]);
            return 0;
        }

        private static int ChineseDigit(char c)
        {
            switch (c)
            {
                case '一': return 1;
                case '二': return 2;
                case '三': return 3;
                case '四': return 4;
                case '五': return 5;
                case '六': return 6;
                case '七': return 7;
                case '八': return 8;
                case '九': return 9;
                default: return 0;
            }
        }

        private static int ParseWeekdayToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return 0;
            char c = token.Trim()[0];
            if (c >= '1' && c <= '7') return c - '0';
            if (c == '日' || c == '天') return 7;
            return ChineseDigit(c);
        }
    }

    public class AppData
    {
        public int SchemaVersion { get; set; }
        public List<MemoTask> Tasks { get; set; }
        public List<StickyNote> StickyNotes { get; set; }
        public TimetableData Timetable { get; set; }

        public AppData()
        {
            SchemaVersion = 2;
            Tasks = new List<MemoTask>();
            StickyNotes = new List<StickyNote>();
            Timetable = new TimetableData();
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
        public string HotkeyNewTask { get; set; }
        public string HotkeyToggleAll { get; set; }
        public string HotkeyShowPanel { get; set; }
        public string HotkeyNewSticky { get; set; }
        public string HotkeyVoiceMemo { get; set; }
        public bool VoiceMemoEnabled { get; set; }
        public string VoiceTriggerMode { get; set; } // toggle, hold
        public bool VoiceAutoCommit { get; set; }
        public bool ReminderSoundEnabled { get; set; }

        public string AiProviderPreset { get; set; }
        public string AiBaseUrl { get; set; }
        public string AiApiKey { get; set; }
        public string AiModel { get; set; }
        public string AiCustomInstruction { get; set; }

        // MIS Timetable Integration
        public bool MisEnabled { get; set; }
        public string MisUrl { get; set; }
        public bool ShowTodayCoursesInQuadrants { get; set; }

        // Chat Export Import Directory
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
            HotkeyNewTask = "Ctrl+Alt+N";
            HotkeyToggleAll = "Ctrl+Alt+M";
            HotkeyShowPanel = "Ctrl+Alt+P";
            HotkeyNewSticky = "Ctrl+Alt+S";
            HotkeyVoiceMemo = "Ctrl+Alt+V";
            VoiceMemoEnabled = true;
            VoiceTriggerMode = "toggle";
            VoiceAutoCommit = true;
            ReminderSoundEnabled = true;

            AiProviderPreset = "deepseek_flash";
            AiBaseUrl = "https://api.deepseek.com/v1";
            AiApiKey = "";
            AiModel = "deepseek-flash";
            AiCustomInstruction = "";
            MisEnabled = false;
            MisUrl = "";
            ShowTodayCoursesInQuadrants = true;
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
        public int TeachingWeek { get; set; }
        public int TeachingWeekday { get; set; }
        public string Reason { get; set; }
        public bool Selected { get; set; }

        public AiExtractedTask()
        {
            Title = "";
            Notes = "";
            DeadlineIso = "";
            RemindAtIso = "";
            Quadrant = 0;
            TeachingWeek = 0;
            TeachingWeekday = 0;
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
