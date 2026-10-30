using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IdentityServer;

internal static class ProfileClaims
{
    internal static ClaimsIdentity Create(UserProfile user, IEnumerable<string> scopes, Regex[] groupPatterns)
    {
        if (string.IsNullOrWhiteSpace(user.Subject))
            throw new InvalidOperationException("The directory returned no stable subject identifier.");
        var requested = scopes.ToHashSet(StringComparer.Ordinal);
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);
        if (requested.Contains(Scopes.OpenId))
        {
            identity.AddClaim(Claims.Subject, user.Subject);
            identity.AddClaim(ClaimTypes.Name, user.DisplayName);
        }
        if (requested.Contains(Scopes.Email))
            identity.AddClaim(ClaimTypes.Email, string.IsNullOrEmpty(user.Email) ? user.Username + "@localhost" : user.Email);
        if (requested.Contains(Scopes.Profile))
        {
            identity.AddClaim(ClaimTypes.WindowsAccountName, user.AccountName);
            if (!string.IsNullOrEmpty(user.GivenName)) identity.AddClaim(ClaimTypes.GivenName, user.GivenName);
            if (!string.IsNullOrEmpty(user.Surname)) identity.AddClaim(ClaimTypes.Surname, user.Surname);
            if (!string.IsNullOrEmpty(user.TelephoneNumber)) identity.AddClaim(ClaimTypes.HomePhone, user.TelephoneNumber);
        }
        if (requested.Contains(Scopes.Roles))
            foreach (string group in user.Groups.Distinct(StringComparer.Ordinal))
                if (groupPatterns.Any(pattern => pattern.IsMatch(group))) identity.AddClaim(ClaimTypes.Role, group);
        return identity;
    }
}
