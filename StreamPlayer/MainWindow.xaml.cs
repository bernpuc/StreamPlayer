using StreamPlayer.ViewModels;
using System.Windows;

namespace StreamPlayer;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => VideoPlayer.MediaPlayer = viewModel.MediaPlayer;
    }
}
