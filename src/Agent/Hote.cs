using Microsoft.Extensions.Logging.EventLog;

namespace CryptoCrypt.Agent;

/// <summary>Hote de l agent : service Windows ou unite systemd selon le systeme.</summary>
internal static class Hote
{
    public static async Task<int> ExecuterAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Services.AddWindowsService(o => o.ServiceName = Produit.NomService);
        builder.Services.AddSystemd();
        if (OperatingSystem.IsWindows())
        {
            builder.Services.Configure<EventLogSettings>(r => { if (OperatingSystem.IsWindows()) { r.SourceName = Produit.NomService; } });
        }
        builder.Services.AddHostedService<Boucle>();

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return Environment.ExitCode;
    }
}
