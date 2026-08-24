using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace Vrinfo.Mail.App;

public sealed class PushToastWindow : Window
{
    private static readonly Queue<(string Title, string Body)> Queue = new();
    private static bool _showing;

    public static void Enqueue(string title, string body)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
            return;
        app.Dispatcher.Invoke(() =>
        {
            Queue.Enqueue((title, body));
            if (!_showing)
                ShowNext();
        });
    }

    private static void ShowNext()
    {
        if (Queue.Count == 0)
        {
            _showing = false;
            return;
        }

        _showing = true;
        var item = Queue.Dequeue();
        var toast = new PushToastWindow(item.Title, item.Body);
        toast.Closed += (_, _) => ShowNext();
        toast.Show();
    }

    private PushToastWindow(string title, string body)
    {
        Width = 440;
        Height = 96;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;

        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + 36;

        var bar = new WpfProgressBar
        {
            Height = 3,
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            Foreground = MediaBrushes.White,
            Background = new SolidColorBrush(MediaColor.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(0)
        };

        var root = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 12, 16, 10),
            Background = new SolidColorBrush(Theme.IsDark
                ? MediaColor.FromArgb(220, 28, 38, 51)
                : MediaColor.FromArgb(235, 21, 101, 192)),
            BorderBrush = new SolidColorBrush(Theme.IsDark
                ? MediaColor.FromArgb(90, 197, 205, 216)
                : MediaColor.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24,
                Opacity = 0.28,
                ShadowDepth = 0
            },
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    bar,
                    new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = string.IsNullOrWhiteSpace(title) ? "Novo e-mail" : title,
                                FontWeight = FontWeights.SemiBold,
                                FontSize = 14,
                                Foreground = MediaBrushes.White,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            },
                            new TextBlock
                            {
                                Text = body ?? "",
                                Margin = new Thickness(0, 4, 0, 0),
                                Foreground = Theme.IsDark
                                    ? new SolidColorBrush(MediaColor.FromArgb(230, 241, 245, 249))
                                    : MediaBrushes.White,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            }
                        }
                    }
                }
            }
        };
        DockPanel.SetDock(bar, Dock.Bottom);
        Content = root;
        Opacity = 0;
        MouseLeftButtonUp += (_, _) =>
        {
            if (System.Windows.Application.Current.MainWindow is { } main)
            {
                main.Show();
                main.WindowState = WindowState.Normal;
                main.Activate();
            }
            Close();
        };

        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            var drain = new DoubleAnimation(100, 0, TimeSpan.FromSeconds(10));
            drain.Completed += (_, _) =>
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
                fade.Completed += (_, _) => Close();
                BeginAnimation(OpacityProperty, fade);
            };
            bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, drain);
        };
    }
}
