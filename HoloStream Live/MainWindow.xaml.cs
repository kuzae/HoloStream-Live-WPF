using HoloStream_Live.Services;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HoloStream_Live
{
    public partial class MainWindow : Window
    {
        private const int MaxColumns = 5;
        private const int ThumbnailWidth = 50;
        private const int ThumbnailHeight = 50;
        private const int frameMaxWidth = 300;
        private const int frameMaxHeight = 300;
        private const int scheduleListHeight = 60;
        private const int minColumns = 2;
        private List<StreamItem> _cachedStreams = new();
        private readonly string _logFilePath;
        private CancellationTokenSource _timerCancellationTokenSource;
        private bool _isLayout1Active;
        private bool _isUpdating;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApp();
        }

        private void InitializeApp()
        {
            Layout1.Visibility = Visibility.Visible;
            Layout2.Visibility = Visibility.Hidden;
            Title = string.Empty;
            InitializeStreamsAsync();
            InitializeWebView2();
        }

        private async Task InitializeStreamsAsync()
        {
            await FetchStreamsAsync();
            LoadGrid(Layout1Mode: true); // Initial grid load for Layout1
            LoadSchedule(ScheduleContainer); // Load schedule for Layout1
        }
        private async Task FetchStreamsAsync()
        {
            try
            {
                var scraper = new ScheduleScraper();
                var url = "https://hololive.hololivepro.com/en/schedule/";
                _cachedStreams = await scraper.ScrapeStreamsAsync(url);

                LogToFile($"Fetched {_cachedStreams.Count} streams.");

                // Filter the streams: remove streams that have passed and are not live
                var now = DateTime.UtcNow;
                var filteredStreams = _cachedStreams.Where(stream =>
                {
                    if (DateTime.TryParseExact(stream.Start, "MM.dd HH:mm", null, System.Globalization.DateTimeStyles.None, out var startTime))
                    {
                        var utcStartTime = TimeZoneInfo.ConvertTimeToUtc(startTime, TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"));
                        return utcStartTime >= now.AddMinutes(-15) || stream.LiveStatus == "Live";
                    }
                    else
                    {
                        LogToFile($"Invalid start time format for stream: {stream.Name} - {stream.Start}");
                        return false;
                    }
                }).ToList();

                LogToFile($"Filtered streams: {filteredStreams.Count} streams remaining after filtering.");

                _cachedStreams = filteredStreams;

                if (_cachedStreams.Count == 0)
                {
                    LogToFile("No valid streams found after filtering.");
                }
            }
            catch (Exception ex)
            {
                LogToFile($"Error fetching streams: {ex.Message}");
            }
        }
        private async void InitializeWebView2()
        {
            try
            {
                await VideoPlayer.EnsureCoreWebView2Async();
                if (VideoPlayer.CoreWebView2 != null)
                {
                    VideoPlayer.CoreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;
                    VideoPlayer.CoreWebView2.ContainsFullScreenElementChanged += CoreWebView2_ContainsFullScreenElementChanged;

                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void CoreWebView2_ContainsFullScreenElementChanged(object sender, object e)
        {
            if (VideoPlayer.CoreWebView2.ContainsFullScreenElement)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;

                VideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
                VideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;

                Grid.SetRow(VideoPlayer, 0);
                Grid.SetRowSpan(VideoPlayer, 2);
                Grid.SetColumn(VideoPlayer, 0);
                Grid.SetColumnSpan(VideoPlayer, 2);

                VideoPlayer.Width = this.ActualWidth; // Match the full window width
                VideoPlayer.Height = this.ActualHeight; // Match the full window height
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;

                VideoPlayer.HorizontalAlignment = HorizontalAlignment.Stretch;
                VideoPlayer.VerticalAlignment = VerticalAlignment.Stretch;

                Grid.SetRow(VideoPlayer, 0);
                Grid.SetRowSpan(VideoPlayer, 1);
                Grid.SetColumn(VideoPlayer, 0);
                Grid.SetColumnSpan(VideoPlayer, 1);

                VideoPlayer.Width = double.NaN; // Restore default width
                VideoPlayer.Height = double.NaN; // Restore default height
            }
        }
        private void CoreWebView2_PermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            e.State = CoreWebView2PermissionState.Allow;
        }
        private void CoreWebView2_ContainsFullScreenElementChanged(object sender, EventArgs e)
        {
            if (VideoPlayer.CoreWebView2.ContainsFullScreenElement)
            {
                // Enter fullscreen mode
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            else
            {
                // Exit fullscreen mode
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
        }


        private async void StartGridReloadTimer()
        {
            _timerCancellationTokenSource = new CancellationTokenSource();

            try
            {
                while (!_timerCancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(30), _timerCancellationTokenSource.Token);

                    if (_isUpdating) continue; // Prevent re-entrant calls
                    _isUpdating = true;

                    try
                    {
                        await FetchStreamsAsync();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_isLayout1Active)
                            {
                                LoadGrid(Layout1Mode: true);
                                LoadSchedule(ScheduleContainer);
                            }
                            else
                            {
                                LoadGrid(Layout1Mode: false);
                                LoadSchedule(ScheduleContainerLayout2);
                            }
                        });
                    }
                    finally
                    {
                        _isUpdating = false;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Graceful exit on cancellation
            }
        }



        private void LoadGrid(bool Layout1Mode)
        {
            var container = Layout1Mode ? GridContainer : GridContainerLayout2;

            if (_cachedStreams == null || _cachedStreams.Count == 0)
            {
                LogToFile("No streams available to display.");
                return;
            }

            container.Content = null;
            var dynamicGrid = new Grid();
            int columns = Math.Max(minColumns, (int)(this.Width / 400)); 
            int rows = (_cachedStreams.Count + columns - 1) / columns;

            for (int i = 0; i < rows; i++)
            {
                dynamicGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            for (int i = 0; i < columns; i++)
            {
                dynamicGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (int i = 0; i < _cachedStreams.Count; i++)
            {
                var stream = _cachedStreams[i];
                var gridItem = CreateStreamCard(stream);
                Grid.SetRow(gridItem, i / columns);
                Grid.SetColumn(gridItem, i % columns);
                dynamicGrid.Children.Add(gridItem);
            }
            container.Content = dynamicGrid;
        }

        private void LoadSchedule(ScrollViewer container)
        {
            if (_cachedStreams == null || _cachedStreams.Count == 0)
            {
                MessageBox.Show("No schedule available.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            container.Content = null;

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top
            };

            var isAlternate = false;

            foreach (var stream in _cachedStreams)
            {
                var innerStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(4,4,4,4)
                };

                var tokyoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                var localTimeZone = TimeZoneInfo.Local;

                var firstLine = new TextBlock
                {
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = stream.LiveStatus == "Live" ? Brushes.Red : Brushes.Black,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                // Handle live or scheduled streams
                if (DateTime.TryParseExact(stream.Start, "MM.dd HH:mm", null, System.Globalization.DateTimeStyles.None, out var parsedStartTime))
                {
                    var tokyoDateTime = TimeZoneInfo.ConvertTimeToUtc(parsedStartTime, tokyoTimeZone);
                    var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(tokyoDateTime, localTimeZone);

                    if (stream.LiveStatus == "Live")
                    {
                        var timer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(1)
                        };

                        timer.Tick += (s, e) =>
                        {
                            var duration = DateTime.UtcNow - tokyoDateTime;
                            firstLine.Text = $"(LIVE) {duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2} - {stream.Name}";
                        };

                        timer.Start();
                        container.Unloaded += (s, e) => timer.Stop();
                    }
                    else
                    {
                        firstLine.Text = $"{localDateTime:MM/dd h:mm tt} - {stream.Name}";
                    }
                }
                else
                {
                    firstLine.Text = "Invalid Time - " + stream.Name;
                }

                var secondLine = new TextBlock
                {
                    Text = stream.Text,
                    FontSize = 14,
                    Foreground = Brushes.Black,
                    TextWrapping = TextWrapping.Wrap
                };

                innerStack.Children.Add(firstLine);
                innerStack.Children.Add(secondLine);

                var background = isAlternate ? Brushes.LightGray : Brushes.WhiteSmoke;
                isAlternate = !isAlternate;

                var border = new Border
                {
                    Background = background,
                    Child = innerStack,
                    Height = 100,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0)
                };
                stackPanel.Children.Add(border);
            }

            container.Content = stackPanel;
        }
        private void OnReturnButtonClicked(object sender, EventArgs e)
        {
            CloseVideoPlayer();
            _isLayout1Active = true;
            Layout1.Visibility = Visibility.Visible;
            Layout2.Visibility = Visibility.Hidden;
        }
        private void LoadVideo(string videoUrl)
        {
            CloseVideoPlayer();
            ClearUI();

            _isLayout1Active = false;
            Layout1.Visibility = Visibility.Hidden;
            Layout2.Visibility = Visibility.Visible;

            VideoPlayer.Source = new Uri("https://cdpn.io/pen/debug/oNPzxKo?v=" +
                System.Text.RegularExpressions.Regex.Match(videoUrl, @"v=([^&]+)").Groups[1].Value + "&autoplay=0&mute=1",
                UriKind.Absolute);
            if (!VideoPlayer.IsInitialized)
            {
                VideoPlayer.EnsureCoreWebView2Async();
            }
            LoadSchedule(ScheduleContainerLayout2);
            LoadGrid(false);
        }


        private void CloseVideoPlayer()
        {
            VideoPlayer.Stop();
            VideoPlayer.Source = new Uri("about:blank");
            ClearUI();
            Layout1.Visibility = Visibility.Visible;
            Layout2.Visibility = Visibility.Hidden;
            LoadSchedule(ScheduleContainer);
            LoadGrid(true);
        }

        private void LogToFile(string message)
        {
            try
            {
                string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                File.AppendAllText(_logFilePath, logMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to log file: {ex.Message}");
            }
        }
        private Border CreateStreamCard(StreamItem stream)
        {

            var backgroundImage = new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri(stream.BackgroundThumbnailUrl, UriKind.Absolute)),
                Stretch = Stretch.UniformToFill,
                Opacity = 0.5
            };

            var grid = new Grid
            {
                Background = backgroundImage,
                Height = 180
            };

            var hoverOverlay = new Border
            {
                Background = Brushes.Transparent,
                Opacity = 0,
            };

            var profileEllipse = new Ellipse
            {
                Width = ThumbnailWidth,
                Height = ThumbnailHeight,
                Fill = new ImageBrush
                {
                    ImageSource = new BitmapImage(new Uri(stream.ProfilePictureUrl, UriKind.Absolute)),
                    Stretch = Stretch.UniformToFill
                },
                Stroke = stream.LiveStatus == "Live" ? Brushes.Red : Brushes.Gray,
                StrokeThickness = 2
            };

            var liveLabel = new Border
            {
                Background = Brushes.Red,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(5, 2, 5, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10),
                Child = new TextBlock
                {
                    Text = "LIVE",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Visibility = stream.LiveStatus == "Live" ? Visibility.Visible : Visibility.Collapsed
            };

            if (stream.LiveStatus == "Live")
            {
                var rotateTransform = new RotateTransform();
                profileEllipse.RenderTransform = rotateTransform;
                profileEllipse.RenderTransformOrigin = new Point(0.5, 0.5);

                var spinAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromSeconds(5),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                var storyboard = new Storyboard();
                storyboard.Children.Add(spinAnimation);
                Storyboard.SetTarget(spinAnimation, profileEllipse);
                Storyboard.SetTargetProperty(spinAnimation, new PropertyPath("RenderTransform.Angle"));
                storyboard.Begin();
            }

            var nameLabel = new TextBlock
            {
                Text = stream.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(10, 0, 10, 5)
            };

            var timeLabel = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Foreground = Brushes.White,
                Margin = new Thickness(10, 0, 10, 5)
            };

            var tokyoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            var localTimeZone = TimeZoneInfo.Local;

            if (DateTime.TryParseExact(stream.Start, "MM.dd HH:mm", null, System.Globalization.DateTimeStyles.None, out var parsedStartTime))
            {
                var tokyoDateTime = TimeZoneInfo.ConvertTimeToUtc(parsedStartTime, tokyoTimeZone);
                var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(tokyoDateTime, localTimeZone);
                timeLabel.Text = $"{localDateTime:h:mm tt}";
            }
            else
            {
                timeLabel.Text = "Invalid Time";
            }

            var titleLabel = new TextBlock
            {
                Text = stream.Text,
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap,
                Height = 50
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var profileInfoStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            profileInfoStack.Children.Add(nameLabel);
            profileInfoStack.Children.Add(timeLabel);
            grid.Children.Add(profileInfoStack);
            grid.Children.Add(liveLabel);
            Grid.SetRow(profileEllipse, 1);
            grid.Children.Add(profileEllipse);
            Grid.SetRow(titleLabel, 2);
            grid.Children.Add(titleLabel);

            var cardBorder = new Border
            {
                Child = grid,
                Height = 180,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Colors.Black)
            };

            cardBorder.MouseLeftButtonDown += (s, e) => LoadVideo(stream.Link);

            cardBorder.MouseEnter += (sender, e) =>
            {
                if (backgroundImage != null)
                {
                    var fadeInAnimation = new DoubleAnimation
                    {
                        To = 0.2,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    backgroundImage.BeginAnimation(ImageBrush.OpacityProperty, fadeInAnimation);
                }
            };

            cardBorder.MouseLeave += (sender, e) =>
            {
                if (backgroundImage != null)
                {
                    var fadeInAnimation = new DoubleAnimation
                    {
                        To = 0.5,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    backgroundImage.BeginAnimation(ImageBrush.OpacityProperty, fadeInAnimation);
                }
            };

            return cardBorder;
        }

        private void ClearUI()
        {
            ScheduleContainer.Content = null;
            GridContainer.Content = null;
            GridContainerLayout2.Content = null;
            ScheduleContainerLayout2.Content = null;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _timerCancellationTokenSource?.Cancel();
            _timerCancellationTokenSource?.Dispose();
            base.OnClosing(e);
        }
    }
}