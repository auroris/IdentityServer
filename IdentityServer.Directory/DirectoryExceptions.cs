using System;

namespace IdentityServer.Directory;

/// <summary>
/// A directory request that cannot be run as asked: a global search term below the minimum
/// length, too many terms, a malformed id. The message is written for the caller, so an HTTP
/// API can return it in a 400 response.
/// </summary>
public sealed class DirectoryQueryException : Exception
{
    /// <summary>Creates the exception with a message that tells the caller what to change.</summary>
    /// <param name="message">The reason the request was refused.</param>
    public DirectoryQueryException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A directory source could not be queried: no domain controller, a misconfigured LDAP root, the
/// process account refused. The inner exception carries the cause; an HTTP API can answer 503
/// instead of failing the host.
/// </summary>
public sealed class DirectoryUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What could not be queried.</param>
    /// <param name="innerException">The underlying failure.</param>
    public DirectoryUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
