using Holodex.NET.DataTypes;
using Holodex.NET.Enums;
using HoloStream_Live.Services;
using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Diagnostics;
using System;
using Holodex.NET;

namespace HoloStream_Live
{
    public partial class MainWindow : Window
    {
        private const int ThumbnailWidth = 50;
        private const int ThumbnailHeight = 50;
        private const int minColumns = 2;
        private List<StreamItem> _cachedStreams = new();
        private readonly string _logFilePath;
        private CancellationTokenSource _timerCancellationTokenSource;
        private bool _isLayout1Active;
        private bool _isUpdating;
        private bool _isAlternate;

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
            StartGridReloadTimer();
        }
        private async Task InitializeStreamsAsync()
        {
            bool isLoading = true;

            _ = Task.Run(async () =>
            {
                while (isLoading)
                {
                    for (int i = 0; i <= 100; i += 10)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LoadingBar.Value = i;
                            LoadingStatusText.Text = $"Loading... {i}%";
                        });
                        await Task.Delay(100);
                    }
                }
            });

            try
            {
                Dispatcher.Invoke(() =>
                {
                    CenteredLoadingUI.Visibility = Visibility.Visible;
                    LoadingBar.Value = 0;
                    LoadingStatusText.Text = "Starting...";
                });

                await FetchStreamsAsync();

                isLoading = false;

                Dispatcher.Invoke(() =>
                {
                    CenteredLoadingUI.Visibility = Visibility.Collapsed;
                });

                LoadGrid(Layout1Mode: true);
                LoadSchedule(Layout1Mode: true);
            }
            catch (Exception ex)
            {
                isLoading = false;
                Dispatcher.Invoke(() =>
                {
                    LoadingStatusText.Text = $"Error: {ex.Message}";
                    LoadingBar.Value = 0;
                });
            }
        }

        private async Task FetchStreamsAsync()
        {
            try
            {
                ScheduleService service = new ScheduleService("");


                List<StreamItem> ps = await service.GetScheduleAsync(organization: "hololive");

                foreach (var item in ps)
                {
                    Debug.WriteLine($"Name: {item.Name}");
                    Debug.WriteLine($"Link: {item.Link}");
                    Debug.WriteLine($"Start Time: {item.Start}");
                    Debug.WriteLine($"Live Status: {item.LiveStatus}");
                }


                var scraper = new ScheduleScraper();
                var url = "https://hololive.hololivepro.com/en/schedule/";
                _cachedStreams = await service.GetScheduleAsync(scheduleUrl: url);
                LogToFile($"Fetched {_cachedStreams.Count} streams.");
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

                VideoPlayer.Width = this.ActualWidth;
                VideoPlayer.Height = this.ActualHeight;
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

                VideoPlayer.Width = double.NaN;
                VideoPlayer.Height = double.NaN;
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

                    if (_isUpdating) continue;
                    _isUpdating = true;

                    try
                    {
                        await FetchStreamsAsync();

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_isLayout1Active)
                            {
                                LoadGrid(Layout1Mode: true);
                                LoadSchedule(Layout1Mode: true);
                            }
                            else
                            {
                                LoadGrid(Layout1Mode: false);
                                LoadSchedule(Layout1Mode: false);
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
                dynamicGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
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

        private void LoadSchedule(bool Layout1Mode)
        {
            var container = Layout1Mode ? ScheduleContainer : ScheduleContainerLayout2;

            if (_cachedStreams == null || _cachedStreams.Count == 0)
            {
                MessageBox.Show("No schedule available.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var stackPanel = container.Content as StackPanel;

            if (stackPanel == null)
            {
                stackPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Top
                };
                container.Content = stackPanel;
            }

            var existingItems = stackPanel.Children.Cast<UIElement>().ToList();

            for (int i = 0; i < _cachedStreams.Count; i++)
            {
                var stream = _cachedStreams[i];
                var existingItem = existingItems
                    .FirstOrDefault(item => ((Border)item).Tag is StreamItem existingStream && existingStream.Name == stream.Name);

                if (existingItem == null)
                {
                    var newScheduleItem = CreateScheduleItem(stream);
                    newScheduleItem.Tag = stream;
                    stackPanel.Children.Add(newScheduleItem);
                }
                else
                {
                    var scheduleBorder = existingItem as Border;
                    if (scheduleBorder != null && !stream.Equals((StreamItem)scheduleBorder.Tag))
                    {
                        stackPanel.Children.Remove(existingItem);
                        var updatedScheduleItem = CreateScheduleItem(stream);
                        updatedScheduleItem.Tag = stream;
                        stackPanel.Children.Insert(i, updatedScheduleItem);
                    }
                }
            }

            foreach (var existingItem in existingItems)
            {
                if (!(_cachedStreams.Any(stream => stream.Name == ((StreamItem)((Border)existingItem).Tag)?.Name)))
                {
                    stackPanel.Children.Remove(existingItem);
                }
            }
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
            LoadSchedule(false);
            LoadGrid(false);
        }


        private void CloseVideoPlayer()
        {
            VideoPlayer.Stop();
            VideoPlayer.Source = new Uri("about:blank");
            ClearUI();
            Layout1.Visibility = Visibility.Visible;
            Layout2.Visibility = Visibility.Hidden;
            LoadSchedule(true);
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
        private Border CreateScheduleItem(StreamItem stream)
        {
            var innerStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                MinHeight = 100,
                Background = _isAlternate ? Brushes.DarkGray : Brushes.Gray
            };

            var firstLine = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = stream.LiveStatus == "Live" ? Brushes.Red : Brushes.Black,
                Text = stream.Name
            };

            var tokyoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            var localTimeZone = TimeZoneInfo.Local;

            if (DateTime.TryParseExact(stream.Start, "MM.dd HH:mm", null, System.Globalization.DateTimeStyles.None, out var parsedStartTime))
            {
                var tokyoDateTime = TimeZoneInfo.ConvertTimeToUtc(parsedStartTime, tokyoTimeZone);
                var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(tokyoDateTime, localTimeZone);
                firstLine.Text = $"{localDateTime:MM/dd h:mm tt} - {stream.Name}";
            }
            else
            {
                firstLine.Text = "Invalid Time";
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

            _isAlternate = !_isAlternate;

            return new Border
            {
                Child = innerStack
            };
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