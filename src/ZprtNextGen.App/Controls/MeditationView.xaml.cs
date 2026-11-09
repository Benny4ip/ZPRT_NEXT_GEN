using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZprtNextGen.Core.Presentation;

namespace ZprtNextGen.App.Controls;

/// <summary>Отображение состояния медитации: анимированная фигура на холсте 240×240.</summary>
public partial class MeditationView : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(MeditationState),
        typeof(MeditationView),
        new PropertyMetadata(MeditationState.Stopped, OnStateChanged));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(
        nameof(ReduceMotion),
        typeof(bool),
        typeof(MeditationView),
        new PropertyMetadata(false, OnReduceMotionChanged));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(MeditationView),
        new PropertyMetadata(0.0, OnProgressChanged));

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MeditationView)d).UpdateProgressArc();

    /// <summary>Заполнение кольца-прогресса: 0 (пусто) … 1 (полный круг).</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private Storyboard? _progressStoryboard;

    public MeditationState State
    {
        get => (MeditationState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool ReduceMotion
    {
        get => (bool)GetValue(ReduceMotionProperty);
        set => SetValue(ReduceMotionProperty, value);
    }

    public MeditationView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MeditationView)d).ApplyState();

    private static void OnReduceMotionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MeditationView)d).ApplyState();

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyState();

    private void OnUnloaded(object sender, RoutedEventArgs e) => RemoveStoryboards();

    /// <summary>Применяет текущее состояние: останавливает все раскадровки и запускает подходящую.</summary>
    private void ApplyState()
    {
        RemoveStoryboards();

        if (ReduceMotion)
        {
            ApplyReduceMotionStatic();
            return;
        }

        string? storyboardKey = State switch
        {
            MeditationState.Stopped => "StoppedStoryboard",
            MeditationState.Connecting => "ConnectingStoryboard",
            MeditationState.Connected => "ConnectedStoryboard",
            MeditationState.Error => "ErrorStoryboard",
            _ => null
        };

        if (storyboardKey is not null && Resources[storyboardKey] is Storyboard storyboard)
        {
            storyboard.Begin(this, isControllable: true);
        }

        switch (State)
        {
            case MeditationState.Stopped:
                ProgressArc.Opacity = 0;
                AnimateProgress(0.0, new Duration(TimeSpan.FromSeconds(0.4)));
                StopShimmer();
                break;
            case MeditationState.Connecting:
                ProgressArc.Opacity = 0.75;
                AnimateProgress(0.85, new Duration(TimeSpan.FromSeconds(7)));
                StartShimmer();
                break;
            case MeditationState.Connected:
                ProgressArc.Opacity = 1;
                AnimateProgress(1.0, new Duration(TimeSpan.FromSeconds(0.6)));
                StartShimmer();
                break;
            case MeditationState.Error:
                ProgressArc.Opacity = 0.75;
                AnimateProgress(0.5, new Duration(TimeSpan.FromSeconds(0.4)));
                StartShimmer();
                break;
        }
    }

    /// <summary>Анимирует заполнение кольца-прогресса к целевому значению.</summary>
    private void AnimateProgress(double target, Duration duration)
    {
        _progressStoryboard?.Stop(this);
        _progressStoryboard = null;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation(target, duration);
        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, new PropertyPath(ProgressProperty));
        storyboard.Children.Add(animation);
        _progressStoryboard = storyboard;
        storyboard.Begin(this, isControllable: true);
    }

    /// <summary>Запускает зацикленное мерцание линии.</summary>
    private void StartShimmer()
    {
        if (Resources["ShimmerStoryboard"] is Storyboard shimmer)
        {
            shimmer.Begin(this, isControllable: true);
        }
    }

    /// <summary>Останавливает мерцание и гасит слой мерцания.</summary>
    private void StopShimmer()
    {
        if (Resources["ShimmerStoryboard"] is Storyboard shimmer)
        {
            shimmer.Stop(this);
        }

        PulseShimmer.Opacity = 0;
    }

    /// <summary>Останавливает все раскадровки медитации и снимает их анимации с элементов.</summary>
    private void RemoveStoryboards()
    {
        foreach (string key in new[]
                 {
                     "StoppedStoryboard", "ConnectingStoryboard", "ConnectedStoryboard", "ErrorStoryboard"
                 })
        {
            if (Resources[key] is Storyboard storyboard)
            {
                storyboard.Stop(this);
            }
        }

        _progressStoryboard?.Stop(this);
        _progressStoryboard = null;
        StopShimmer();
    }

    /// <summary>Статические значения при Reduce Motion: без зацикленных раскадровок и свечения.</summary>
    private void ApplyReduceMotionStatic()
    {
        BreathTransform.Y = 0;
        ScaleTransform.ScaleX = 1;
        ScaleTransform.ScaleY = 1;
        ErrorFlash.Opacity = 0;

        bool connected = State == MeditationState.Connected;
        bool active = connected || State == MeditationState.Connecting;

        Glow.Opacity = connected ? 0.3 : 0.16;
        PulseLine.Opacity = active ? 0.9 : 0.45;
        PulseGlow.Opacity = connected ? 0.35 : 0;
        ProgressArc.Opacity = connected ? 0.75 : 0;
        Progress = connected ? 1.0 : 0.0;
        EndDotLayer.Opacity = connected ? 1 : 0;
        EndDotGlow.Opacity = connected ? 0.2 : 0;
        PulseShimmer.Opacity = 0;
    }

    /// <summary>Пересчитывает геометрию золотой дуги-кольца по текущему <see cref="Progress"/>.</summary>
    private void UpdateProgressArc()
    {
        var progress = Math.Clamp(Progress, 0.0, 1.0);
        var centerX = 140.0;
        var centerY = 140.0;
        var radius = 96.0;
        var start = new System.Windows.Point(centerX, centerY - radius); // верх окружности (12 часов)

        if (progress <= 0.0)
        {
            ProgressArc.Data = null;
            return;
        }

        var radians = progress * 2.0 * Math.PI;
        var end = new System.Windows.Point(centerX + radius * Math.Sin(radians), centerY - radius * Math.Cos(radians));

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new System.Windows.Size(radius, radius),
            RotationAngle = 0,
            IsLargeArc = progress >= 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });

        ProgressArc.Data = new PathGeometry { Figures = { figure } };
    }
}
