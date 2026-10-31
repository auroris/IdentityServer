using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Runtime.Versioning;
using ActiveDirectory;

namespace IdentityServer;

/// <summary>Directory-independent facts used to issue tokens. Subject must survive account renames.</summary>
public sealed record UserProfile
{
    public required string Subject { get; init; }
    public required string Username { get; init; }
    public required string AccountName { get; init; }
    public required string DisplayName { get; init; }
    public string Email { get; init; } = "";
    public string GivenName { get; init; } = "";
    public string Surname { get; init; } = "";
    public string TelephoneNumber { get; init; } = "";
    public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();
    public bool IsLocal { get; init; }
}

/// <summary>Resolves an already authenticated identity; never authenticates a supplied username.</summary>
public interface IUserProfileProvider
{
    UserProfile GetProfile(ClaimsPrincipal principal, bool includeGroups);
}

/// <summary>Resolves Windows domain accounts using process credentials and handles local accounts without directory access.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsUserProfileProvider : IUserProfileProvider
{
    public UserProfile GetProfile(ClaimsPrincipal principal, bool includeGroups)
    {
        if (principal.Identity?.IsAuthenticated != true)
            throw new InvalidOperationException("An authenticated Windows identity is required.");
        string account = principal.Identity.Name ?? throw new InvalidOperationException("The authenticated account has no name.");
        string subject = principal.FindFirst(ClaimTypes.PrimarySid)?.Value
            ?? throw new InvalidOperationException("The authenticated Windows account has no primary SID.");
        string username = account.Contains('\\') ? account[(account.IndexOf('\\') + 1)..] : account;
        if (principal.HasClaim(ClaimTypes.GroupSid, "S-1-5-113"))
            return new() { Subject = subject, Username = username, AccountName = account, DisplayName = username, IsLocal = true };

        using var user = new ADUser(account);
        return new()
        {
            Subject = subject, Username = user.Username, AccountName = account, DisplayName = user.DisplayName,
            Email = user.Email, GivenName = user.GivenName, Surname = user.Surname, TelephoneNumber = user.TelephoneNumber,
            Groups = includeGroups ? user.GroupsCommonName : Array.Empty<string>()
        };
    }
}
