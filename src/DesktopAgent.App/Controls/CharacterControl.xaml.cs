using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopAgent.Core.Enums;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace DesktopAgent.App.Controls;

public partial class CharacterControl : System.Windows.Controls.UserControl
{
    private CharacterState _previousState = CharacterState.Idle;
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(CharacterState), typeof(CharacterControl),
        new PropertyMetadata(CharacterState.Idle, OnVisualChanged));
    public static readonly DependencyProperty AnimationEnabledProperty = DependencyProperty.Register(
        nameof(AnimationEnabled), typeof(bool), typeof(CharacterControl),
        new PropertyMetadata(true, OnVisualChanged));
    public static readonly DependencyProperty AnimationIntensityProperty = DependencyProperty.Register(
        nameof(AnimationIntensity), typeof(double), typeof(CharacterControl),
        new PropertyMetadata(0.55, OnVisualChanged));

    public CharacterControl()
    {
        InitializeComponent();
        MouseEnter += (_, _) => { if (AnimationEnabled) Face.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(200))); };
    }
    public CharacterState State { get => (CharacterState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public bool AnimationEnabled { get => (bool)GetValue(AnimationEnabledProperty); set => SetValue(AnimationEnabledProperty, value); }
    public double AnimationIntensity { get => (double)GetValue(AnimationIntensityProperty); set => SetValue(AnimationIntensityProperty, value); }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((CharacterControl)d).UpdateVisual();

    private void UpdateVisual()
    {
        if (!IsInitialized) return;
        var (glow, body, indicator, pulse) = State switch
        {
            CharacterState.Thinking => ("#CDB8FF", "#7554D9", "•••", true),
            CharacterState.Working => ("#8DFFD3", "#2F9B85", "↻", true),
            CharacterState.Attention => ("#FFE08A", "#D17A36", "!", true),
            CharacterState.Sleeping => ("#AEB6CB", "#4B5268", "zZ", false),
            CharacterState.DeveloperSuccess => ("#8DFFD3", "#2F9B85", "✓", false),
            CharacterState.DeveloperError => ("#FFADAD", "#BD4949", "!", false),
            _ => ("#9D96FF", "#5B50D6", string.Empty, false)
        };
        Glow.Color = (MediaColor)MediaColorConverter.ConvertFromString(glow);
        Body.Color = (MediaColor)MediaColorConverter.ConvertFromString(body);
        Indicator.Text = indicator;
        Indicator.Opacity = string.IsNullOrEmpty(indicator) ? 0 : 0.9;
        PulseRing.Opacity = pulse ? 0.8 : 0;
        Face.Opacity = State == CharacterState.Sleeping ? 0.45 : 1;
        if (AnimationEnabled)
        {
            AmbientStoryboard.Storyboard.Resume(this);
            AmbientStoryboard.Storyboard.SetSpeedRatio(this, Math.Clamp(0.55 + AnimationIntensity, 0.55, 1.55));
        }
        else
        {
            AmbientStoryboard.Storyboard.Pause(this);
        }

        PulseRing.BeginAnimation(OpacityProperty, null);
        var completed = State == CharacterState.Idle && _previousState is CharacterState.Working or CharacterState.Thinking;
        _previousState = State;
        if (AnimationEnabled && pulse)
        {
            PulseRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 0.85, TimeSpan.FromSeconds(0.9))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        else if (AnimationEnabled && completed)
            PulseRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 0, TimeSpan.FromMilliseconds(350)));
    }
}
