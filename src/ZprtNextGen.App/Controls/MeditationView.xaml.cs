using System.Windows;
using System.Windows.Controls;
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
        AccentLayer.Opacity = connected ? 0.75 : 0;
        EndDotLayer.Opacity = connected ? 1 : 0;
        EndDotGlow.Opacity = connected ? 0.2 : 0;
    }
}
