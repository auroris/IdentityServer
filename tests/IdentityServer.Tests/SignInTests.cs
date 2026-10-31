using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace IdentityServer.Tests;

public sealed class SignInTests
{
    private const string Callback = "https://app.example/callback";
    private const string Verifier = "a-test-verifier-with-at-least-forty-three-characters-1234567";

    [Theory]
    [InlineData("FreeIpa", "81f8a447-5bdd-4f57-9e35-678912302922")]
    [InlineData("SambaAd", "S-1-5-21-1-2-3-1001")]
    [InlineData("ActiveDirectory", "S-1-5-21-1-2-3-1001")]
    public async Task KerberosPrincipal_CompletesCodeFlow_WithProviderClaims(string provider, string subject)
    {
        var profiles = new TestProfiles(subject);
        using var host = CreateHost(provider, "Kerberos", profiles);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        using var authorization = await client.GetAsync(AuthorizeUrl(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        var query = QueryHelpers.ParseQuery(authorization.Headers.Location!.Query);
        Assert.False(query.ContainsKey("error"), authorization.Headers.Location.ToString());
        string code = query["code"].ToString();
        Assert.NotEmpty(code);

        using var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "test-client", ["redirect_uri"] = Callback,
            ["code"] = code, ["code_verifier"] = Verifier
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        Assert.Empty(token.Headers.WwwAuthenticate);
        var response = await token.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var jwt = new JsonWebToken(response.GetProperty("id_token").GetString()!);
        Assert.Equal(subject, jwt.Subject);
        Assert.Equal("alice@example.com", jwt.GetClaim(ClaimTypes.Email).Value);
        Assert.Equal("App Readers", Assert.Single(jwt.Claims, c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal(1, profiles.Reads);
        Assert.True(profiles.IncludedGroups);
    }

    [Theory]
    [InlineData("NTLM")]
    [InlineData("Negotiate")]
    public async Task RequiredKerberos_RejectsOtherProtocolsBeforeProfileLookup(string protocol)
    {
        var profiles = new TestProfiles("unused");
        using var host = CreateHost("FreeIpa", protocol, profiles);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        using var authorization = await client.GetAsync(AuthorizeUrl(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
        Assert.Equal("access_denied", QueryHelpers.ParseQuery(authorization.Headers.Location!.Query)["error"].ToString());
        Assert.Equal(0, profiles.Reads);
        // Also enforce the requirement for routed applications when the host supplies an identity
        // directly (as IIS does), bypassing the Negotiate OnAuthenticated event.
        using var routed = await client.GetAsync("/test/protected", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, routed.StatusCode);
        using var anonymous = await client.GetAsync("/test/public", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateHost(string provider, string protocol, TestProfiles profiles) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityServer:ServerUri"] = "https://identity.example/",
                ["IdentityServer:PersistKeys"] = "false",
                ["IdentityServer:Hosts:0"] = "https://app.example",
                ["IdentityServer:Clients:0:ClientId"] = "test-client",
                ["IdentityServer:Groups:0"] = "^App ",
                ["IdentityServer:Authentication:Mode"] = "Kerberos",
                ["IdentityServer:Directory:Provider"] = provider,
                ["IdentityServer:Directory:Server"] = "ldaps://directory.example",
                ["IdentityServer:Directory:BaseDn"] = "dc=example",
                ["IdentityServer:Directory:Realm"] = "EXAMPLE.COM",
                ["Test:Protocol"] = protocol
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IUserProfileProvider>(profiles));
                services.AddTransient<TestAuthenticationHandler>();
                // Substitute only the OS handshake. Keep real OpenIddict authorization, code
                // redemption, claims, token keys, middleware and authentication policy in place.
                services.PostConfigure<AuthenticationOptions>(options =>
                    options.Schemes.Single(s => s.Name == NegotiateDefaults.AuthenticationScheme).HandlerType = typeof(TestAuthenticationHandler));
                services.AddControllers().AddApplicationPart(typeof(SignInTestController).Assembly);
            });
        });

    private static string AuthorizeUrl() => QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
    {
        ["client_id"] = "test-client", ["redirect_uri"] = Callback, ["response_type"] = "code", ["response_mode"] = "query",
        ["scope"] = "openid profile email roles", ["nonce"] = "test-nonce", ["state"] = "test-state",
        ["code_challenge_method"] = "S256", ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)))
    });

    private sealed class TestProfiles(string subject) : IUserProfileProvider
    {
        public int Reads { get; private set; }
        public bool IncludedGroups { get; private set; }
        public UserProfile GetProfile(ClaimsPrincipal principal, bool includeGroups)
        {
            Reads++; IncludedGroups = includeGroups;
            return new() { Subject = subject, Username = "alice", AccountName = principal.Identity!.Name!, DisplayName = "Alice",
                Email = "alice@example.com", Groups = ["App Readers", "Administrators"] };
        }
    }
}

public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
        new AuthenticationTicket(IdentityProviderTests.Principal(protocol: configuration["Test:Protocol"]!), Scheme.Name)));
}

[ApiController]
public sealed class SignInTestController : ControllerBase
{
    [HttpGet("/test/protected")]
    public IActionResult Protected() => Ok();
    [AllowAnonymous, HttpGet("/test/public")]
    public IActionResult Public() => Ok();
}
