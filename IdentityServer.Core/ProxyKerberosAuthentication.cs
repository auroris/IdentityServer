using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdentityServer;

internal sealed class ProxyKerberosOptions
{
    internal const string Scheme = "ProxyKerberos";
    internal const string UserHeader = "X-IdentityServer-User";
    internal const string SecretHeader = "X-IdentityServer-Proxy-Secret";
    private readonly byte[] _secretHash;

    internal ProxyKerberosOptions(IConfiguration configuration, ProxyHostingOptions hosting)
    {
        if (!hosting.HasProxyConfiguration || !hosting.UseForwardedHeaders)
            throw new InvalidOperationException("ProxyKerberos requires explicit ReverseProxy settings with trusted proxy IP addresses.");
        string? secret = configuration["IdentityServer:Authentication:ProxySecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || secret.Length > 1024 || secret.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("ProxyKerberos requires Authentication:ProxySecret containing at least 32 non-whitespace characters. Supply a random shared secret through deployment secrets.");
        _secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    internal bool Matches(string secret) => secret.Length <= 1024 &&
        CryptographicOperations.FixedTimeEquals(_secretHash, SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

/// <summary>
/// Accepts a Kerberos principal attested by a configured authenticating proxy. Header values alone
/// never establish trust: require the original peer, protected transport and a per-deployment secret.
/// </summary>
internal sealed class ProxyKerberosHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, ProxyKerberosOptions proxy)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ProxyKerberosOptions.UserHeader, out var users) || users.Count == 0 ||
            (users.Count == 1 && string.IsNullOrEmpty(users[0])))
            return Task.FromResult(AuthenticateResult.NoResult());

        var connection = Context.Features.Get<ProxyConnectionFeature>();
        if (connection is not { Trusted: true, ProtectedTransport: true } ||
            !Request.Headers.TryGetValue(ProxyKerberosOptions.SecretHeader, out var secrets) ||
            secrets.Count != 1 || string.IsNullOrEmpty(secrets[0]) || !proxy.Matches(secrets[0]!))
            return Task.FromResult(AuthenticateResult.Fail("The authenticating proxy could not be verified."));

        string user = users[0] ?? "";
        int at = user.LastIndexOf('@');
        if (users.Count != 1 || user.Length > 1024 || at <= 0 || at == user.Length - 1 ||
            user.IndexOf('@') != at || user.Any(char.IsWhiteSpace) || user.Any(char.IsControl) || user.IndexOfAny([',', '/', '\\']) >= 0)
            return Task.FromResult(AuthenticateResult.Fail("The proxy must supply one realm-qualified user principal."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], ProxyKerberosOptions.Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // The front proxy must authenticate this route. Challenging here would start an OS
        // Negotiate exchange on a pooled backend connection and would defeat this hosting mode.
        Logger.LogWarning("ProxyKerberos request has no verified principal. Check the front proxy's authentication and header configuration for this route.");
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
