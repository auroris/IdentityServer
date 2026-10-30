using System;
using System.Collections.Generic;
using System.DirectoryServices;

namespace ActiveDirectory
{
    /// <summary>
    /// Represents an Active Directory security or distribution group.
    /// Provides typed accessors for common group attributes and methods to enumerate
    /// members and parent groups.
    /// </summary>
    public class ADGroup : ADObject
    {
        /// <summary>Parameterless constructor for use when <see cref="ADObject.adobject"/> is set separately.</summary>
        public ADGroup() { }

        /// <summary>Constructs an <see cref="ADGroup"/> from its AD GUID.</summary>
        /// <param name="guid">The group's AD GUID.</param>
        public ADGroup(ADGuid guid) : base(guid) { }

        /// <summary>Constructs an <see cref="ADGroup"/> wrapping an existing <see cref="DirectoryEntry"/>.</summary>
        /// <param name="entry">An already-opened <see cref="DirectoryEntry"/> for a group object.</param>
        public ADGroup(DirectoryEntry entry) : base(entry) { }

        /// <summary>Constructs an <see cref="ADGroup"/> from a full LDAP path or distinguished name.</summary>
        /// <param name="ldap">A full LDAP path or bare DN for the group object.</param>
        public ADGroup(String ldap) : base(ldap) { }

        /// <summary>Gets the group's description (the <c>description</c> attribute).</summary>
        public String Description
        {
            get { return GetProperty(ADProperties.Group.Description); }
        }

        /// <summary>Gets the group's notes/info field (the <c>info</c> attribute).</summary>
        public String Notes
        {
            get { return GetProperty(ADProperties.Group.Notes); }
        }

        /// <summary>
        /// Gets the AD object designated as the group's manager (the <c>managedBy</c> attribute).
        /// Returns <c>null</c> if no manager is set.
        /// Check <see cref="ADObject.SchemaClass"/> on the returned object to determine its type.
        /// </summary>
        public ADObject? ManagedBy
        {
            get
            {
                String dn = GetProperty(ADProperties.Group.ManagedBy);
                return dn.Length > 0 ? new ADObject(dn) : null;
            }
        }

        /// <summary>Gets the group's pre-Windows 2000 (SAM account) name (the <c>sAMAccountName</c> attribute).</summary>
        public String Windows2000Name
        {
            get { return GetProperty(ADProperties.Group.SamAccountName); }
        }

        /// <summary>
        /// Adds an Active Directory object to this group's membership list.
        /// Call <see cref="ADObject.Save"/> afterwards to commit the change to AD.
        /// </summary>
        /// <param name="obj">The AD object to add as a member.</param>
        public void AddMember(ADObject obj)
        {
            AssertBound().Properties[ADProperties.Group.Member].Add(obj.LDAPName);
        }

        /// <summary>
        /// Removes an Active Directory object from this group's membership list.
        /// Call <see cref="ADObject.Save"/> afterwards to commit the change to AD.
        /// </summary>
        /// <param name="obj">The AD object to remove from membership.</param>
        public void RemoveMember(ADObject obj)
        {
            AssertBound().Properties[ADProperties.Group.Member].Remove(obj.LDAPName);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="obj"/> is a transitive member of this group,
        /// using the <see cref="ADObject.LDAP_MATCHING_RULE_IN_CHAIN"/> extensible match rule.
        /// A single base-scope LDAP query is issued against <paramref name="obj"/>'s directory entry.
        /// </summary>
        /// <param name="obj">The object to test for transitive membership.</param>
        public bool ContainsMember(ADObject obj)
        {
            using var searcher = new DirectorySearcher(obj.Get);
            searcher.SearchScope = SearchScope.Base;
            searcher.Filter = $"(memberOf:{LDAP_MATCHING_RULE_IN_CHAIN}:=<GUID={base.ADGuid.ToString()}>)";
            return searcher.FindOne() != null;
        }

        /// <summary>
        /// Gets the list of direct members of this group.
        /// Makes three separate LDAP queries to retrieve member groups, users, and computers
        /// respectively, then combines them into a single list.
        /// </summary>
        public List<Membership> Members
        {
            get
            {
                List<Membership> info = new List<Membership>();
                String memberFilter = $"memberOf=<GUID={base.ADGuid.ToString()}>";

                // Find all member groups
                var groupSearch = new DirectorySearch(ObjectClass.Group);
                groupSearch.AdditionalFilter = memberFilter;
                groupSearch.PropertiesToLoad.Add(ADProperties.CommonName);
                groupSearch.PropertiesToLoad.Add(ADProperties.DistinguishedName);
                foreach (var g in groupSearch.Find<ADGroup>())
                    info.Add(new Membership() { ObjectType = ObjectClass.Group, DisplayName = g.Name, DistinguishedName = g.QualifiedName, ADGuid = g.ADGuid });

                // Find all member users
                var userSearch = new DirectorySearch(ObjectClass.User);
                userSearch.AdditionalFilter = memberFilter;
                userSearch.PropertiesToLoad.Add(ADProperties.User.DisplayName);
                userSearch.PropertiesToLoad.Add(ADProperties.DistinguishedName);
                foreach (var u in userSearch.Find<ADUser>())
                {
                    String dn = u.QualifiedName;
                    // Fall back to the DN if displayName is absent (e.g. service accounts)
                    String displayName = u.DisplayName.Length > 0 ? u.DisplayName : $"<{dn}>";
                    info.Add(new Membership() { ObjectType = ObjectClass.User, DisplayName = displayName, DistinguishedName = dn, ADGuid = u.ADGuid });
                }

                // Find all member computers
                var computerSearch = new DirectorySearch(ObjectClass.Computer);
                computerSearch.AdditionalFilter = memberFilter;
                computerSearch.PropertiesToLoad.Add(ADProperties.CommonName);
                computerSearch.PropertiesToLoad.Add(ADProperties.DistinguishedName);
                foreach (var c in computerSearch.Find<ADComputer>())
                    info.Add(new Membership() { ObjectType = ObjectClass.Computer, DisplayName = c.Name, DistinguishedName = c.QualifiedName, ADGuid = c.ADGuid });

                return info;
            }
        }

        /// <summary>
        /// Gets the list of groups that this group transitively belongs to, using the
        /// <see cref="ADObject.LDAP_MATCHING_RULE_IN_CHAIN"/> extensible match rule.
        /// Searches the current domain.
        /// </summary>
        public List<Membership> Parents
        {
            get
            {
                List<Membership> info = new List<Membership>();

                var search = new DirectorySearch(ObjectClass.Group);
                search.AdditionalFilter = $"member:{LDAP_MATCHING_RULE_IN_CHAIN}:=<GUID={base.ADGuid.ToString()}>";
                search.PropertiesToLoad.Add(ADProperties.CommonName);
                search.PropertiesToLoad.Add(ADProperties.DistinguishedName);
                foreach (var g in search.Find<ADGroup>())
                    info.Add(new Membership() { ObjectType = ObjectClass.Group, DisplayName = g.Name, DistinguishedName = g.QualifiedName, ADGuid = g.ADGuid });

                return info;
            }
        }

        /// <summary>
        /// Represents a single member entry returned by <see cref="Members"/> or <see cref="Parents"/>.
        /// </summary>
        public class Membership
        {
            /// <summary>The member's full distinguished name (e.g. <c>CN=...,OU=...,DC=...</c>).</summary>
            public string DistinguishedName = "";

            /// <summary>The member's display name, or its DN in angle brackets if no display name is set.</summary>
            public string DisplayName = "";

            /// <summary>The member's AD GUID.</summary>
            public ADGuid ADGuid = new ADGuid();

            /// <summary>Whether this member is a user, group, or computer.</summary>
            public ObjectClass ObjectType;
        }

        /// <summary>
        /// Flags describing a group's scope and type.
        /// Values can be combined (e.g. <c>GlobalGroup | SecurityGroup</c>).
        /// </summary>
        public enum GroupType : uint
        {
            /// <summary>Universal scope — membership can span domains in a forest.</summary>
            UniversalGroup   = 0x08,

            /// <summary>Domain local scope — membership is limited to the local domain.</summary>
            DomainLocalGroup = 0x04,

            /// <summary>Global scope — membership is limited to the same domain.</summary>
            GlobalGroup      = 0x02,

            /// <summary>Security group (as opposed to a distribution group).</summary>
            SecurityGroup    = 0x80000000
        }

        /// <summary>
        /// Walks the chain of ancestry in objects all the way to the root until it finds a match.
        /// Used for transitive group membership queries.
        /// </summary>
        private const String LDAP_MATCHING_RULE_IN_CHAIN = "1.2.840.113556.1.4.1941";
    }
}
