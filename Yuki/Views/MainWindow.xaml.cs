using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Yuki.Views;

public partial class MainWindow : Window {
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int SnowflakeCount = 12;
    private static readonly Random Random = new();
    private static readonly string[] SnowflakeColors = ["#6FCBFF", "#FF9FD1", "#F8F4FF", "#FFE9B3"];

    private readonly List<Storyboard> _activeSnowStoryboards = [];

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public MainWindow() {
        InitializeComponent();

        var screenWidth = SystemParameters.WorkArea.Width;
        var screenHeight = SystemParameters.WorkArea.Height;
        Left = screenWidth - Width - 20;
        Top = screenHeight - Height - 20;

        Loaded += (_, _) => InitializeSnowfall();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) {
        var isVisible = (bool)e.NewValue;
        foreach (var storyboard in _activeSnowStoryboards) {
            if (isVisible) {
                storyboard.Resume(this);
            }
            else {
                storyboard.Pause(this);
            }
        }
    }

    private void InitializeSnowfall() {
        for (var i = 0; i < SnowflakeCount; i++) {
            var flake = new TextBlock {
                Text = "❄",
                FontSize = Random.Next(6, 13),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    SnowflakeColors[Random.Next(SnowflakeColors.Length)])),
                Opacity = 0
            };
            SnowCanvas.Children.Add(flake);
            StartFall(flake, TimeSpan.FromSeconds(Random.NextDouble() * 6));
        }
    }

    private const double EdgeMargin = 24;

    private void StartFall(TextBlock flake, TimeSpan delay) {
        var width = SnowCanvas.ActualWidth > 0 ? SnowCanvas.ActualWidth : 340;
        var height = SnowCanvas.ActualHeight > 0 ? SnowCanvas.ActualHeight : 110;
        var spawnRange = Math.Max(width - 2 * EdgeMargin, 0);

        Canvas.SetLeft(flake, EdgeMargin + Random.NextDouble() * spawnRange);

        var duration = TimeSpan.FromSeconds(5 + Random.NextDouble() * 4);
        var peakOpacity = 0.35 + Random.NextDouble() * 0.35;

        var fall = new DoubleAnimation(-12, height + 12, duration);
        Storyboard.SetTarget(fall, flake);
        Storyboard.SetTargetProperty(fall, new PropertyPath(Canvas.TopProperty));

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(peakOpacity, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(peakOpacity, KeyTime.FromTimeSpan(duration - TimeSpan.FromSeconds(1))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration)));
        Storyboard.SetTarget(fade, flake);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));

        var storyboard = new Storyboard { BeginTime = delay };
        storyboard.Children.Add(fall);
        storyboard.Children.Add(fade);

        storyboard.Completed += (_, _) => {
            _activeSnowStoryboards.Remove(storyboard);
            StartFall(flake, TimeSpan.Zero);
        };

        _activeSnowStoryboards.Add(storyboard);
        storyboard.Begin(this, true);
    }
}
