using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WpfBrush = System.Windows.Media.Brush;

namespace DesktopMemo
{
    public static class GlassControlStyles
    {
        public static Button CreateGlassButton(string text, WpfBrush bg, WpfBrush fg, WpfBrush border, double fontSize, Thickness padding, double cornerRadius)
        {
            var btn = new Button();
            btn.Content = text;
            btn.Background = bg;
            btn.Foreground = fg;
            btn.BorderBrush = border;
            btn.BorderThickness = border == null ? new Thickness(0) : new Thickness(1);
            btn.FontSize = fontSize;
            btn.FontFamily = ThemeService.AppFontFamily;
            btn.Padding = padding;
            btn.Cursor = Cursors.Hand;
            btn.Focusable = false;

            var template = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            template.VisualTree = bd;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.86));
            template.Triggers.Add(hoverTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
            template.Triggers.Add(disabledTrigger);

            btn.Template = template;
            SpringAnimationHelper.AttachSpringPress(btn);
            return btn;
        }

        public static void ApplyToResources(ResourceDictionary res, ThemePalette pal, UiScaleMetrics scale)
        {
            ApplyToResources(res, pal, scale, true);
        }

        public static void ApplyToResources(ResourceDictionary res, ThemePalette pal, UiScaleMetrics scale, bool includeSliderAndScrollBar)
        {
            if (res == null || pal == null) return;
            if (scale == null) scale = ThemeService.GetScaleMetrics("standard", 13);

            res[typeof(TextBox)] = BuildTextBoxStyle(pal, scale);
            res[typeof(PasswordBox)] = BuildPasswordBoxStyle(pal, scale);
            res[typeof(ComboBox)] = BuildComboBoxStyle(pal, scale);
            res[typeof(ComboBoxItem)] = BuildComboBoxItemStyle(pal, scale);
            res[typeof(CheckBox)] = BuildCheckBoxStyle(pal, scale);
            if (includeSliderAndScrollBar || !res.Contains(typeof(Slider)))
            {
                res[typeof(Slider)] = BuildSliderStyle(pal);
            }
            if (includeSliderAndScrollBar || !res.Contains(typeof(ScrollBar)))
            {
                res[typeof(ScrollBar)] = BuildScrollBarStyle(pal);
            }
            res[typeof(DatePicker)] = BuildDatePickerStyle(pal, scale);
        }

        private static Style BuildTextBoxStyle(ThemePalette pal, UiScaleMetrics scale)
        {
            var s = new Style(typeof(TextBox));
            s.Setters.Add(new Setter(Control.FontFamilyProperty, ThemeService.AppFontFamily));
            s.Setters.Add(new Setter(Control.FontSizeProperty, scale.FontSize));
            s.Setters.Add(new Setter(Control.ForegroundProperty, pal.TextPrimary));
            s.Setters.Add(new Setter(Control.BackgroundProperty, pal.InputBg));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, pal.Border));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(TextBox.CaretBrushProperty, pal.AccentBg));

            var tpl = new ControlTemplate(typeof(TextBox));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(scale.CornerRadius - 1));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var sv = new FrameworkElementFactory(typeof(ScrollViewer));
            sv.Name = "PART_ContentHost";
            sv.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(sv);
            tpl.VisualTree = bd;

            var focusTrg = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrg.Setters.Add(new Setter(Border.BorderBrushProperty, pal.AccentBg, "bd"));
            tpl.Triggers.Add(focusTrg);

            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        private static Style BuildPasswordBoxStyle(ThemePalette pal, UiScaleMetrics scale)
        {
            var s = new Style(typeof(PasswordBox));
            s.Setters.Add(new Setter(Control.FontFamilyProperty, ThemeService.AppFontFamily));
            s.Setters.Add(new Setter(Control.FontSizeProperty, scale.FontSize));
            s.Setters.Add(new Setter(Control.ForegroundProperty, pal.TextPrimary));
            s.Setters.Add(new Setter(Control.BackgroundProperty, pal.InputBg));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, pal.Border));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(PasswordBox.CaretBrushProperty, pal.AccentBg));

            var tpl = new ControlTemplate(typeof(PasswordBox));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(scale.CornerRadius - 1));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var sv = new FrameworkElementFactory(typeof(ScrollViewer));
            sv.Name = "PART_ContentHost";
            sv.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(sv);
            tpl.VisualTree = bd;

            var focusTrg = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focusTrg.Setters.Add(new Setter(Border.BorderBrushProperty, pal.AccentBg, "bd"));
            tpl.Triggers.Add(focusTrg);

            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        private static Style BuildComboBoxStyle(ThemePalette pal, UiScaleMetrics scale)
        {
            var s = new Style(typeof(ComboBox));
            s.Setters.Add(new Setter(Control.FontFamilyProperty, ThemeService.AppFontFamily));
            s.Setters.Add(new Setter(Control.FontSizeProperty, scale.SmallFontSize + 1));
            s.Setters.Add(new Setter(Control.ForegroundProperty, pal.TextPrimary));
            s.Setters.Add(new Setter(Control.BackgroundProperty, pal.InputBg));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, pal.Border));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            s.Setters.Add(new Setter(Control.PaddingProperty, scale.InputPadding));
            s.Setters.Add(new Setter(Control.MinHeightProperty, scale.ButtonHeight));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            var tpl = new ControlTemplate(typeof(ComboBox));
            var root = new FrameworkElementFactory(typeof(Grid));

            // ToggleButton covering whole ComboBox
            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.Name = "ToggleButton";
            toggle.SetValue(Control.FocusableProperty, false);
            toggle.SetValue(Control.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            toggle.SetValue(Control.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            toggle.SetValue(Control.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            toggle.SetValue(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsDropDownOpen")
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });

            var toggleTpl = new ControlTemplate(typeof(ToggleButton));
            var tBd = new FrameworkElementFactory(typeof(Border));
            tBd.Name = "tBd";
            tBd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            tBd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            tBd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            tBd.SetValue(Border.CornerRadiusProperty, new CornerRadius(scale.CornerRadius - 1));
            var tGrid = new FrameworkElementFactory(typeof(Grid));
            var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var c2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c2.SetValue(ColumnDefinition.WidthProperty, new GridLength(22));
            tGrid.AppendChild(c1);
            tGrid.AppendChild(c2);

            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.SetValue(Grid.ColumnProperty, 1);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(Shape.StrokeProperty, pal.TextSecondary);
            arrow.SetValue(Shape.StrokeThicknessProperty, 1.6);
            arrow.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0,0 L 4,4 L 8,0"));
            tGrid.AppendChild(arrow);

            tBd.AppendChild(tGrid);
            toggleTpl.VisualTree = tBd;
            var tHover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            tHover.Setters.Add(new Setter(Border.BorderBrushProperty, pal.AccentBg, "tBd"));
            toggleTpl.Triggers.Add(tHover);
            toggle.SetValue(Control.TemplateProperty, toggleTpl);
            root.AppendChild(toggle);

            // ContentPresenter
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.Name = "ContentSite";
            cp.SetValue(UIElement.IsHitTestVisibleProperty, false);
            cp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            cp.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 3, 24, 3));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            root.AppendChild(cp);

            // Editable TextBox for editable ComboBox (Hour/Min)
            var editTb = new FrameworkElementFactory(typeof(TextBox));
            editTb.Name = "PART_EditableTextBox";
            editTb.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            editTb.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            editTb.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            editTb.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 2, 22, 2));
            editTb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(editTb);

            // Popup dropdown
            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(UIElement.FocusableProperty, false);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetValue(Popup.IsOpenProperty, new TemplateBindingExtension(ComboBox.IsDropDownOpenProperty));

            var dropBorder = new FrameworkElementFactory(typeof(Border));
            dropBorder.SetValue(Border.BackgroundProperty, pal.PopupBg);
            dropBorder.SetValue(Border.BorderBrushProperty, pal.Border);
            dropBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            dropBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            dropBorder.SetValue(Border.PaddingProperty, new Thickness(4));
            dropBorder.SetValue(Border.MarginProperty, new Thickness(0, 4, 0, 8));
            dropBorder.SetValue(FrameworkElement.MinWidthProperty, new TemplateBindingExtension(FrameworkElement.ActualWidthProperty));
            dropBorder.SetValue(FrameworkElement.MaxHeightProperty, 240.0);
            dropBorder.SetValue(UIElement.EffectProperty, new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 4,
                Opacity = pal.IsDark ? 0.42 : 0.16
            });

            var dropScroll = new FrameworkElementFactory(typeof(ScrollViewer));
            dropScroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            dropScroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            dropScroll.AppendChild(itemsPresenter);
            dropBorder.AppendChild(dropScroll);
            popup.AppendChild(dropBorder);
            root.AppendChild(popup);

            tpl.VisualTree = root;

            var editTrg = new Trigger { Property = ComboBox.IsEditableProperty, Value = true };
            editTrg.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "PART_EditableTextBox"));
            editTrg.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Hidden, "ContentSite"));
            tpl.Triggers.Add(editTrg);

            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        private static Style BuildComboBoxItemStyle(ThemePalette pal, UiScaleMetrics scale)
        {
            var s = new Style(typeof(ComboBoxItem));
            s.Setters.Add(new Setter(Control.FontFamilyProperty, ThemeService.AppFontFamily));
            s.Setters.Add(new Setter(Control.FontSizeProperty, scale.SmallFontSize + 1));
            s.Setters.Add(new Setter(Control.ForegroundProperty, pal.TextPrimary));
            s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            var tpl = new ControlTemplate(typeof(ComboBoxItem));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bd.SetValue(Border.MarginProperty, new Thickness(0, 1, 0, 1));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, pal.HoverBg, "bd"));
            tpl.Triggers.Add(hover);

            var sel = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, pal.AccentBg, "bd"));
            sel.Setters.Add(new Setter(Control.ForegroundProperty, pal.AccentFg));
            tpl.Triggers.Add(sel);

            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        private static Style BuildCheckBoxStyle(ThemePalette pal, UiScaleMetrics scale)
        {
            var s = new Style(typeof(CheckBox));
            s.Setters.Add(new Setter(Control.FontFamilyProperty, ThemeService.AppFontFamily));
            s.Setters.Add(new Setter(Control.FontSizeProperty, scale.FontSize));
            s.Setters.Add(new Setter(Control.ForegroundProperty, pal.TextPrimary));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            var tpl = new ControlTemplate(typeof(CheckBox));
            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var box = new FrameworkElementFactory(typeof(Border));
            box.Name = "box";
            box.SetValue(FrameworkElement.WidthProperty, 17.0);
            box.SetValue(FrameworkElement.HeightProperty, 17.0);
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            box.SetValue(Border.BackgroundProperty, pal.InputBg);
            box.SetValue(Border.BorderBrushProperty, pal.Border);
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1.4));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var check = new FrameworkElementFactory(typeof(Path));
            check.Name = "check";
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            check.SetValue(Shape.StrokeProperty, pal.AccentFg);
            check.SetValue(Shape.StrokeThicknessProperty, 2.0);
            check.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            check.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            check.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            check.SetValue(Path.DataProperty, Geometry.Parse("M 1,4.5 L 4,7.5 L 9.5,1"));
            box.AppendChild(check);
            sp.AppendChild(box);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 0, 0, 0));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            sp.AppendChild(cp);

            tpl.VisualTree = sp;

            var checkedTrg = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrg.Setters.Add(new Setter(Border.BackgroundProperty, pal.AccentBg, "box"));
            checkedTrg.Setters.Add(new Setter(Border.BorderBrushProperty, pal.AccentBg, "box"));
            checkedTrg.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "check"));
            tpl.Triggers.Add(checkedTrg);

            var hoverTrg = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrg.Setters.Add(new Setter(Border.BorderBrushProperty, pal.AccentBg, "box"));
            tpl.Triggers.Add(hoverTrg);

            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        private static Style BuildSliderStyle(ThemePalette pal)
        {
            string xaml = string.Format(
                "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Slider\">" +
                "<Setter Property=\"Cursor\" Value=\"Hand\"/>" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"Slider\">" +
                "<Grid MinHeight=\"20\">" +
                "<Border Height=\"4\" CornerRadius=\"2\" Background=\"{0}\" VerticalAlignment=\"Center\"/>" +
                "<Track x:Name=\"PART_Track\">" +
                "<Track.DecreaseRepeatButton><RepeatButton Command=\"Slider.DecreaseLarge\" Opacity=\"0\" Background=\"Transparent\"/></Track.DecreaseRepeatButton>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command=\"Slider.IncreaseLarge\" Opacity=\"0\" Background=\"Transparent\"/></Track.IncreaseRepeatButton>" +
                "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType=\"Thumb\">" +
                "<Border Width=\"15\" Height=\"15\" CornerRadius=\"7.5\" Background=\"#FFFFFF\" BorderBrush=\"{1}\" BorderThickness=\"2\"/>" +
                "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>",
                pal.BorderHex, pal.AccentBgHex);
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static Style BuildScrollBarStyle(ThemePalette pal)
        {
            string xaml = string.Format(
                "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ScrollBar\">" +
                "<Setter Property=\"Width\" Value=\"6\"/>" +
                "<Setter Property=\"MinWidth\" Value=\"6\"/>" +
                "<Setter Property=\"Background\" Value=\"Transparent\"/>" +
                "<Setter Property=\"Template\"><Setter.Value>" +
                "<ControlTemplate TargetType=\"ScrollBar\">" +
                "<Track x:Name=\"PART_Track\" IsDirectionReversed=\"True\">" +
                "<Track.DecreaseRepeatButton><RepeatButton Command=\"ScrollBar.PageUpCommand\" Opacity=\"0\" Background=\"Transparent\"/></Track.DecreaseRepeatButton>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command=\"ScrollBar.PageDownCommand\" Opacity=\"0\" Background=\"Transparent\"/></Track.IncreaseRepeatButton>" +
                "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType=\"Thumb\">" +
                "<Border CornerRadius=\"3\" Background=\"{0}\" Opacity=\"0.65\"/>" +
                "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "</Track></ControlTemplate></Setter.Value></Setter></Style>",
                pal.BorderHex);
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static Style BuildDatePickerStyle(ThemePalette pal, UiScaleMetrics scale)
        {
            var s = new Style(typeof(DatePicker));
            s.Setters.Add(new Setter(Control.FontFamilyProperty, ThemeService.AppFontFamily));
            s.Setters.Add(new Setter(Control.FontSizeProperty, scale.SmallFontSize + 1));
            s.Setters.Add(new Setter(Control.ForegroundProperty, pal.TextPrimary));
            s.Setters.Add(new Setter(Control.BackgroundProperty, pal.InputBg));
            s.Setters.Add(new Setter(Control.BorderBrushProperty, pal.Border));
            s.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));

            var tpl = new ControlTemplate(typeof(DatePicker));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(scale.CornerRadius - 1));
            bd.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 2));

            var grid = new FrameworkElementFactory(typeof(Grid));
            var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var c2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(c1);
            grid.AppendChild(c2);

            var tb = new FrameworkElementFactory(typeof(DatePickerTextBox));
            tb.Name = "PART_TextBox";
            tb.SetValue(Grid.ColumnProperty, 0);
            tb.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            tb.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            tb.SetValue(Control.ForegroundProperty, pal.TextPrimary);
            tb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(tb);

            var btn = new FrameworkElementFactory(typeof(Button));
            btn.Name = "PART_Button";
            btn.SetValue(Grid.ColumnProperty, 1);
            btn.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            btn.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            btn.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            btn.SetValue(Control.PaddingProperty, new Thickness(4, 2, 4, 2));
            btn.SetValue(ContentControl.ContentProperty, "📅");
            grid.AppendChild(btn);

            var pop = new FrameworkElementFactory(typeof(Popup));
            pop.Name = "PART_Popup";
            pop.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            pop.SetValue(Popup.AllowsTransparencyProperty, true);
            pop.SetValue(Popup.StaysOpenProperty, false);
            grid.AppendChild(pop);

            bd.AppendChild(grid);
            tpl.VisualTree = bd;
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }
    }
}
