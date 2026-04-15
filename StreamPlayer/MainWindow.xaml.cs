using StreamPlayer.Models;
using StreamPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StreamPlayer;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            VideoPlayer.MediaPlayer = viewModel.MediaPlayer;

            UrlBox.MouseEnter += (_, _) => { UrlBox.Focus(); UrlBox.SelectAll(); };

            HistoryList.SelectionChanged += (_, _) =>
            {
                if (HistoryList.SelectedItem is HistoryEntry entry)
                {
                    viewModel.SelectHistoryEntry(entry);
                    HistoryList.SelectedItem = null;
                }
            };

            SeekSlider.AddHandler(Thumb.DragStartedEvent,
                new DragStartedEventHandler((_, _) => viewModel.OnSeekStarted()));
            SeekSlider.AddHandler(Thumb.DragCompletedEvent,
                new DragCompletedEventHandler((_, _) => viewModel.OnSeekCompleted(SeekSlider.Value)));

            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.VideoInfo))
                    ApplyVideoInfo(viewModel.VideoInfo);
            };
        };
    }

    private void ApplyVideoInfo(VideoInfo? info)
    {
        if (info is null)
        {
            ThumbnailImage.Source = null;
            SeekSlider.Ticks = null;
            SeekSlider.TickPlacement = TickPlacement.None;
            return;
        }

        // Thumbnail
        if (!string.IsNullOrEmpty(info.ThumbnailUrl))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(info.ThumbnailUrl);
                bmp.DecodePixelWidth = 240;   // 2× display width — sharp without being huge
                bmp.EndInit();
                ThumbnailImage.Source = bmp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Thumbnail] Failed to load: {ex.Message}");
            }
        }

        // Chapter tick marks on the seek slider
        if (info.Chapters.Count > 0 && info.DurationMs > 0)
        {
            SeekSlider.Ticks = new DoubleCollection(
                info.Chapters.Select(c => (double)c.StartMs / info.DurationMs));
            SeekSlider.TickPlacement = TickPlacement.BottomRight;
        }
    }
}
