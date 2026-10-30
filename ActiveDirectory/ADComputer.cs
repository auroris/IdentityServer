using System;
using System.DirectoryServices;

namespace ActiveDirectory
{
    /// <summary>
    /// Represents an Active Directory computer account.
    /// Provides typed accessors for common computer attributes.
    /// Group membership and account-control flag testing are inherited from <see cref="ADObject"/>.
    /// </summary>
    public class ADComputer : ADObject
    {
        /// <summary>Parameterless constructor for use when <see cref="ADObject.adobject"/> is set separately.</summary>
        public ADComputer() { }

        /// <summary>Constructs an <see cref="ADComputer"/> from its AD GUID.</summary>
        /// <param name="guid">The computer object's AD GUID.</param>
        public ADComputer(ADGuid guid) : base(guid) { }

        /// <summary>Constructs an <see cref="ADComputer"/> wrapping an existing <see cref="DirectoryEntry"/>.</summary>
        /// <param name="entry">An already-opened <see cref="DirectoryEntry"/> for a computer object.</param>
        public ADComputer(DirectoryEntry entry) : base(entry) { }

        /// <summary>Constructs an <see cref="ADComputer"/> from a full LDAP path or distinguished name.</summary>
        /// <param name="ldap">A full LDAP path (e.g. <c>LDAP://CN=...,DC=...</c>) or a bare DN.</param>
        public ADComputer(String ldap) : base(ldap) { }

        /// <summary>Gets the computer object's description (the <c>description</c> attribute).</summary>
        public String Description
        {
            get { return GetProperty(ADProperties.Computer.Description); }
        }

        /// <summary>Gets the computer's fully-qualified DNS host name (the <c>dNSHostName</c> attribute).</summary>
        public String DnsName
        {
            get { return GetProperty(ADProperties.Computer.DnsHostName); }
        }
    }
}
