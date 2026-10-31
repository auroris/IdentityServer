using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace IdentityServer;

internal enum PathBaseMode { Preserve, Strip }

/// <summary>Explicit, single-hop proxy trust. No settings preserves IIS's built-in integration.</summary>
internal sealed class ProxyHostingOptions
{
    internal bool UseForwardedHeaders { get; private init; }
    internal bool HasProxyConfiguration { get; private init; }
    internal IPAddress[] KnownProxies { get; private init; } = [];
    internal string[] AllowedHosts { get; private init; } = [];
    internal PathString PathBase { get; private init; }
    internal PathBaseMode PathBaseMode { get; private init; }

    internal bool IsTrusted(IPAddress? address) => address is not null &&
        KnownProxies.Any(proxy => Normalize(proxy).Equals(Normalize(address)));

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    internal static ProxyHostingOptions Read(IConfiguration configuration)
    {
        bool forwarded = configuration.GetValue<bool>("IdentityServer:UseForwardedHeaders");
        var section = configuration.GetSection("IdentityServer:ReverseProxy");
        var addresses = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var proxies = addresses.Select(value => IPAddress.TryParse(value, out var address) &&
            !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any) ? address :
            throw new InvalidOperationException("IdentityServer:ReverseProxy:KnownProxies must contain explicit proxy IP addresses, not hostnames, CIDRs or wildcard addresses.")).ToArray();
        string[] hosts = section.GetSection("AllowedHosts").Get<string[]>() ?? [];
        string? issuer = configuration["IdentityServer:ServerUri"];
        Uri? issuerUri = Uri.TryCreate(issuer, UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http" ? parsed : null;
        if (section.Exists())
        {
            if (!forwarded || proxies.Length == 0)
                throw new InvalidOperationException("ReverseProxy requires IdentityServer:UseForwardedHeaders=true and at least one ReverseProxy:KnownProxies IP address.");
            if (hosts.Length == 0 && issuerUri is not null) hosts = [issuerUri.IdnHost];
            if (hosts.Length == 0 || hosts.Any(host => Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains('*')))
                throw new InvalidOperationException("Configure ReverseProxy:AllowedHosts with exact public hostnames (without ports), or set an absolute IdentityServer:ServerUri.");
        }

        string path = configuration["IdentityServer:PathBase"] ?? "";
        if (path == "/") path = "";
        else if (path.EndsWith('/') && !path.EndsWith("//", StringComparison.Ordinal)) path = path[..^1];
        if (path.Length > 0 && (!path.StartsWith('/') || path.Contains("//") ||
            path.Split('/').Any(part => part is "." or "..") ||
            path.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or '~'))))
            throw new InvalidOperationException("IdentityServer:PathBase must be an absolute URL path such as /IdentityServer, without encoded characters, empty segments, dot segments, query or fragment.");
        string mode = configuration["IdentityServer:PathBaseMode"] ?? "Preserve";
        if (!Enum.GetNames<PathBaseMode>().Any(name => name.Equals(mode, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("IdentityServer:PathBaseMode must be Preserve or Strip.");
        var pathMode = Enum.Parse<PathBaseMode>(mode, ignoreCase: true);
        if (pathMode == PathBaseMode.Strip && (path.Length == 0 || proxies.Length == 0 || !forwarded))
            throw new InvalidOperationException("PathBaseMode Strip requires a nonempty PathBase and explicit trusted ReverseProxy settings.");
        if (path.Length > 0 && issuerUri is not null && !issuerUri.AbsolutePath.TrimEnd('/').Equals(path, StringComparison.Ordinal))
            throw new InvalidOperationException("IdentityServer:ServerUri must include the same path as IdentityServer:PathBase.");

        return new()
        {
            UseForwardedHeaders = forwarded, HasProxyConfiguration = section.Exists(), KnownProxies = proxies,
            AllowedHosts = hosts, PathBase = new(path), PathBaseMode = pathMode
        };
    }

    internal void Configure(ForwardedHeadersOptions options)
    {
        // Use host-supplied/IIS options when no ReverseProxy section is configured.
        if (!HasProxyConfiguration) return;
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        options.RequireHeaderSymmetry = true;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var address in KnownProxies) options.KnownProxies.Add(address);
        options.AllowedHosts.Clear();
        foreach (var host in AllowedHosts) options.AllowedHosts.Add(host);
        // X-Forwarded-Prefix is deliberately not enabled. PathBase comes from deployment config.
    }
}

internal sealed record ProxyConnectionFeature(bool Trusted, bool ProtectedTransport);

/// <summary>Capture the TCP peer before forwarded headers replace RemoteIpAddress or Scheme.</summary>
internal sealed class ProxyConnectionMiddleware(RequestDelegate next, ProxyHostingOptions options)
{
    public Task InvokeAsync(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        context.Features.Set(new ProxyConnectionFeature(options.IsTrusted(address),
            (context.Request.IsHttps && context.Features.Get<ITlsConnectionFeature>() is not null) ||
            (address is not null && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address))));
        return next(context);
    }
}

internal sealed class PathBaseMiddleware(RequestDelegate next, ProxyHostingOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.PathBase.HasValue) { await next(context); return; }
        var originalBase = context.Request.PathBase;
        var originalPath = context.Request.Path;
        if (originalBase.HasValue)
        {
            // IIS (or an embedding host) may already have separated the application path.
            if (!originalBase.Equals(options.PathBase)) { context.Response.StatusCode = 404; return; }
            await next(context);
            return;
        }
        if (options.PathBaseMode == PathBaseMode.Strip)
        {
            if (context.Features.Get<ProxyConnectionFeature>()?.Trusted != true)
            { context.Response.StatusCode = 403; return; }
        }
        else
        {
            if (!originalPath.StartsWithSegments(options.PathBase, StringComparison.Ordinal, out var remainder))
            { context.Response.StatusCode = 404; return; }
            context.Request.Path = remainder;
        }
        context.Request.PathBase = options.PathBase;
        try { await next(context); }
        finally { context.Request.PathBase = originalBase; context.Request.Path = originalPath; }
    }
}
