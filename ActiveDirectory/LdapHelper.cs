namespace ActiveDirectory
{
    /// <summary>
    /// Shared LDAP utility methods used across controllers and AD wrapper classes.
    /// </summary>
    public static class LdapHelper
    {
        /// <summary>
        /// Sanitizes a string for safe use in an LDAP filter expression (<see cref="DirectorySearcher.Filter"/>)
        /// by escaping all characters that have special meaning in LDAP filter syntax (RFC 4515).
        /// Safe to use with arbitrary, untrusted input.
        /// <para>
        /// The backslash replacement is performed first to prevent double-escaping any of the
        /// subsequently inserted escape sequences.
        /// </para>
        /// </summary>
        /// <param name="value">The raw, untrusted input string (e.g. from a query parameter or user form field).</param>
        /// <returns>
        /// A copy of <paramref name="value"/> with all RFC 4515 special characters escaped,
        /// or an empty string if <paramref name="value"/> is <c>null</c>.
        /// </returns>
        public static string CleanLDAPString(string? value)
        {
            if (value == null) return "";
            value = value.Trim().Trim((char)0);
            value = value.Replace("\\", "\\5c"); // must be first to avoid double-escaping
            value = value.Replace("*", "\\2a");
            value = value.Replace("(", "\\28");
            value = value.Replace(")", "\\29");
            value = value.Replace("\0", "\\00");
            return value;
        }
    }
}