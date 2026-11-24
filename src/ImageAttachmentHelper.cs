using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Gdi = System.Drawing;
using Gdi2D = System.Drawing.Drawing2D;
using GdiImaging = System.Drawing.Imaging;

namespace DesktopMemo
{
    public static class ImageAttachmentHelper
    {
        public static bool IsSupportedImageExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".webp";
        }

        public static string DetectMimeType(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return "image/jpeg";
            if (bytes[0] == 0xFF && bytes[1] == 0xD8) return "image/jpeg";
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "image/gif";
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            {
                return "image/webp";
            }
            return "image/jpeg";
        }

        public static string DetectFileExtension(byte[] bytes)
        {
            string mime = DetectMimeType(bytes);
            if (mime == "image/png") return ".png";
            if (mime == "image/gif") return ".gif";
            if (mime == "image/webp") return ".webp";
            return ".jpg";
        }

        public static string ToDataUrl(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            string mime = DetectMimeType(bytes);
            return "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
        }

        public static byte[] CompressImageFileToJpeg(string filePath, int maxEdge, long quality)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            byte[] raw = File.ReadAllBytes(filePath);
            if (raw == null || raw.Length == 0) return null;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".webp" && raw.Length <= 4 * 1024 * 1024)
            {
                return raw;
            }

            try
            {
                using (var ms = new MemoryStream(raw))
                {
                    return ResizeAndEncodeJpegStream(ms, maxEdge, quality);
                }
            }
            catch
            {
                return raw.Length <= 6 * 1024 * 1024 ? raw : null;
            }
        }

        public static byte[] CompressBitmapSourceToJpeg(BitmapSource source, int maxEdge, long quality)
        {
            if (source == null) return null;
            try
            {
                using (var ms = new MemoryStream())
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(source));
                    enc.Save(ms);
                    ms.Position = 0;
                    return ResizeAndEncodeJpegStream(ms, maxEdge, quality);
                }
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ResizeAndEncodeJpegStream(Stream srcStream, int maxEdge, long quality)
        {
            using (var srcImg = Gdi.Image.FromStream(srcStream, false, true))
            {
                int origW = srcImg.Width;
                int origH = srcImg.Height;
                if (origW <= 0 || origH <= 0) return null;

                int targetW = origW;
                int targetH = origH;
                int longest = Math.Max(origW, origH);
                if (maxEdge > 0 && longest > maxEdge)
                {
                    double ratio = (double)maxEdge / longest;
                    targetW = Math.Max(1, (int)Math.Round(origW * ratio));
                    targetH = Math.Max(1, (int)Math.Round(origH * ratio));
                }

                using (var canvas = new Gdi.Bitmap(targetW, targetH, GdiImaging.PixelFormat.Format24bppRgb))
                {
                    canvas.SetResolution(96, 96);
                    using (var g = Gdi.Graphics.FromImage(canvas))
                    {
                        g.Clear(Gdi.Color.White);
                        g.CompositingQuality = Gdi2D.CompositingQuality.HighQuality;
                        g.InterpolationMode = Gdi2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = Gdi2D.SmoothingMode.HighQuality;
                        g.DrawImage(srcImg, 0, 0, targetW, targetH);
                    }

                    GdiImaging.ImageCodecInfo jpgCodec = GetJpegCodec();
                    using (var outMs = new MemoryStream())
                    {
                        if (jpgCodec != null)
                        {
                            using (var ep = new GdiImaging.EncoderParameters(1))
                            {
                                ep.Param[0] = new GdiImaging.EncoderParameter(GdiImaging.Encoder.Quality, Math.Max(40L, Math.Min(95L, quality)));
                                canvas.Save(outMs, jpgCodec, ep);
                            }
                        }
                        else
                        {
                            canvas.Save(outMs, GdiImaging.ImageFormat.Jpeg);
                        }
                        return outMs.ToArray();
                    }
                }
            }
        }

        private static GdiImaging.ImageCodecInfo GetJpegCodec()
        {
            GdiImaging.ImageCodecInfo[] codecs = GdiImaging.ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].FormatID == GdiImaging.ImageFormat.Jpeg.Guid)
                {
                    return codecs[i];
                }
            }
            return null;
        }

        public static BitmapImage LoadThumbnail(string filePath, int decodeWidth)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                return LoadThumbnailFromBytes(bytes, decodeWidth);
            }
            catch
            {
                return null;
            }
        }

        public static BitmapImage LoadThumbnailFromBytes(byte[] bytes, int decodeWidth)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void ShowImagePreviewDialog(List<string> imagePaths, int initialIndex, Window owner, string themeKey)
        {
            if (imagePaths == null || imagePaths.Count == 0) return;
            var validPaths = new List<string>();
            for (int i = 0; i < imagePaths.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(imagePaths[i]) && File.Exists(imagePaths[i]))
                {
                    validPaths.Add(imagePaths[i]);
                }
            }
            if (validPaths.Count == 0)
            {
                MessageBox.Show("本地暂未找到可预览的附件图片（如刚刚开启同步，请稍候自动下载完成）。", "图片预览", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int idx = Math.Max(0, Math.Min(initialIndex, validPaths.Count - 1));
            ThemePalette pal = ThemeService.GetPalette(themeKey, 0.98);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics("standard", 13);

            var win = new Window
            {
                Title = "图片附件预览",
                Width = 820,
                Height = 620,
                MinWidth = 480,
                MinHeight = 360,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = true
            };
            if (owner != null && owner.IsVisible) win.Owner = owner;

            var outer = new Border
            {
                Margin = new Thickness(12),
                CornerRadius = new CornerRadius(14),
                Background = pal.PopupBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 4, Opacity = 0.25 }
            };

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Border
            {
                Background = pal.HeaderBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Padding = new Thickness(16, 10, 12, 10)
            };
            header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { win.DragMove(); } catch { } }
            };
            var hDock = new DockPanel();
            var titleTb = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.FontSize + 1,
                FontWeight = FontWeights.Bold,
                Foreground = pal.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(titleTb, Dock.Left);
            hDock.Children.Add(titleTb);

            var closeBtn = GlassControlStyles.CreateGlassButton("✕", Brushes.Transparent, pal.TextSecondary, null, 13, new Thickness(8, 2, 8, 2), 7);
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Click += delegate { win.Close(); };
            DockPanel.SetDock(closeBtn, Dock.Right);
            hDock.Children.Add(closeBtn);
            header.Child = hDock;
            Grid.SetRow(header, 0);
            rootGrid.Children.Add(header);

            var imgHost = new Border
            {
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8)
            };
            var imgCtrl = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            imgHost.Child = imgCtrl;
            Grid.SetRow(imgHost, 1);
            rootGrid.Children.Add(imgHost);

            var footer = new DockPanel { Margin = new Thickness(16, 0, 16, 14) };
            var navBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var prevBtn = GlassControlStyles.CreateGlassButton("◀ 上一张", pal.SurfaceBg, pal.TextPrimary, pal.Border, 12, new Thickness(10, 4, 10, 4), 7);
            prevBtn.Margin = new Thickness(0, 0, 6, 0);
            var nextBtn = GlassControlStyles.CreateGlassButton("下一张 ▶", pal.SurfaceBg, pal.TextPrimary, pal.Border, 12, new Thickness(10, 4, 10, 4), 7);
            navBtns.Children.Add(prevBtn);
            navBtns.Children.Add(nextBtn);
            DockPanel.SetDock(navBtns, Dock.Left);
            footer.Children.Add(navBtns);

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var openExtBtn = GlassControlStyles.CreateGlassButton("用系统看图打开", pal.HoverBg, pal.TextPrimary, pal.Border, 12, new Thickness(10, 4, 10, 4), 7);
            openExtBtn.Margin = new Thickness(0, 0, 8, 0);
            openExtBtn.Click += delegate
            {
                try { Process.Start(new ProcessStartInfo { FileName = validPaths[idx], UseShellExecute = true }); } catch { }
            };
            var okBtn = GlassControlStyles.CreateGlassButton("关闭", pal.AccentBg, pal.AccentFg, null, 12, new Thickness(14, 4, 14, 4), 7);
            okBtn.Click += delegate { win.Close(); };
            rightBtns.Children.Add(openExtBtn);
            rightBtns.Children.Add(okBtn);
            footer.Children.Add(rightBtns);
            Grid.SetRow(footer, 2);
            rootGrid.Children.Add(footer);

            Action render = delegate
            {
                string p = validPaths[idx];
                titleTb.Text = string.Format("🖼 图片预览 ({0}/{1}) · {2}", idx + 1, validPaths.Count, Path.GetFileName(p));
                imgCtrl.Source = LoadThumbnail(p, 0);
                prevBtn.IsEnabled = validPaths.Count > 1;
                nextBtn.IsEnabled = validPaths.Count > 1;
            };
            prevBtn.Click += delegate
            {
                idx = (idx - 1 + validPaths.Count) % validPaths.Count;
                render();
            };
            nextBtn.Click += delegate
            {
                idx = (idx + 1) % validPaths.Count;
                render();
            };
            win.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) win.Close();
                else if (e.Key == Key.Left && validPaths.Count > 1) { idx = (idx - 1 + validPaths.Count) % validPaths.Count; render(); }
                else if (e.Key == Key.Right && validPaths.Count > 1) { idx = (idx + 1) % validPaths.Count; render(); }
            };

            outer.Child = rootGrid;
            win.Content = outer;
            render();
            win.Loaded += delegate { SpringAnimationHelper.AnimatePopIn(outer, 240); };
            win.ShowDialog();
        }
    }
}
