using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Services;
using DesktopAgent.Desktop.Services;
using DesktopAgent.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DesktopAgent.Desktop;

internal static class DesktopHost
{
    public static IHost Build()
    {
        // No default configuration, user secrets, existing JSON repositories or native workers.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Services.AddSingleton<ICharacterService, CharacterService>();
        builder.Services.AddSingleton<ICredentialStore, UnavailableCredentialStore>();
        builder.Services.AddSingleton<ShellViewModel>();
        return builder.Build();
    }
}
