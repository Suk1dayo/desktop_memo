using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace DesktopMemo
{
    public class UiScaleMetrics
    {
        public string Key { get; set; }
        public double FontSize { get; set; }
        public double SmallFontSize { get; set; }
        public double HeaderFontSize { get; set; }
        public Thickness ButtonPadding { get; set; }
        public Thickness SmallButtonPadding { get; set; }
        public double ButtonHeight { get; set; }
        public Thickness InputPadding { get; set; }
        public Thickness CardPadding { get; set; }
        public double CornerRadius { get; set; }
    }

    public class ThemePalette
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool IsDark { get; set; }
        public string CardBgHex { get; set; }
        public string HeaderBgHex { get; set; }
        public string SurfaceBgHex { get; set; }
        public string InputBgHex { get; set; }
        public string BorderHex { get; set; }
        public string TextPrimaryHex { get; set; }
        public string TextSecondaryHex { get; set; }
        public string AccentBgHex { get; set; }
        public string AccentFgHex { get; set; }
        public string HoverBgHex { get; set; }
        public string SwatchHex { get; set; }
        public string DockTrackHex { get; set; }
        public string DockActiveHex { get; set; }
        public double Opacity { get; set; }

        public WpfBrush CardBg { get; private set; }
        public WpfBrush HeaderBg { get; private set; }
        public WpfBrush SurfaceBg { get; private set; }
        public WpfBrush InputBg { get; private set; }
        public WpfBrush Border { get; private set; }
        public WpfBrush TextPrimary { get; private set; }
        public WpfBrush TextSecondary { get; private set; }
        public WpfBrush AccentBg { get; private set; }
        public WpfBrush AccentFg { get; private set; }
        public WpfBrush HoverBg { get; private set; }
        public WpfBrush PillGlassBg { get; private set; }
        public WpfBrush ActivePillGlassBg { get; private set; }
        public WpfBrush PopupBg { get; private set; }

        public void BuildBrushes(double opacity)
        {
            Opacity = Math.Max(0.60, Math.Min(1.00, opacity));
            byte mainAlpha = (byte)Math.Max(140, Math.Min(255, (int)Math.Round(Opacity * 255.0)));
            byte cardAlpha = (byte)Math.Max(165, Math.Min(255, mainAlpha + 10));

            CardBg = ThemeService.BrushFromHexAlpha(CardBgHex, mainAlpha);
            HeaderBg = ThemeService.BrushFromHexAlpha(HeaderBgHex, mainAlpha);
            SurfaceBg = ThemeService.BrushFromHexAlpha(SurfaceBgHex, cardAlpha);
            InputBg = ThemeService.BrushFromHex(InputBgHex);
            Border = ThemeService.BrushFromHex(BorderHex);
            TextPrimary = ThemeService.BrushFromHex(TextPrimaryHex);
            TextSecondary = ThemeService.BrushFromHex(TextSecondaryHex);
            AccentBg = ThemeService.BrushFromHex(AccentBgHex);
            AccentFg = ThemeService.BrushFromHex(AccentFgHex);
            HoverBg = ThemeService.BrushFromHex(HoverBgHex);
            PillGlassBg = ThemeService.BrushFromHex(DockTrackHex);
            ActivePillGlassBg = ThemeService.BrushFromHex(DockActiveHex);
            PopupBg = ThemeService.BrushFromHex(SurfaceBgHex);
        }
    }

    public class QuadrantVisual
    {
        public int Quadrant { get; set; }
        public string Title { get; set; }
        public string ShortTitle { get; set; }
        public string Subtitle { get; set; }
        public string AccentHex { get; set; }
        public string SoftBgHex { get; set; }
        public string HeaderBgHex { get; set; }
        public string BadgeBgHex { get; set; }
        public string BadgeFgHex { get; set; }
    }

    public static class ThemeService
    {
        public static readonly string[] ThemeKeys = new string[]
        {
            "claude_warm",
            "notion_light",
            "linear_dark",
            "nord_slate",
            "matcha_flat",
            "rose_flat"
        };

        public static string NormalizeThemeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "claude_warm";
            switch (key)
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

        public static string GetThemeDisplayName(string key)
        {
            switch (NormalizeThemeKey(key))
            {
                case "claude_warm": return "Claude 暖杏极简";
                case "notion_light": return "Notion 纯净白";
                case "linear_dark": return "Linear 深空暗黑";
                case "nord_slate": return "Nord 静谧灰蓝";
                case "matcha_flat": return "青岚护眼浅绿";
                case "rose_flat": return "莫兰迪暖灰粉";
                default: return "Claude 暖杏极简";
            }
        }

        public static ThemePalette GetPalette(string key, double opacity)
        {
            ThemePalette p = new ThemePalette();
            p.Key = NormalizeThemeKey(key);
            p.DisplayName = GetThemeDisplayName(p.Key);

            switch (p.Key)
            {
                case "notion_light":
                    p.IsDark = false;
                    p.CardBgHex = "#F7F7F5";
                    p.HeaderBgHex = "#FFFFFF";
                    p.SurfaceBgHex = "#FFFFFF";
                    p.InputBgHex = "#FFFFFF";
                    p.BorderHex = "#E5E5E2";
                    p.TextPrimaryHex = "#242424";
                    p.TextSecondaryHex = "#6B6B6B";
                    p.AccentBgHex = "#2383E2";
                    p.AccentFgHex = "#FFFFFF";
                    p.HoverBgHex = "#EFEFED";
                    p.SwatchHex = "#FFFFFF";
                    p.DockTrackHex = "#EBEBE8";
                    p.DockActiveHex = "#FFFFFF";
                    break;

                case "linear_dark":
                    p.IsDark = true;
                    p.CardBgHex = "#111318";
                    p.HeaderBgHex = "#161920";
                    p.SurfaceBgHex = "#1B1F27";
                    p.InputBgHex = "#14171E";
                    p.BorderHex = "#2B313E";
                    p.TextPrimaryHex = "#F3F4F6";
                    p.TextSecondaryHex = "#9CA3AF";
                    p.AccentBgHex = "#6366F1";
                    p.AccentFgHex = "#FFFFFF";
                    p.HoverBgHex = "#252A36";
                    p.SwatchHex = "#1B1F27";
                    p.DockTrackHex = "#1B1F27";
                    p.DockActiveHex = "#2B313E";
                    break;

                case "nord_slate":
                    p.IsDark = false;
                    p.CardBgHex = "#EEF2F7";
                    p.HeaderBgHex = "#E5EBF3";
                    p.SurfaceBgHex = "#FFFFFF";
                    p.InputBgHex = "#FFFFFF";
                    p.BorderHex = "#D3DCE8";
                    p.TextPrimaryHex = "#1E293B";
                    p.TextSecondaryHex = "#5B6B82";
                    p.AccentBgHex = "#4C75A3";
                    p.AccentFgHex = "#FFFFFF";
                    p.HoverBgHex = "#E2E8F0";
                    p.SwatchHex = "#E5EBF3";
                    p.DockTrackHex = "#DFE6F0";
                    p.DockActiveHex = "#FFFFFF";
                    break;

                case "matcha_flat":
                    p.IsDark = false;
                    p.CardBgHex = "#F2F7F4";
                    p.HeaderBgHex = "#E8F1EC";
                    p.SurfaceBgHex = "#FFFFFF";
                    p.InputBgHex = "#FFFFFF";
                    p.BorderHex = "#CFE0D6";
                    p.TextPrimaryHex = "#162A22";
                    p.TextSecondaryHex = "#517063";
                    p.AccentBgHex = "#15803D";
                    p.AccentFgHex = "#FFFFFF";
                    p.HoverBgHex = "#E3EFE8";
                    p.SwatchHex = "#E8F1EC";
                    p.DockTrackHex = "#DFECE5";
                    p.DockActiveHex = "#FFFFFF";
                    break;

                case "rose_flat":
                    p.IsDark = false;
                    p.CardBgHex = "#FAF4F5";
                    p.HeaderBgHex = "#F3E8EA";
                    p.SurfaceBgHex = "#FFFFFF";
                    p.InputBgHex = "#FFFFFF";
                    p.BorderHex = "#E8D5D9";
                    p.TextPrimaryHex = "#2A1B1F";
                    p.TextSecondaryHex = "#785D64";
                    p.AccentBgHex = "#BE4B62";
                    p.AccentFgHex = "#FFFFFF";
                    p.HoverBgHex = "#F0E3E6";
                    p.SwatchHex = "#F3E8EA";
                    p.DockTrackHex = "#EDE0E3";
                    p.DockActiveHex = "#FFFFFF";
                    break;

                case "claude_warm":
                default:
                    p.Key = "claude_warm";
                    p.IsDark = false;
                    p.CardBgHex = "#FAF9F5";
                    p.HeaderBgHex = "#F3F0E7";
                    p.SurfaceBgHex = "#FFFFFF";
                    p.InputBgHex = "#FFFFFF";
                    p.BorderHex = "#E5E0D5";
                    p.TextPrimaryHex = "#1F1E1C";
                    p.TextSecondaryHex = "#6E695F";
                    p.AccentBgHex = "#C96442";
                    p.AccentFgHex = "#FFFFFF";
                    p.HoverBgHex = "#EFECE3";
                    p.SwatchHex = "#F3F0E7";
                    p.DockTrackHex = "#EBE7DC";
                    p.DockActiveHex = "#FFFFFF";
                    break;
            }

            p.BuildBrushes(opacity);
            return p;
        }

        public static UiScaleMetrics GetScaleMetrics(string uiScale, double baseFontSize)
        {
            string key = string.IsNullOrWhiteSpace(uiScale) ? "standard" : uiScale;
            double fs = baseFontSize >= 10 && baseFontSize <= 22 ? baseFontSize : 13;
            var m = new UiScaleMetrics { Key = key };

            if (key == "compact")
            {
                m.FontSize = Math.Max(11, fs - 1);
                m.SmallFontSize = Math.Max(10, m.FontSize - 2);
                m.HeaderFontSize = m.FontSize + 1.5;
                m.ButtonPadding = new Thickness(9, 4, 9, 4);
                m.SmallButtonPadding = new Thickness(6, 2, 6, 2);
                m.ButtonHeight = 26;
                m.InputPadding = new Thickness(8, 4, 8, 4);
                m.CardPadding = new Thickness(8, 6, 8, 6);
                m.CornerRadius = 8;
            }
            else if (key == "large")
            {
                m.FontSize = Math.Min(20, fs + 1.5);
                m.SmallFontSize = Math.Max(11.5, m.FontSize - 2);
                m.HeaderFontSize = m.FontSize + 2.5;
                m.ButtonPadding = new Thickness(15, 8, 15, 8);
                m.SmallButtonPadding = new Thickness(9, 4, 9, 4);
                m.ButtonHeight = 36;
                m.InputPadding = new Thickness(11, 7, 11, 7);
                m.CardPadding = new Thickness(12, 9, 12, 9);
                m.CornerRadius = 11;
            }
            else
            {
                m.Key = "standard";
                m.FontSize = fs;
                m.SmallFontSize = Math.Max(10.5, fs - 2);
                m.HeaderFontSize = fs + 2;
                m.ButtonPadding = new Thickness(12, 6, 12, 6);
                m.SmallButtonPadding = new Thickness(7, 3, 7, 3);
                m.ButtonHeight = 30;
                m.InputPadding = new Thickness(9, 5, 9, 5);
                m.CardPadding = new Thickness(10, 7, 10, 7);
                m.CornerRadius = 9;
            }
            return m;
        }

        public static QuadrantVisual GetQuadrantVisual(int quadrant, bool isDark)
        {
            var qv = new QuadrantVisual();
            qv.Quadrant = MemoTask.NormalizeQuadrant(quadrant);
            switch (qv.Quadrant)
            {
                case 0:
                    qv.Title = "未分类（独立便签）";
                    qv.ShortTitle = "未分类";
                    qv.Subtitle = "独立便签待办 · 未指定象限";
                    qv.AccentHex = "#94A3B8";
                    qv.SoftBgHex = isDark ? "#1B1F27" : "#F8FAFC";
                    qv.HeaderBgHex = isDark ? "#232833" : "#F1F5F9";
                    qv.BadgeBgHex = isDark ? "#2B313E" : "#E2E8F0";
                    qv.BadgeFgHex = isDark ? "#CBD5E1" : "#475569";
                    break;
                case 1:
                    qv.Title = "Q1 重要且紧急";
                    qv.ShortTitle = "Q1 立刻做";
                    qv.Subtitle = "马上处理 · 危机与临近DDL";
                    qv.AccentHex = "#DC2626";
                    qv.SoftBgHex = isDark ? "#22181B" : "#FFF8F7";
                    qv.HeaderBgHex = isDark ? "#2D1D21" : "#FEF0EE";
                    qv.BadgeBgHex = isDark ? "#4C1D24" : "#FEE2E2";
                    qv.BadgeFgHex = isDark ? "#FCA5A5" : "#B91C1C";
                    break;
                case 2:
                    qv.Title = "Q2 重要不紧急";
                    qv.ShortTitle = "Q2 计划做";
                    qv.Subtitle = "核心目标 · 规划与长期收益";
                    qv.AccentHex = "#2563EB";
                    qv.SoftBgHex = isDark ? "#171E2C" : "#F7FAFF";
                    qv.HeaderBgHex = isDark ? "#1D283B" : "#EDF4FF";
                    qv.BadgeBgHex = isDark ? "#1E3A8A" : "#DBEAFE";
                    qv.BadgeFgHex = isDark ? "#93C5FD" : "#1D4ED8";
                    break;
                case 3:
                    qv.Title = "Q3 紧急不重要";
                    qv.ShortTitle = "Q3 尽快办";
                    qv.Subtitle = "限时琐事 · 尽量快速收尾";
                    qv.AccentHex = "#D97706";
                    qv.SoftBgHex = isDark ? "#231E16" : "#FFFBF3";
                    qv.HeaderBgHex = isDark ? "#2F2719" : "#FEF5E4";
                    qv.BadgeBgHex = isDark ? "#78350F" : "#FEF3C7";
                    qv.BadgeFgHex = isDark ? "#FCD34D" : "#B45309";
                    break;
                case 4:
                default:
                    qv.Quadrant = 4;
                    qv.Title = "Q4 不重要不紧急";
                    qv.ShortTitle = "Q4 稍后做";
                    qv.Subtitle = "灵感备忘 · 有空再清理";
                    qv.AccentHex = "#64748B";
                    qv.SoftBgHex = isDark ? "#181C23" : "#F8F9FA";
                    qv.HeaderBgHex = isDark ? "#212630" : "#F1F3F5";
                    qv.BadgeBgHex = isDark ? "#2B313E" : "#E5E7EB";
                    qv.BadgeFgHex = isDark ? "#CBD5E1" : "#4B5563";
                    break;
            }
            return qv;
        }

        public static WpfBrush BrushFromHex(string hex)
        {
            return BrushFromHexAlpha(hex, 255);
        }

        public static WpfBrush BrushFromHexAlpha(string hex, byte alpha)
        {
            WpfColor c = ColorFromHex(hex);
            c.A = alpha;
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static WpfColor ColorFromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Colors.White;
            string s = hex.Trim().TrimStart('#');
            if (s.Length == 6)
            {
                byte r = Convert.ToByte(s.Substring(0, 2), 16);
                byte g = Convert.ToByte(s.Substring(2, 2), 16);
                byte b = Convert.ToByte(s.Substring(4, 2), 16);
                return WpfColor.FromArgb(255, r, g, b);
            }
            if (s.Length == 8)
            {
                byte a = Convert.ToByte(s.Substring(0, 2), 16);
                byte r = Convert.ToByte(s.Substring(2, 2), 16);
                byte g = Convert.ToByte(s.Substring(4, 2), 16);
                byte b = Convert.ToByte(s.Substring(6, 2), 16);
                return WpfColor.FromArgb(a, r, g, b);
            }
            return Colors.White;
        }

        public static System.Windows.Media.FontFamily AppFontFamily
        {
            get { return new System.Windows.Media.FontFamily("Microsoft YaHei UI, Segoe UI"); }
        }

        public static System.Windows.Controls.Button CreatePillButton(string text, WpfBrush bg, WpfBrush fg, WpfBrush border, double fontSize, Thickness padding)
        {
            return GlassControlStyles.CreateGlassButton(text, bg, fg, border, fontSize, padding, 8);
        }

        public static Icon CreateAppTrayIcon()
        {
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "app.ico");
                if (File.Exists(icoPath))
                {
                    return new Icon(icoPath, 32, 32);
                }
                Icon exeIcon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (exeIcon != null) return exeIcon;
            }
            catch { }

            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                using (var bg = new SolidBrush(System.Drawing.Color.FromArgb(250, 249, 245)))
                using (var header = new SolidBrush(System.Drawing.Color.FromArgb(201, 100, 66)))
                using (var border = new System.Drawing.Pen(System.Drawing.Color.FromArgb(229, 224, 213), 2))
                {
                    g.FillRectangle(bg, 3, 3, 26, 26);
                    g.FillRectangle(header, 3, 3, 26, 7);
                    g.DrawRectangle(border, 3, 3, 26, 26);
                }
                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }
    }
}
