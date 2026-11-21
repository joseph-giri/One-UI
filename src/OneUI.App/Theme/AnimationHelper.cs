using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OneUI.App.Theme;

/// <summary>
/// Provides fluid macOS-inspired spring and cubic-ease UI animations for OneUI windows and controls.
/// </summary>
public static class AnimationHelper
{
    public static void AnimatePopIn(FrameworkElement target, double fromScale = 0.93, double fromOffsetY = -10, int durationMs = 210)
    {
        var scale = new ScaleTransform(fromScale, fromScale);
        var translate = new TranslateTransform(0, fromOffsetY);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(translate);

        target.RenderTransformOrigin = new Point(0.5, 0.0);
        target.RenderTransform = group;
        target.Opacity = 0;

        var dur = TimeSpan.FromMilliseconds(durationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var springEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 };

        target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs * 0.75)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, 1.0, dur) { EasingFunction = springEase });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, 1.0, dur) { EasingFunction = springEase });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromOffsetY, 0, dur) { EasingFunction = ease });
    }

    public static void AnimateCenterPopIn(FrameworkElement target, double fromScale = 0.95, int durationMs = 175)
    {
        var scale = new ScaleTransform(fromScale, fromScale);
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        target.RenderTransform = scale;
        target.Opacity = 0;

        var dur = TimeSpan.FromMilliseconds(durationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };

        target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs * 0.8)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, 1.0, dur) { EasingFunction = spring });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, 1.0, dur) { EasingFunction = spring });
    }

    public static void AnimateFadeIn(UIElement target, int durationMs = 150)
    {
        target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    public static void AnimateLaunchBounce(TranslateTransform translate, bool doubleHop = true)
    {
        var keyframes = new DoubleAnimationUsingKeyFrames();
        keyframes.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        keyframes.KeyFrames.Add(new EasingDoubleKeyFrame(-18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        keyframes.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),
            new QuadraticEase { EasingMode = EasingMode.EaseIn }));

        if (doubleHop)
        {
            keyframes.KeyFrames.Add(new EasingDoubleKeyFrame(-10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            keyframes.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(460)),
                new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        }

        translate.BeginAnimation(TranslateTransform.YProperty, keyframes);
    }
}
