using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace IdentityServer
{
    /// <summary>
    /// Middleware that enforces authentication on routed endpoints unless they allow anonymous access
    /// or name their own authentication schemes.
    /// OpenIddict endpoints are handled by the preceding ASP.NET Core authentication middleware.
    /// Unmapped requests return 404 without a Negotiate/Kerberos challenge.
    /// </summary>
    public class Authentication : IMiddleware
    {
        private readonly ILogger<Authentication> _logger;

        public Authentication(ILogger<Authentication> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var endpoint = context.GetEndpoint();
            if (endpoint == null)
            {
                // There is nothing to authenticate for an unknown route (including favicon.ico).
                // Stop before the fallback authorization policy can trigger a browser popup.
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (context.User.Identity?.IsAuthenticated == true
                || endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null
                || NamesOwnSchemes(endpoint))
            {
                await next(context);
            }
            else
            {
                _logger.LogDebug("Requesting Windows credentials for unauthenticated {Method} {Path} from {RemoteIp}. An initial 401 challenge is normal; the client must answer it before this protected endpoint can run.",
                    context.Request.Method, context.Request.Path,
                    context.Connection.RemoteIpAddress);
                await context.ChallengeAsync();
            }
        }

        /// <summary>
        /// Whether the endpoint's authorization names its own authentication schemes. The authorization
        /// middleware authenticates such endpoints with those schemes and issues their challenge,
        /// without this middleware first sending the default Windows authentication challenge.
        /// </summary>
        private static bool NamesOwnSchemes(Endpoint endpoint) =>
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(data => !string.IsNullOrWhiteSpace(data.AuthenticationSchemes));
    }
}
