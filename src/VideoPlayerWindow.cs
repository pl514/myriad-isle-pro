using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace IslandShot
{
    /// <summary>应用内视频播放器，避免依赖系统 .mp4 文件关联。</summary>
    internal sealed class VideoPlayerWindow : Window
    {
        readonly MediaElement player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform };
        readonly Slider progress = new Slider { Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock time = new TextBlock { Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        readonly Button playButton;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        bool dragging;
        bool opened;
        bool playing;

        public VideoPlayerWindow(string path)
        {
            Title = "视频 · " + Path.GetFileName(path);
            Width = 920;
            Height = 620;
            MinWidth = 480;
            MinHeight = 320;
            Background = new SolidColorBrush(Color.FromRgb(10, 12, 16));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            ShowInTaskbar = true;
            Topmost = true;

            var root = new DockPanel();
            var bar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 24, 32)),
                Padding = new Thickness(10, 8, 10, 8)
            };
            DockPanel.SetDock(bar, Dock.Bottom);
            var controls = new Grid();
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition());
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            playButton = new Button
            {
                Content = "暂停",
                Width = 64,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(124, 108, 242)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            playButton.Click += delegate { TogglePlay(); };
            controls.Children.Add(playButton);
            Grid.SetColumn(progress, 1);
            controls.Children.Add(progress);
            Grid.SetColumn(time, 2);
            controls.Children.Add(time);
            bar.Child = controls;

            root.Children.Add(bar);
            var surface = new Grid { Background = Brushes.Black, ClipToBounds = true };
            surface.Children.Add(player);
            root.Children.Add(surface);
            Content = root;

            player.MediaOpened += delegate
            {
                opened = true;
                if (player.NaturalDuration.HasTimeSpan) progress.Maximum = player.NaturalDuration.TimeSpan.TotalSeconds;
                player.Play();
                playing = true;
                playButton.Content = "暂停";
                timer.Start();
            };
            player.MediaEnded += delegate
            {
                player.Stop();
                playing = false;
                playButton.Content = "播放";
                progress.Value = 0;
                time.Text = "00:00 / " + Format(player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.Zero);
            };
            player.MediaFailed += delegate(object sender, ExceptionRoutedEventArgs e)
            {
                timer.Stop();
                playing = false;
                var error = new TextBlock
                {
                    Text = "无法播放这个视频\n" + (e.ErrorException == null ? "" : e.ErrorException.Message),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                surface.Children.Add(error);
            };
            progress.PreviewMouseDown += delegate { dragging = true; };
            progress.PreviewMouseUp += delegate
            {
                dragging = false;
                if (opened && player.NaturalDuration.HasTimeSpan) player.Position = TimeSpan.FromSeconds(progress.Value);
            };
            timer.Tick += delegate
            {
                if (!opened || dragging) return;
                try
                {
                    progress.Value = player.Position.TotalSeconds;
                    time.Text = Format(player.Position) + " / " + Format(player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.Zero);
                }
                catch (Exception) { }
            };
            Loaded += delegate
            {
                try
                {
                    player.Source = new Uri(path, UriKind.Absolute);
                    player.Play();
                }
                catch (Exception exception)
                {
                    DragNative.Log("video player failed: " + exception.Message);
                }
            };
            Closed += delegate { try { player.Stop(); player.Close(); } catch (Exception) { } timer.Stop(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
                if (e.Key == Key.Space) { TogglePlay(); e.Handled = true; }
            };
        }

        void TogglePlay()
        {
            if (!opened) return;
            if (playing)
            {
                player.Pause();
                playing = false;
                playButton.Content = "播放";
            }
            else
            {
                player.Play();
                playing = true;
                playButton.Content = "暂停";
            }
        }

        static string Format(TimeSpan value)
        {
            if (value.TotalHours >= 1) return value.ToString(@"h\:mm\:ss");
            return value.ToString(@"m\:ss");
        }
    }
}
