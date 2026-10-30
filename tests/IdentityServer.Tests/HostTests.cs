using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityServer.Tests;

/// <summary>Exercises the standalone host and the configuration boundary used by derived hosts.</summary>
public sealed class HostTests
{
    [Fact]
    public async Task Discovery_IsAnonymousAndUsesTheHostsIssuer()
    {
        using var host = new IdentityServerFactory("https://base.example/identity", "base-client");
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
        JsonElement discovery = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("https://base.example/identity", discovery.GetProperty("issuer").GetString());
        Assert.Equal("/connect/authorize", new Uri(discovery.GetProperty("authorization_endpoint").GetString()!).AbsolutePath);
        Assert.Equal("/connect/token", new Uri(discovery.GetProperty("token_endpoint").GetString()!).AbsolutePath);
    }

    [Theory]
    [InlineData("/missing-endpoint")]
    [InlineData("/favicon.ico")]
    public async Task UnknownPaths_Return404WithoutAChallenge(string path)
    {
        using var host = new IdentityServerFactory("https://base.example/", "base-client");
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task IndependentHosts_KeepTheirOwnClientsAndRedirectHosts()
    {
        using var first = new IdentityServerFactory("https://first.example/", "first-client");
        using var second = new IdentityServerFactory("https://second.example/", "second-client");
        using HttpClient firstClient = first.CreateClient();
        using HttpClient secondClient = second.CreateClient();

        // Check the first host again after the second has started. A process-global configuration
        // would reject its own client or start accepting the second host's client and redirects.
        await AssertAuthorization(firstClient, "first-client", "first.example", HttpStatusCode.Unauthorized);
        await AssertAuthorization(secondClient, "second-client", "second.example", HttpStatusCode.Unauthorized);
        await AssertAuthorization(firstClient, "second-client", "first.example", HttpStatusCode.BadRequest);
        await AssertAuthorization(firstClient, "first-client", "second.example", HttpStatusCode.BadRequest);
        await AssertAuthorization(firstClient, "first-client", "first.example", HttpStatusCode.Unauthorized);
    }

    private static async Task AssertAuthorization(HttpClient client, string clientId, string redirectHost, HttpStatusCode expected)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"/connect/authorize?client_id={clientId}&redirect_uri=https%3A%2F%2F{redirectHost}%2Fcallback" +
            "&scope=openid&response_type=id_token&response_mode=form_post&nonce=test-nonce", TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Unauthorized)
            Assert.Equal(["Negotiate"], response.Headers.WwwAuthenticate.Select(value => value.Scheme));
        else
            Assert.Empty(response.Headers.WwwAuthenticate);
    }
}

internal sealed class IdentityServerFactory(string issuer, string clientId) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityServer:PersistKeys"] = "false",
            ["IdentityServer:ServerUri"] = issuer,
            ["IdentityServer:Hosts:0"] = issuer,
            ["IdentityServer:Clients:0:ClientId"] = clientId,
            // These tests need no directory connection. Explicit portable settings let them run
            // on Linux too; Windows's configuration-free defaults have separate regression tests.
            ["IdentityServer:Directory:Provider"] = "ActiveDirectory",
            ["IdentityServer:Directory:Server"] = "ldaps://directory.example",
            ["IdentityServer:Directory:BaseDn"] = "dc=example",
            ["IdentityServer:Directory:Realm"] = "EXAMPLE",
        }));
        builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, ConnectionItemsStartupFilter>());
    }

    private sealed class ConnectionItemsStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Features.Set<IConnectionItemsFeature>(new ConnectionItemsFeature());
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class ConnectionItemsFeature : IConnectionItemsFeature
    {
        public IDictionary<object, object?> Items { get; set; } = new ConnectionItems();
    }
}
