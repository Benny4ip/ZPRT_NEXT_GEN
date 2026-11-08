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

    /// <summary>Статические значения при Reduce Motion: без зацикленных раскадровок, частиц и вращения.</summary>
    private void ApplyReduceMotionStatic()
    {
        LiftTransform.Y = -32;
        BreathTransform.Y = 0;
        ScaleTransform.ScaleX = 1;
        ScaleTransform.ScaleY = 1;
        RaysRotate.Angle = 0;
        RaysGroup.Opacity = 0.25;
        HaloDiamond.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xC6, 0x5C));
        HaloDiamond.Opacity = 0.5;
        AuraGlow.Opacity = 0.12;
        ErrorFlash.Opacity = 0;

        for (int i = 1; i <= 12; i++)
        {
            if (FindName($"Particle{i:D2}") is FrameworkElement particle)
            {
                particle.Opacity = 0;
            }
        }
    }
}
