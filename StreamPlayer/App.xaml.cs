using Prism.DryIoc;
using Prism.Ioc;
using StreamPlayer.Services;
using StreamPlayer.Services.Interfaces;
using System.Windows;
using System.Windows.Controls;

namespace StreamPlayer;

public partial class App : PrismApplication
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(5000));
        base.OnStartup(e);
    }

    protected override Window CreateShell() => Container.Resolve<MainWindow>();

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<IPlayerService, PlayerService>();
        containerRegistry.RegisterSingleton<IHistoryService, HistoryService>();
        containerRegistry.RegisterSingleton<IAcrCloudService, AcrCloudService>();
    }
}
