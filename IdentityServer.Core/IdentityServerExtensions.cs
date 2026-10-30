using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace IdentityServer;

/// <summary>Registers and hosts the reusable Windows-authenticated OpenID Connect server.</summary>
public static class IdentityServerExtensions
{
    /// <summary>Adds authentication, authorization and OpenIddict. The host registers its own controllers.</summary>
    public static IServiceCollection AddIdentityServer(this IServiceCollection services, IConfiguration configuration)
    {
        // Negotiate proves who the Windows user is; OpenIddict turns that identity into tokens
        // applications can use. The fallback policy also protects newly added application routes
        // unless they explicitly opt into anonymous access.
        services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate().AddCookie();
        services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy);
        services.AddCors();
        IdentityServer.Add(services, configuration);
        services.AddSingleton<Authentication>();
        services.AddHttpContextAccessor();
        return services;
    }

    /// <summary>
    /// Adds routing, CORS, authentication and authorization. Call UseEndpoints afterwards to map
    /// the consuming application's controllers or other endpoints.
    /// </summary>
    public static IApplicationBuilder UseIdentityServer(this IApplicationBuilder app, IWebHostEnvironment env)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("IdentityServer.Startup");
        bool persistKeys = configuration.GetValue<bool>("IdentityServer:PersistKeys");
        string serverUri = configuration.GetValue<string>("IdentityServer:ServerUri") ?? "*";
        string[] hosts = configuration.GetSection("IdentityServer:Hosts").Get<string[]>() ?? [];

        if (!persistKeys)
            logger.LogWarning("IdentityServer:PersistKeys is false: signing and encryption keys are regenerated at startup. Previously issued tokens may fail validation and outstanding authorization codes cannot be redeemed after a restart. Set PersistKeys to true to reuse keys on this machine.");
        else
            logger.LogInformation("IdentityServer:PersistKeys is true: signing and encryption keys are stored on disk using Windows machine protection. Preserve the key files across deployments; copying them to another machine does not make them usable there.");

        if (serverUri == "*" || string.IsNullOrEmpty(serverUri))
            logger.LogInformation("IdentityServer:ServerUri is automatic: the request URL determines the issuer (the server address clients trust). If clients use a different public address, configure ServerUri with that address, including any application path.");
        else
            logger.LogInformation("IdentityServer:ServerUri is {ServerUri}; clients must use this issuer when validating tokens.", serverUri);

        if (hosts.Length == 0)
            logger.LogWarning("IdentityServer:Hosts is empty: all authorization redirects will be rejected. Add the trusted application callback origins so the server knows where sign-in results may be sent.");
        else
            logger.LogInformation("IdentityServer:Hosts is {Hosts}. Redirect checks compare host names only; browser cross-origin requests (CORS) use the configured scheme, host and port.", string.Join(", ", hosts));

        if (!configuration.GetSection("IdentityServer:Groups").GetChildren().Any())
            logger.LogInformation("IdentityServer:Groups is empty: no Active Directory groups will become role claims, even when a client requests the roles scope. Configure group-name patterns if applications need roles for access decisions.");

        if (env.IsDevelopment())
            app.UseDeveloperExceptionPage();

        if (configuration.GetValue<bool>("IdentityServer:UseForwardedHeaders"))
        {
            // This must precede authentication so issuer generation sees the public URL.
            // Enabling the middleware alone does not configure which proxies or headers to trust.
            app.UseForwardedHeaders();
            logger.LogInformation("Forwarded-header middleware is enabled. Configure trusted proxies and forwarded headers in the host so issuer detection sees the public scheme and host.");
        }

        // Order matters: routing supplies endpoint metadata, CORS handles browser preflight,
        // and OpenIddict handles discovery/token requests inside UseAuthentication. Those protocol
        // endpoints must stay reachable without a Windows challenge. Our middleware then protects
        // application routes, and UseAuthorization enforces their access policies.
        app.UseRouting();
        app.UseCors(builder => builder.WithOrigins(hosts).AllowAnyMethod().AllowAnyHeader());
        app.UseAuthentication();
        app.UseMiddleware<Authentication>();
        app.UseAuthorization();
        return app;
    }
}
