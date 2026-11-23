using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopMemo
{
    public class IosSpringEase : EasingFunctionBase
    {
        public static readonly DependencyProperty DampingProperty =
            DependencyProperty.Register("Damping", typeof(double), typeof(IosSpringEase), new PropertyMetadata(0.76));

        public static readonly DependencyProperty StiffnessProperty =
            DependencyProperty.Register("Stiffness", typeof(double), typeof(IosSpringEase), new PropertyMetadata(14.0));

        public double Damping
        {
            get { return (double)GetValue(DampingProperty); }
            set { SetValue(DampingProperty, value); }
        }

        public double Stiffness
        {
            get { return (double)GetValue(StiffnessProperty); }
            set { SetValue(StiffnessProperty, value); }
        }

        public IosSpringEase()
        {
            EasingMode = EasingMode.EaseOut;
        }

        protected override Freezable CreateInstanceCore()
        {
            return new IosSpringEase();
        }

        protected override double EaseInCore(double normalizedTime)
        {
            // Since EasingMode defaults to EaseOut in constructor, WPF calls EaseInCore(1 - t) for EaseOut.
            // To guarantee clean monotonic damped overshoot with Ease(0)=0 and Ease(1)=1:
            if (normalizedTime <= 0.0) return 0.0;
            if (normalizedTime >= 1.0) return 1.0;

            double t = normalizedTime;
            double decay = Math.Exp(-8.5 * t);
            double wave = Math.Cos(10.5 * t);
            double val = 1.0 - decay * wave;
            // Smoothly clamp endpoint blend at t -> 1
            double blend = t * t * t;
            return val * (1.0 - blend) + 1.0 * blend;
        }
    }

    public static class SpringAnimationHelper
    {
        public static IEasingFunction CreateSmoothSpring(double damping)
        {
            return new CubicEase { EasingMode = EasingMode.EaseOut };
        }

        public static IEasingFunction CreateIosSpring()
        {
            // BackEase with gentle amplitude provides authentic iOS spring overshoot on EaseOut
            return new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 };
        }

        public static IEasingFunction CreateIosFluid()
        {
            return new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5.2 };
        }

        public static void AttachSpringPress(FrameworkElement elem)
        {
            if (elem == null) return;
            elem.RenderTransformOrigin = new Point(0.5, 0.5);
            ScaleTransform scale = elem.RenderTransform as ScaleTransform;
            if (scale == null)
            {
                scale = new ScaleTransform(1.0, 1.0);
                elem.RenderTransform = scale;
            }

            elem.PreviewMouseLeftButtonDown += delegate
            {
                AnimateScale(scale, 0.95, 95, CreateIosFluid());
            };
            elem.PreviewMouseLeftButtonUp += delegate
            {
                AnimateScale(scale, 1.0, 260, CreateIosSpring());
            };
            elem.MouseLeave += delegate
            {
                AnimateScale(scale, 1.0, 200, CreateIosFluid());
            };
        }

        public static void AnimateScale(ScaleTransform scale, double target, int durationMs, IEasingFunction ease)
        {
            if (scale == null) return;
            var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var ax = new DoubleAnimation(target, dur) { EasingFunction = ease };
            var ay = new DoubleAnimation(target, dur) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }

        public static void AnimateSlideIndicator(FrameworkElement pill, double targetX, double targetWidth, int durationMs)
        {
            if (pill == null) return;
            pill.RenderTransformOrigin = new Point(0.5, 0.5);

            TransformGroup tg = pill.RenderTransform as TransformGroup;
            ScaleTransform sc = null;
            TranslateTransform tt = null;

            if (tg != null && tg.Children.Count >= 2)
            {
                sc = tg.Children[0] as ScaleTransform;
                tt = tg.Children[1] as TranslateTransform;
            }

            if (tg == null || sc == null || tt == null)
            {
                sc = new ScaleTransform(1.0, 1.0);
                tt = new TranslateTransform(targetX, 0);
                tg = new TransformGroup();
                tg.Children.Add(sc);
                tg.Children.Add(tt);
                pill.RenderTransform = tg;
                pill.Width = Math.Max(10, targetWidth);
                return;
            }

            int totalMs = Math.Max(320, durationMs);
            var dur = new Duration(TimeSpan.FromMilliseconds(totalMs));
            var spring = CreateIosSpring();
            var fluid = CreateIosFluid();

            var animX = new DoubleAnimation(targetX, dur) { EasingFunction = spring };
            tt.BeginAnimation(TranslateTransform.XProperty, animX);

            if (double.IsNaN(pill.Width) || pill.Width <= 0)
            {
                pill.Width = Math.Max(10, targetWidth);
            }
            else
            {
                var animW = new DoubleAnimation(Math.Max(10, targetWidth), dur) { EasingFunction = fluid };
                pill.BeginAnimation(FrameworkElement.WidthProperty, animW);
            }

            // Liquid droplet stretch-squish morphing during slide
            var kx = new DoubleAnimationUsingKeyFrames();
            kx.KeyFrames.Add(new EasingDoubleKeyFrame(1.13, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(105)), fluid));
            kx.KeyFrames.Add(new EasingDoubleKeyFrame(0.96, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(225)), fluid));
            kx.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(totalMs)), spring));

            var ky = new DoubleAnimationUsingKeyFrames();
            ky.KeyFrames.Add(new EasingDoubleKeyFrame(0.89, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(105)), fluid));
            ky.KeyFrames.Add(new EasingDoubleKeyFrame(1.04, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(225)), fluid));
            ky.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(totalMs)), spring));

            sc.BeginAnimation(ScaleTransform.ScaleXProperty, kx);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, ky);
        }

        public static void AnimateViewTransition(UIElement view, double fromOffsetX, int durationMs)
        {
            if (view == null) return;
            var group = new TransformGroup();
            var scale = new ScaleTransform(0.985, 0.985);
            var trans = new TranslateTransform(fromOffsetX, 0);
            group.Children.Add(scale);
            group.Children.Add(trans);
            view.RenderTransformOrigin = new Point(0.5, 0.5);
            view.RenderTransform = group;
            view.Opacity = 0.0;

            var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var fluid = CreateIosFluid();
            var spring = CreateIosSpring();

            view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds((int)(durationMs * 0.72)))) { EasingFunction = fluid });
            trans.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromOffsetX, 0.0, dur) { EasingFunction = spring });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, 1.0, dur) { EasingFunction = fluid });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.985, 1.0, dur) { EasingFunction = fluid });
        }

        public static void AnimatePopIn(FrameworkElement elem, int durationMs)
        {
            if (elem == null) return;
            elem.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(0.94, 0.94);
            elem.RenderTransform = scale;
            elem.Opacity = 0.0;

            var dur = new Duration(TimeSpan.FromMilliseconds(durationMs));
            var spring = CreateIosSpring();
            var fluid = CreateIosFluid();

            elem.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds((int)(durationMs * 0.7)))) { EasingFunction = fluid });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1.0, dur) { EasingFunction = spring });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1.0, dur) { EasingFunction = spring });
        }
    }
}
