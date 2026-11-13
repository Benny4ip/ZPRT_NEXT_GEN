#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ZprtNextGen.Core.Connection;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ZprtNextGen.App.Controls;

/// <summary>
/// Орб-светофор главного экрана: цвет и подпись зависят от состояния подключения,
/// кольцо показывает прогресс подбора, на зелёном состоянии появляется галочка.
/// </summary>
public partial class ConnectionOrb : UserControl
{
    private static readonly Color Red = Color.FromRgb(0xE0, 0x1B, 0x2E);
    private static readonly Color Amber = Color.FromRgb(0xF5, 0xA5, 0x24);
    private static readonly Color Green = Color.FromRgb(0x22, 0xC5, 0x5E);
    private static readonly Color Grey = Color.FromRgb(0x8A, 0x8A, 0x8A);

    private const double ArcRadius = 148;

    public ConnectionOrb()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyVisuals(animateCheck: false);
    }

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ConnectionState), typeof(ConnectionOrb),
        new PropertyMetadata(ConnectionState.Disconnected, OnStateChanged));

    /// <summary>Состояние подключения, определяющее цвет и оформление.</summary>
    public ConnectionState State
    {
        get => (ConnectionState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly DependencyProperty MainTextProperty = DependencyProperty.Register(
        nameof(MainText), typeof(string), typeof(ConnectionOrb),
        new PropertyMetadata(string.Empty));

    /// <summary>Крупная подпись внутри орба.</summary>
    public string MainText
    {
        get => (string)GetValue(MainTextProperty);
        set => SetValue(MainTextProperty, value);
    }

    public static readonly DependencyProperty HintTextProperty = DependencyProperty.Register(
        nameof(HintText), typeof(string), typeof(ConnectionOrb),
        new PropertyMetadata(string.Empty));

    /// <summary>Подсказка для экранных дикторов и тултипа.</summary>
    public string HintText
    {
        get => (string)GetValue(HintTextProperty);
        set => SetValue(HintTextProperty, value);
    }

    public static readonly DependencyProperty AutomationNameProperty = DependencyProperty.Register(
        nameof(AutomationName), typeof(string), typeof(ConnectionOrb),
        new PropertyMetadata("Включить обход"));

    /// <summary>Имя элемента для экранных дикторов.</summary>
    public string AutomationName
    {
        get => (string)GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ConnectionOrb),
        new PropertyMetadata(0d, OnProgressChanged));

    /// <summary>Прогресс подбора от 0 до 1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion), typeof(bool), typeof(ConnectionOrb),
        new PropertyMetadata(false));

    /// <summary>Отключить анимации.</summary>
    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(ConnectionOrb),
        new PropertyMetadata(null));

    /// <summary>Команда клика по орбу.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(ConnectionOrb),
        new PropertyMetadata(null));

    /// <summary>Параметр команды клика.</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ConnectionOrb orb)
            orb.ApplyVisuals(animateCheck: true);
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ConnectionOrb orb)
            orb.DrawProgress((double)e.NewValue);
    }

    private void ApplyVisuals(bool animateCheck)
    {
        var accent = State switch
        {
            ConnectionState.Connected => Green,
            ConnectionState.Selecting or ConnectionState.Connecting or ConnectionState.Stopping => Amber,
            ConnectionState.Error => Red,
            _ => Red,
        };

        if (State == ConnectionState.Stopping)
            accent = Grey;

        var accentBrush = new SolidColorBrush(accent);
        var accentColor = accent;

        ProgressArc.Stroke = accentBrush;
        GlowCenter.Color = Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B);

        if (OrbButton.Template?.FindName("Disc", OrbButton) is Ellipse disc)
        {
            disc.Stroke = accentBrush;
            if (disc.Fill is RadialGradientBrush gradient)
            {
                gradient.GradientStops[0].Color = Blend(accentColor, 0.22);
                gradient.GradientStops[1].Color = Color.FromRgb(0x14, 0x14, 0x14);
            }
        }

        if (OrbButton.Template?.FindName("Check", OrbButton) is Path check)
        {
            var connected = State == ConnectionState.Connected;
            check.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            check.Stroke = accentBrush;

            if (connected && animateCheck && !ReduceMotion)
                AnimateCheck(check);
            else
                check.Opacity = connected ? 1 : 0;
        }

        ProgressArc.Visibility = State is ConnectionState.Selecting or ConnectionState.Connecting
            ? Visibility.Visible
            : Visibility.Collapsed;

        DrawProgress(Progress);
    }

    private void AnimateCheck(Path check)
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        var scaleX = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut },
        };

        var scaleY = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut },
        };

        if (check.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        check.BeginAnimation(OpacityProperty, fade);
    }

    private void DrawProgress(double progress)
    {
        var clamped = Math.Clamp(double.IsNaN(progress) ? 0 : progress, 0, 1);

        if (clamped <= 0 || State is not (ConnectionState.Selecting or ConnectionState.Connecting))
        {
            ProgressArc.Data = null;
            return;
        }

        var center = new Point(160, 160);
        var start = new Point(center.X, center.Y - ArcRadius);
        var angle = 360 * clamped;
        var radians = (angle - 90) * Math.PI / 180;
        var end = new Point(
            center.X + ArcRadius * Math.Cos(radians),
            center.Y + ArcRadius * Math.Sin(radians));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(ArcRadius, ArcRadius),
            IsLargeArc = angle > 180,
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ProgressArc.Data = geometry;
    }

    private static Color Blend(Color accent, double alpha)
    {
        var background = Color.FromRgb(0x14, 0x14, 0x14);
        return Color.FromRgb(
            (byte)(background.R + (accent.R - background.R) * alpha),
            (byte)(background.G + (accent.G - background.G) * alpha),
            (byte)(background.B + (accent.B - background.B) * alpha));
    }
}
