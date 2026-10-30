using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace IdentityServer;

/// <summary>Shared host configuration for the standalone server and derived applications.</summary>
public static class IdentityServerHost
{
    /// <summary>Runs a host with the application's own startup class.</summary>
    public static void Run<TStartup>(string[] args) where TStartup : class
    {
        // Configuration or key loading can fail before the host's full logger exists. Keep a
        // console logger available so an early startup failure still has an explanation.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            CreateHostBuilder<TStartup>(args).Build().Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly. Inspect the exception for the cause; if startup failed, check configuration values and the process account's access to configured files and directories.");
            throw;
        }
        finally
        {
            // Flush buffered events so the last error is not lost when the process exits.
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Loads the consuming application's settings, optional local overrides, environment variables
    /// and command-line arguments, then configures logging and its startup class.
    /// </summary>
    public static IHostBuilder CreateHostBuilder<TStartup>(string[] args) where TStartup : class =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog((context, loggerConfig) => loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File("logs/identityserver-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
            .ConfigureAppConfiguration((_, config) =>
            {
                // Keep standalone and embedded hosts on the same precedence rules. Later sources
                // win, so environment variables and launch arguments can override local settings.
                // Clearing defaults deliberately excludes appsettings.{Environment}.json/user secrets.
                config.Sources.Clear();
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<TStartup>());
}
