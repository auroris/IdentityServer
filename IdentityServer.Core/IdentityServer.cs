using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Security.Principal;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using ActiveDirectory;

namespace IdentityServer
{
    /// <summary>
    /// Configures the OpenIddict server and validation stack.
    /// Windows Authentication identifies the person; Active Directory supplies profile and group
    /// details; OpenIddict issues tokens that client applications can understand. Claims are the
    /// named facts carried in those tokens, such as the account identifier, email or role.
    /// Uses OpenIddict's degraded mode, meaning this application supplies client validation and
    /// identity lookup instead of using OpenIddict's database-backed stores.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supported flows: Authorization Code, Implicit, and Hybrid.
    /// Supported scopes: <c>openid</c>, <c>email</c>, <c>profile</c>, <c>roles</c>.
    /// </para>
    /// <para>
    /// When <c>IdentityServer:PersistKeys</c> is <see langword="true"/>, signing and encryption keys
    /// are persisted to disk (under <c>IdentityServer:DataPath</c>), protected at rest with Windows
    /// DPAPI, so the same key material survives app pool recycles and avoids JWKS cache mismatches
    /// in OIDC clients. When <see langword="false"/> (default), ephemeral keys are generated on
    /// every startup.
    /// </para>
    /// </remarks>
    public class IdentityServer
    {
        private sealed class ClientConfig
        {
            public string ClientId { get; init; } = "";
            public string? ClientSecret { get; init; }
        }

        /// <summary>
        /// Finds a matching client from <c>IdentityServer:Clients</c> by case-insensitive <paramref name="clientId"/>
        /// or by the <c>*</c> wildcard entry. Returns <see langword="null"/> if the list is configured
        /// but contains no match, or a wildcard <c>ClientConfig</c> if the list is absent (open access).
        /// </summary>
        private static ClientConfig? FindClient(IConfiguration configuration, string? clientId)
        {
            var clients = configuration.GetSection("IdentityServer:Clients").Get<ClientConfig[]>();
            if (clients == null || clients.Length == 0)
                return new ClientConfig { ClientId = "*" }; // no list configured: accept any client

            return clients.FirstOrDefault(c => c.ClientId.Equals(clientId ?? "", StringComparison.OrdinalIgnoreCase))
                ?? clients.FirstOrDefault(c => c.ClientId == "*");
        }

        /// <summary>
        /// Returns a logger for this class, resolved from the request's DI container when available,
        /// falling back to a null logger outside an HTTP request.
        /// </summary>
        private static ILogger GetLogger(HttpContext? httpContext) =>
            (httpContext?.RequestServices.GetService<ILoggerFactory>()
             ?? NullLoggerFactory.Instance)
            .CreateLogger<IdentityServer>();

        /// <summary>
        /// Loads a persistent RSA key from <paramref name="filename"/> under <c>IdentityServer:DataPath</c>
        /// (defaulting to a <c>keys</c> subfolder of the app base directory), generating and saving a new
        /// 2048-bit key if the file does not yet exist.
        /// </summary>
        /// <remarks>
        /// Private key material is encrypted at rest with Windows DPAPI
        /// (<see cref="DataProtectionScope.LocalMachine"/>); the raw key bytes are never written to disk
        /// in plaintext. Only processes running on the same machine can decrypt the file.
        /// Machine protection is not an account-level access rule: restrict file permissions to
        /// the server account and administrators. Copying the files to a new host is not a key migration.
        /// </remarks>
        private static RsaSecurityKey LoadOrCreateRsaKey(IConfiguration configuration, string filename)
        {
            var dataPath = configuration.GetValue<string>("IdentityServer:DataPath")
                           ?? Path.Combine(AppContext.BaseDirectory, "keys");
            System.IO.Directory.CreateDirectory(dataPath);
            var keyPath = Path.Combine(dataPath, filename);

            var rsa = RSA.Create(2048);
            if (File.Exists(keyPath))
            {
                byte[] decrypted = ProtectedData.Unprotect(
                    File.ReadAllBytes(keyPath), null, DataProtectionScope.LocalMachine);
                rsa.ImportFromPem(Encoding.UTF8.GetString(decrypted));
            }
            else
            {
                byte[] pem = Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem());
                File.WriteAllBytes(keyPath, ProtectedData.Protect(pem, null, DataProtectionScope.LocalMachine));
            }

            return new RsaSecurityKey(rsa);
        }

        /// <summary>
        /// Registers OpenIddict server and validation services in the DI container.
        /// Reads <c>IdentityServer:ServerUri</c>, <c>IdentityServer:Hosts</c>, and
        /// <c>IdentityServer:Groups</c> from the consuming host's configuration.
        /// </summary>
        /// <param name="services">The application service collection.</param>
        /// <param name="configuration">The consuming application's configuration.</param>
        public static void Add(IServiceCollection services, IConfiguration configuration)
        {
            // Each host owns its configuration and cached group patterns. There is no process-global
            // Program state shared between independently configured servers.
            var validGroupPatterns = new Lazy<Regex[]>(() =>
                (configuration.GetSection("IdentityServer:Groups").Get<string[]>() ?? [])
                    .Select(g => new Regex(g, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                    .ToArray());
            services.AddOpenIddict().AddServer(options =>
            {
                // Signing lets clients verify that tokens came from this server. Encryption lets
                // this server protect values such as authorization codes until they are redeemed.
                // Reusing both keys across restarts preserves those relationships; fresh keys can
                // invalidate existing tokens and make outstanding codes unreadable.
                if (configuration.GetValue<bool>("IdentityServer:PersistKeys"))
                    options.AddSigningKey(LoadOrCreateRsaKey(configuration, "signing-key.bin"))
                           .AddEncryptionKey(LoadOrCreateRsaKey(configuration, "encryption-key.bin"));
                else
                    options.AddSigningKey(new RsaSecurityKey(RSA.Create(2048)))
                           .AddEncryptionKey(new RsaSecurityKey(RSA.Create(2048)));
                options.AllowAuthorizationCodeFlow();
                options.AllowHybridFlow();
                options.AllowImplicitFlow();

                var accessTokenLifetime = configuration.GetValue<TimeSpan?>("IdentityServer:AccessTokenLifetime");
                if (accessTokenLifetime.HasValue) options.SetAccessTokenLifetime(accessTokenLifetime.Value);

                var identityTokenLifetime = configuration.GetValue<TimeSpan?>("IdentityServer:IdentityTokenLifetime");
                if (identityTokenLifetime.HasValue) options.SetIdentityTokenLifetime(identityTokenLifetime.Value);

                var authCodeLifetime = configuration.GetValue<TimeSpan?>("IdentityServer:AuthorizationCodeLifetime");
                if (authCodeLifetime.HasValue) options.SetAuthorizationCodeLifetime(authCodeLifetime.Value);
                var serverUri = configuration.GetSection("IdentityServer:ServerUri").Get<string>();
                if (!string.IsNullOrEmpty(serverUri) && serverUri != "*")
                    options.SetIssuer(new Uri(serverUri));
                // Relative endpoint paths keep an IIS application prefix such as /IdentityServer.
                options.SetAuthorizationEndpointUris("connect/authorize")
                       .SetTokenEndpointUris("connect/token");
                // With no OpenIddict stores, our handlers below must validate configured clients
                // and redirect destinations. Degraded mode does not mean validation is optional.
                options.EnableDegradedMode();
                // Permit existing intranet HTTP deployments. This does not encrypt traffic or
                // relax a client application's HTTPS requirements; HTTPS belongs in the host/proxy.
                options.UseAspNetCore()
                    .DisableTransportSecurityRequirement();
                options.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.Roles);

                // The client is the application asking for sign-in, not the Windows user. Check
                // it and the return destination before sending any sign-in result to that location.
                options.AddEventHandler<ValidateAuthorizationRequestContext>(builder =>
                    builder.UseInlineHandler(context =>
                    {
                        var logger = GetLogger(context.Transaction.GetHttpRequest()?.HttpContext);

                        if (FindClient(configuration, context.Request.ClientId) == null)
                        {
                            logger.LogWarning("Authorization request rejected: unknown client_id '{ClientId}'. Match the application's client_id to an entry in IdentityServer:Clients; this identifies the application, not the Windows user.",
                                context.Request.ClientId);
                            // invalid_client produces HTTP 401, which IIS turns into another
                            // Windows challenge even when the user is already authenticated.
                            context.Reject(
                                error: Errors.InvalidRequest,
                                description: "The specified client_id is not valid.");
                            return default;
                        }

                        var redirectHost = new Uri(context.RedirectUri!).Host;
                        // This is a host-name allowlist, not a full callback-URL comparison. CORS
                        // uses the same settings as origins, where scheme and port also matter.
                        if ((configuration.GetSection("IdentityServer:Hosts").Get<string[]>() ?? [])
                            .Any(s => new Uri(s).Host.Equals(redirectHost, StringComparison.OrdinalIgnoreCase)))
                        {
                            return default;
                        }

                        // Log only the host: callback paths or query strings may contain private data.
                        logger.LogWarning("Authorization request rejected for client '{ClientId}': redirect_uri host '{RedirectHost}' is not in IdentityServer:Hosts. Check the application's callback URL and allow its host only if it is a trusted destination for sign-in results.",
                            context.Request.ClientId, redirectHost);
                        context.Reject(
                            error: Errors.InvalidRequest,
                            description: "The specified redirect_uri is not valid.");
                        return default;
                    }));

                // Code flow uses a separate token request to exchange the authorization code.
                // Authenticate the application here, not the browser's Windows account: a server
                // application may make this call without forwarding the user's Windows credentials.
                // A configured ClientSecret of "*" bypasses secret validation; never log secrets/codes.
                options.AddEventHandler<ValidateTokenRequestContext>(builder =>
                    builder.UseInlineHandler(context =>
                    {
                        var logger = GetLogger(context.Transaction.GetHttpRequest()?.HttpContext);

                        var client = FindClient(configuration, context.Request.ClientId);
                        if (client == null)
                        {
                            logger.LogWarning("Token request rejected: unknown client_id '{ClientId}'. Use the same registered application client_id for authorization and token exchange; check IdentityServer:Clients.",
                                context.Request.ClientId);
                            context.Reject(
                                error: Errors.InvalidClient,
                                description: "The specified client_id is not valid.");
                            return default;
                        }

                        if (client.ClientSecret != null && client.ClientSecret != "*" &&
                            !string.Equals(client.ClientSecret, context.Request.ClientSecret, StringComparison.Ordinal))
                        {
                            logger.LogWarning("Token request rejected: invalid client_secret for client '{ClientId}'. The application's credential must match its IdentityServer:Clients entry; this is not the user's Windows password.",
                                context.Request.ClientId);
                            context.Reject(
                                error: Errors.InvalidClient,
                                description: "The specified client_secret is not valid.");
                            return default;
                        }

                        return default;
                    }));

                // Scopes are the claim categories the client requests. Windows proves identity first;
                // directory lookup then supplies the requested facts. Success at one step does not
                // guarantee the next step succeeds (for example, the domain controller may be offline).
                options.AddEventHandler<HandleAuthorizationRequestContext>(builder =>
                    builder.UseInlineHandler(async context =>
                    {
                        var logger = GetLogger(context.Transaction.GetHttpRequest()?.HttpContext);
                        string? winAccountName = null;
                        string stage = "Windows authentication";
                        try
                        {
                            HttpRequest request = context.Transaction.GetHttpRequest()
                                ?? throw new InvalidOperationException("The ASP.NET Core request cannot be retrieved.");

                            // The Negotiate scheme handles the exchange itself on Kestrel and defers to IIS Windows
                            // Authentication when hosted there, so the same call works under both servers.
                            AuthenticateResult result = await request.HttpContext.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
                            if (result.None)
                            {
                                // Nothing has challenged yet: OpenIddict handles this endpoint inside the
                                // authentication middleware, ahead of the Authentication middleware, so on
                                // Kestrel the challenge must be issued here. The browser repeats the same
                                // authorization request with credentials (downgrading from HTTP/2 to
                                // HTTP/1.1), and the Negotiate handler completes the exchange on that
                                // request before this handler runs again. IIS may have already
                                // authenticated the request, in which case no new challenge is needed.
                                logger.LogDebug("Issuing Negotiate challenge for authorization request from {RemoteIp}. This initial 401 asks the browser for Windows credentials and is normal; repeated challenges can indicate that the client is not answering or the credentials are not accepted.",
                                    request.HttpContext.Connection.RemoteIpAddress);
                                await request.HttpContext.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
                                context.HandleRequest();
                                return;
                            }

                            if (!result.Succeeded)
                            {
                                logger.LogWarning("Windows authentication failed for authorization request from {RemoteIp}: {Failure}. No token claims were built. Check the credentials and Windows authentication configuration on the host.",
                                    request.HttpContext.Connection.RemoteIpAddress,
                                    result.Failure?.Message ?? "(no details)");
                                context.Reject(error: Errors.AccessDenied, description: "Windows authentication failed.");
                                return;
                            }

                            stage = "reading the Windows account identity";
                            ClaimsIdentity identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);
                            WindowsIdentity wi = result.Principal?.Identity as WindowsIdentity
                                ?? throw new InvalidOperationException("Negotiate did not produce a Windows identity.");

                            // Account type is independent of logon type: manually entered browser
                            // credentials can produce a network token without the Local logon SID.
                            bool isLocal = wi.HasClaim(ClaimTypes.GroupSid, "S-1-5-113");

                            winAccountName = wi.FindFirst(ClaimTypes.Name)!.Value;
                            // The SID (Windows security identifier) survives an account rename, unlike
                            // its display/logon name, so it is the token's stable subject identifier.
                            string primarySid = wi.FindFirst(ClaimTypes.PrimarySid)!.Value;
                            string samName    = winAccountName.Contains('\\') ? winAccountName.Split('\\')[1] : winAccountName;

                            logger.LogDebug("Building claims for {User} (local: {IsLocal}, scopes: {Scopes})",
                                winAccountName, isLocal, string.Join(" ", context.Request.GetScopes()));

                            if (isLocal)
                            {
                                stage = "building local-account claims";
                                logger.LogDebug("Using local Windows account claims for {User}; Active Directory lookup is skipped, so directory profile fields and group roles are unavailable.", winAccountName);
                                if (context.Request.HasScope(Scopes.OpenId))
                                {
                                    identity.AddClaim(Claims.Subject, primarySid);
                                    identity.AddClaim(ClaimTypes.Name, samName);
                                }

                                if (context.Request.HasScope(Scopes.Profile))
                                {
                                    identity.AddClaim(ClaimTypes.WindowsAccountName, winAccountName);
                                }

                                if (context.Request.HasScope(Scopes.Email))
                                {
                                    // A local account has no directory email. This compatibility value
                                    // is a placeholder, not a verified or deliverable mailbox.
                                    identity.AddClaim(ClaimTypes.Email, samName + "@localhost");
                                }
                            }
                            else
                            {
                                // No impersonation occurs here: these reads use the server process
                                // account. Successful browser sign-in does not prove that account can
                                // reach the domain controller or read the user's directory attributes.
                                stage = "reading the Active Directory profile";
                                logger.LogDebug("Reading Active Directory profile for {User} using the server process account; Windows authentication has already succeeded.", winAccountName);
                                using ADUser user = new ADUser(winAccountName);

                                if (context.Request.HasScope(Scopes.OpenId))
                                {
                                    identity.AddClaim(Claims.Subject, primarySid);
                                    identity.AddClaim(ClaimTypes.Name, user.DisplayName);
                                }

                                if (context.Request.HasScope(Scopes.Email))
                                {
                                    // Preserve the existing placeholder when mail is absent; do not
                                    // interpret an @localhost address as a verified email address.
                                    identity.AddClaim(ClaimTypes.Email, !string.IsNullOrEmpty(user.Email) ? user.Email : user.Username + "@localhost");
                                }

                                if (context.Request.HasScope(Scopes.Profile))
                                {
                                    identity.AddClaim(ClaimTypes.WindowsAccountName, winAccountName);
                                    if (!string.IsNullOrEmpty(user.GivenName)) { identity.AddClaim(ClaimTypes.GivenName, user.GivenName); }
                                    if (!string.IsNullOrEmpty(user.Surname)) { identity.AddClaim(ClaimTypes.Surname, user.Surname); }
                                    if (!string.IsNullOrEmpty(user.TelephoneNumber)) { identity.AddClaim(ClaimTypes.HomePhone, user.TelephoneNumber); }
                                }

                                if (context.Request.HasScope(Scopes.Roles))
                                {
                                    stage = "reading Active Directory groups and applying IdentityServer:Groups";
                                    // Only direct memberships are read; nested groups are not expanded.
                                    // The allowlist limits both exposed group names and token size. Regex
                                    // patterns are not shell wildcards: ^MyApp .*$ means "starts with MyApp ".
                                    var groups = user.GroupsCommonName;
                                    foreach (string group in groups)
                                    {
                                        if (validGroupPatterns.Value.Any(rx => rx.IsMatch(group)))
                                        {
                                            identity.AddClaim(ClaimTypes.Role, group);
                                        }
                                    }
                                    var matchedRoles = identity.FindAll(ClaimTypes.Role).ToList();
                                    int roleCharCount = matchedRoles.Sum(c => c.Value.Length);
                                    logger.LogDebug("User {User} has {Total} direct AD groups; {Matched} matched IdentityServer:Groups ({Chars} role-name characters). Only matching groups become roles; check the patterns and direct memberships if expected roles are missing.",
                                        winAccountName, groups.Count, matchedRoles.Count, roleCharCount);
                                    if (roleCharCount > 4096)
                                        logger.LogWarning("User {User} has {Chars} characters of role claims; this is a size estimate before token encoding. Large tokens can exceed an application's request-header limit (HTTP 431). Narrow IdentityServer:Groups to roles the applications need.",
                                            winAccountName, roleCharCount);
                                }
                            }

                            stage = "preparing token claims";
                            // Scopes above decide which facts to include. Destinations decide where
                            // they travel: the ID token describes the person to the client application;
                            // the access token carries claims to APIs. Keep this distinction in mind
                            // before adding a claim, because this policy sends it to both audiences.
                            identity.SetDestinations(claim => new[]
                            {
                                Destinations.AccessToken,
                                Destinations.IdentityToken
                            });

                            context.Principal = new ClaimsPrincipal(identity);
                            logger.LogInformation("Authorization claims prepared for {User} via {AuthType} for client '{ClientId}'; OpenIddict will now build the protocol response.",
                                winAccountName, wi.AuthenticationType, context.Request.ClientId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error building authorization claims for user '{User}' during {Stage}; returning server_error. If the exception reports a directory failure, check domain connectivity and read permissions for the server process account.",
                                winAccountName ?? "(unknown)", stage);
                            context.Reject(error: Errors.ServerError, description: "An internal error occurred while processing the authorization request.");
                        }
                    }));

                // This covers responses from /connect/token, not tokens returned directly by the
                // authorization endpoint in implicit/hybrid flows. Record lengths only: token values
                // are credentials. The threshold is a diagnostic hint, not a universal server limit.
                options.AddEventHandler<ApplyTokenResponseContext>(builder =>
                    builder.UseInlineHandler(context =>
                    {
                        var logger = GetLogger(context.Transaction.GetHttpRequest()?.HttpContext);
                        int accessTokenLength = context.Response.AccessToken?.Length ?? 0;
                        int idTokenLength     = context.Response.IdToken?.Length     ?? 0;
                        logger.LogDebug("Token endpoint response sizes: access_token {AccessLen} chars, id_token {IdLen} chars. Zero means that token is absent from this response.",
                            accessTokenLength, idTokenLength);
                        if (accessTokenLength > 8192)
                            logger.LogWarning("access_token is {AccessLen} chars. Applications sending it in an Authorization: Bearer header may exceed a server or proxy header limit (HTTP 431). Check IdentityServer:Groups for unnecessary role claims and the receiving application's header limits.",
                                accessTokenLength);
                        return default;
                    }));
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        }
    }
}
