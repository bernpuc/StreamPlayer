using StreamPlayer.Models;
using StreamPlayer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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
            UrlBox.ContextMenuOpening += (_, _) => UrlBox.SelectAll();

            SeekSlider.AddHandler(Thumb.DragStartedEvent,
                new DragStartedEventHandler((_, _) => viewModel.OnSeekStarted()));
            SeekSlider.AddHandler(Thumb.DragCompletedEvent,
                new DragCompletedEventHandler((_, _) => viewModel.OnSeekCompleted(SeekSlider.Value)));

            PlaylistList.SelectionChanged += (_, _) =>
            {
                if (PlaylistList.SelectedItem is PlaylistEntry entry)
                    viewModel.SelectPlaylistEntry(entry);
            };

            viewModel.PropertyChanged += (_, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(MainWindowViewModel.VideoInfo):
                        ApplyVideoInfo(viewModel.VideoInfo);
                        break;
                    case nameof(MainWindowViewModel.CurrentPlaylistIndex):
                        SyncPlaylistSelection(viewModel.CurrentPlaylistIndex);
                        break;
                    case nameof(MainWindowViewModel.IsPlaylistOpen):
                        if (viewModel.IsPlaylistOpen)
                            SyncPlaylistSelection(viewModel.CurrentPlaylistIndex);
                        break;
                }
            };
        };
    }

    private void SyncPlaylistSelection(int idx)
    {
        PlaylistList.SelectedIndex = idx;
        if (idx >= 0 && idx < PlaylistList.Items.Count)
            PlaylistList.ScrollIntoView(PlaylistList.Items[idx]);
    }

    private void ApplyVideoInfo(VideoInfo? info)
    {
        if (info is null)
        {
            SeekSlider.Ticks = null;
            SeekSlider.TickPlacement = TickPlacement.None;
            return;
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
