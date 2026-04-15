using Prism.DryIoc;
using Prism.Ioc;
using StreamPlayer.Services;
using StreamPlayer.Services.Interfaces;
using System.Windows;

namespace StreamPlayer;

public partial class App : PrismApplication
{
    protected override Window CreateShell() => Container.Resolve<MainWindow>();

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<IPlayerService, PlayerService>();
    }
}
