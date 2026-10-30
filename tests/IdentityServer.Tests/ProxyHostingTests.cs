using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using static IdentityServer.Tests.IdentityProviderTests;

namespace IdentityServer.Tests;

public sealed class ProxyHostingTests
{
    internal const string Secret = "test-only-proxy-secret-0123456789-abcdefghijklmnopqrstuvwxyz";

    [Theory]
    [InlineData("/IdentityServer", "Preserve")]
    [InlineData("/IdentityServer", "Strip")]
    [InlineData("", "Preserve")]
    public async Task DiscoveryAndJwksUseThePublicSchemeHostAndPrefix(string pathBase, string pathMode)
    {
        using var host = new ProxyHost(pathBase, pathMode);
        using var client = host.CreateClient();
        string upstreamPrefix = pathMode == "Strip" ? "" : pathBase;
        using var request = Request(upstreamPrefix + "/.well-known/openid-configuration");
        request.Headers.Add("X-Forwarded-Prefix", "/attacker");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
        var discovery = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("https://public.example" + (pathBase.Length == 0 ? "/" : pathBase), discovery.GetProperty("issuer").GetString());
        Assert.Equal("https://public.example" + pathBase + "/connect/authorize", discovery.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://public.example" + pathBase + "/connect/token", discovery.GetProperty("token_endpoint").GetString());
        var jwksUri = new Uri(discovery.GetProperty("jwks_uri").GetString()!);
        Assert.StartsWith(pathBase + "/", jwksUri.AbsolutePath);
        using var jwksRequest = Request(pathMode == "Strip" ? jwksUri.PathAndQuery[pathBase.Length..] : jwksUri.PathAndQuery);
        using var jwks = await client.SendAsync(jwksRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, jwks.StatusCode);
    }

    [Theory]
    [InlineData("/connect/authorize")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/IdentityServerOther/.well-known/openid-configuration")]
    public async Task PreservedPrefixDoesNotExposeDuplicateRootEndpoints(string path)
    {
        using var host = new ProxyHost();
        using var client = host.CreateClient();
        using var request = Request(path);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Theory]
    [InlineData("Preserve")]
    [InlineData("Strip")]
    public async Task ProxyPrincipalCompletesCodeFlow_AndTokenEndpointRemainsAnonymous(string mode)
    {
        using var host = new ProxyHost(pathMode: mode);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        string prefix = mode == "Strip" ? "" : "/IdentityServer";
        string authorize = QueryHelpers.AddQueryString(prefix + "/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = "proxy-test", ["redirect_uri"] = "https://app.example/callback", ["response_type"] = "code",
            ["response_mode"] = "query", ["scope"] = "openid profile", ["nonce"] = "proxy-test"
        });
        using var request = Request(authorize, "alice@EXAMPLE.COM");
        using var authorization = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        var query = QueryHelpers.ParseQuery(authorization.Headers.Location!.Query);
        Assert.False(query.ContainsKey("error"), authorization.Headers.Location.ToString());
        using var tokenRequest = Request(prefix + "/connect/token");
        tokenRequest.Method = HttpMethod.Post;
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "proxy-test", ["code"] = query["code"].ToString(),
            ["redirect_uri"] = "https://app.example/callback"
        });
        using var token = await client.SendAsync(tokenRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        Assert.Empty(token.Headers.WwwAuthenticate);
        var body = await token.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var jwt = new JsonWebToken(body.GetProperty("id_token").GetString()!);
        Assert.Equal("test-subject-alice@EXAMPLE.COM", jwt.Subject);
        Assert.Equal("https://public.example/IdentityServer", jwt.Issuer);
        Assert.Equal(["alice@EXAMPLE.COM"], host.Profiles.Users);
    }

    [Theory]
    [InlineData(null, Secret)]
    [InlineData("alice@EXAMPLE.COM", null)]
    [InlineData("alice@EXAMPLE.COM", "wrong-secret")]
    [InlineData("alice", Secret)]
    [InlineData("alice@EXAMPLE.COM,bob@EXAMPLE.COM", Secret)]
    [InlineData("HTTP/host@EXAMPLE.COM", Secret)]
    public async Task MissingOrInvalidProxyAttestationCannotAuthenticate(string? user, string? secret)
    {
        using var host = new ProxyHost();
        using var client = host.CreateClient();
        using var request = Request("/IdentityServer/test/protected", user, secret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Empty(host.Profiles.Users);
    }

    [Theory]
    [InlineData(ProxyKerberosOptions.UserHeader)]
    [InlineData(ProxyKerberosOptions.SecretHeader)]
    public async Task DuplicateAuthenticationHeadersAreRejected(string header)
    {
        using var host = new ProxyHost();
        using var client = host.CreateClient();
        using var request = Request("/IdentityServer/test/protected", "alice@EXAMPLE.COM");
        request.Headers.TryAddWithoutValidation(header, header == ProxyKerberosOptions.UserHeader ? "bob@EXAMPLE.COM" : Secret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SpoofedForwardedForDoesNotMakeAnUnknownPeerTrusted()
    {
        using var host = new ProxyHost(peer: "192.0.2.80");
        using var client = host.CreateClient();
        using var request = Request("/IdentityServer/test/protected", "alice@EXAMPLE.COM");
        request.Headers.Remove("X-Forwarded-For");
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var discoveryRequest = Request("/IdentityServer/.well-known/openid-configuration");
        using var discovery = await client.SendAsync(discoveryRequest, TestContext.Current.CancellationToken);
        var body = await discovery.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("http://localhost/IdentityServer", body.GetProperty("issuer").GetString());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Forbidden)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task OffHostProxyRequiresActualBackendTls_NotJustForwardedProto(bool tls, HttpStatusCode expected)
    {
        using var host = new ProxyHost(peer: "10.0.0.5", knownProxy: "10.0.0.5", backendTls: tls);
        using var client = host.CreateClient(new() { BaseAddress = new Uri(tls ? "https://localhost" : "http://localhost") });
        using var request = Request("/IdentityServer/test/protected", "alice@EXAMPLE.COM");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task HttpsSchemeWithoutATlsConnectionDoesNotProveBackendEncryption()
    {
        using var host = new ProxyHost(peer: "10.0.0.5", knownProxy: "10.0.0.5");
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = Request("/IdentityServer/test/protected", "alice@EXAMPLE.COM");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProxyAuthenticationIsPerRequest_AndDoesNotLeakAcrossPooledConnections()
    {
        using var host = new ProxyHost();
        using var client = host.CreateClient();
        foreach (string? user in new string?[] { "alice@EXAMPLE.COM", null, "bob@EXAMPLE.COM", null })
        {
            using var request = Request("/IdentityServer/test/proxy-identity", user);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(user is null ? HttpStatusCode.Forbidden : HttpStatusCode.OK, response.StatusCode);
            if (user is not null)
                Assert.Equal(user, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task NativeKerberosModeIgnoresProxyIdentityHeaders()
    {
        using var host = new ProxyHost(authentication: "Kerberos");
        using var client = host.CreateClient();
        using var request = Request("/IdentityServer/test/protected", "alice@EXAMPLE.COM");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Negotiate", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task StrippedPrefixRejectsUnknownProxyEvenForAnonymousRequests()
    {
        using var host = new ProxyHost(pathMode: "Strip", peer: "192.0.2.80");
        using var client = host.CreateClient();
        using var request = Request("/.well-known/openid-configuration");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ForwardedHostIsRestrictedToConfiguredPublicHosts()
    {
        using var host = new ProxyHost();
        using var client = host.CreateClient();
        using var request = Request("/IdentityServer/.well-known/openid-configuration");
        request.Headers.Remove("X-Forwarded-Host");
        request.Headers.Add("X-Forwarded-Host", "attacker.example");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.DoesNotContain("attacker.example", body.GetProperty("issuer").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/IdentityServer")]
    public async Task IisProvidedPathBaseIsPreservedWithoutDoubling(string configuredPath)
    {
        var options = ProxyHostingOptions.Read(Configuration(("IdentityServer:PathBase", configuredPath)));
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/IdentityServer";
        context.Request.Path = "/connect/authorize";
        bool called = false;
        var middleware = new PathBaseMiddleware(http =>
        {
            called = true;
            Assert.Equal("/IdentityServer", http.Request.PathBase.Value);
            Assert.Equal("/connect/authorize", http.Request.Path.Value);
            return Task.CompletedTask;
        }, options);
        await middleware.InvokeAsync(context);
        Assert.True(called);
    }

    private static HttpRequestMessage Request(string path, string? user = null, string? secret = Secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "public.example");
        if (user is not null) request.Headers.Add(ProxyKerberosOptions.UserHeader, user);
        if (secret is not null) request.Headers.Add(ProxyKerberosOptions.SecretHeader, secret);
        return request;
    }

    private sealed class ProxyHost(string pathBase = "/IdentityServer", string pathMode = "Preserve", string peer = "127.0.0.1",
        string knownProxy = "127.0.0.1", string authentication = "ProxyKerberos", bool backendTls = false) : WebApplicationFactory<Program>
    {
        internal Profiles Profiles { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddConfiguration(LdapConfiguration())
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IdentityServer:ServerUri"] = "*", ["IdentityServer:PersistKeys"] = "false",
                    ["IdentityServer:Hosts:0"] = "https://app.example", ["IdentityServer:Clients:0:ClientId"] = "proxy-test",
                    ["IdentityServer:Authentication:Mode"] = authentication, ["IdentityServer:Authentication:ProxySecret"] = Secret,
                    ["IdentityServer:UseForwardedHeaders"] = "true", ["IdentityServer:ReverseProxy:KnownProxies:0"] = knownProxy,
                    ["IdentityServer:ReverseProxy:AllowedHosts:0"] = "public.example",
                    ["IdentityServer:PathBase"] = pathBase, ["IdentityServer:PathBaseMode"] = pathMode
                }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IUserProfileProvider>(Profiles));
                services.AddSingleton<IStartupFilter>(new PeerFilter(IPAddress.Parse(peer), backendTls));
                services.AddControllers().AddApplicationPart(typeof(SignInTestController).Assembly);
            });
        }
    }

    private sealed class PeerFilter(IPAddress peer, bool tls) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = peer;
                // TestServer has no transport layer. Supply the server feature separately from
                // the URL scheme, as Kestrel does only after a successful TLS handshake.
                if (tls) context.Features.Set<ITlsConnectionFeature>(new TlsConnectionFeature());
                context.Features.Set<IConnectionItemsFeature>(new ConnectionItemsFeature { Items = new ConnectionItems() });
                return continuation(context);
            });
            next(app);
        };
    }

    private sealed class ConnectionItemsFeature : IConnectionItemsFeature
    {
        public IDictionary<object, object?> Items { get; set; } = new ConnectionItems();
    }

    private sealed class Profiles : IUserProfileProvider
    {
        internal List<string> Users { get; } = [];
        public UserProfile GetProfile(ClaimsPrincipal principal, bool includeGroups)
        {
            Assert.Equal(ProxyKerberosOptions.Scheme, principal.Identity!.AuthenticationType);
            string name = principal.Identity.Name!;
            Users.Add(name);
            return new() { Subject = "test-subject-" + name, Username = name.Split('@')[0], AccountName = name, DisplayName = name };
        }
    }
}

[ApiController]
public sealed class ProxyIdentityTestController : ControllerBase
{
    [HttpGet("/test/proxy-identity")]
    public string Identity() => User.Identity!.Name!;
}
