using Microsoft.Extensions.Hosting;

namespace IdentityServer;

/// <summary>Standalone host for the base identity server.</summary>
public class Program
{
    public static void Main(string[] args) => IdentityServerHost.Run<Startup>(args);

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        IdentityServerHost.CreateHostBuilder<Startup>(args);
}
