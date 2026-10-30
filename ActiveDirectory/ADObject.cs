using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Security.Principal;

namespace ActiveDirectory
{
    /// <summary>
    /// Base class for all Active Directory object wrappers (ADUser, ADGroup, ADComputer, ADPrinter).
    /// Wraps a <see cref="DirectoryEntry"/> and provides common property accessors,
    /// safe property retrieval, and LDAP query helpers.
    /// Implements <see cref="IDisposable"/> to ensure the underlying <see cref="DirectoryEntry"/>
    /// is released promptly.
    /// </summary>
    public class ADObject : IDisposable
    {
        /// <summary>The underlying Active Directory object. Null until bound (either by constructor or lazy-bind).</summary>
        protected DirectoryEntry? adobject = null;

        // When populated from a DirectorySearcher result, the pre-fetched properties are copied
        // into a plain dictionary so they are independent of the COM-backed SearchResultCollection
        // lifecycle. The LDAP path is stored so the entry can be bound on demand for writes or
        // direct-entry access, but property reads never bind when the cache is populated.
        private Dictionary<string, List<object?>>? _cache;
        private string? _ldapPath;

        private bool disposedValue;

        /// <summary>Parameterless constructor for use by subclasses that set <see cref="adobject"/> themselves.</summary>
        public ADObject() { }

        /// <summary>
        /// Sets the underlying <see cref="DirectoryEntry"/> after construction.
        /// Used when a caller has an already-opened entry to wrap directly.
        /// </summary>
        internal void SetEntry(DirectoryEntry entry) => adobject = entry;

        /// <summary>
        /// Populates this object from a <see cref="SearchResult"/> returned by
        /// <see cref="DirectorySearch"/>. All pre-fetched properties are copied into a plain
        /// dictionary so they remain accessible after the <see cref="SearchResultCollection"/>
        /// is disposed. Property reads are served entirely from this cache.
        /// </summary>
        internal void SetFromResult(SearchResult result)
        {
            _ldapPath = result.Path;
            _cache = new Dictionary<string, List<object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (string propName in result.Properties.PropertyNames)
            {
                var values = new List<object?>(result.Properties[propName].Count);
                foreach (var val in result.Properties[propName])
                    values.Add(val);
                _cache[propName] = values;
            }
        }

        /// <summary>
        /// Returns the underlying <see cref="DirectoryEntry"/>.
        /// Throws <see cref="InvalidOperationException"/> if the object is not yet bound —
        /// either because it has not been populated at all, or because it was populated from a
        /// <see cref="DirectorySearch"/> result and <see cref="Bind"/> has not been called yet.
        /// </summary>
        protected DirectoryEntry AssertBound()
        {
            if (adobject == null)
            {
                if (_ldapPath == null)
                    throw new InvalidOperationException("No Active Directory object is bound.");

                // Object came from a DirectorySearcher result (_ldapPath is set by SetFromResult).
                // Automatic binding has been removed to prevent unexpected COM exhaustion.
                // Call Bind() explicitly before accessing properties that require a live connection.
                throw new InvalidOperationException(
                    "This object was populated from a DirectorySearcher result and is not bound to a " +
                    "live Active Directory connection. Call Bind() explicitly before accessing this property.");
            }
            return adobject;
        }

        /// <summary>
        /// Opens a live connection to Active Directory for this object.
        /// <para>
        /// Must be called explicitly when this object was populated from a <see cref="DirectorySearch"/>
        /// result and you need to access properties that were not pre-fetched, or properties that
        /// always require a live connection.
        /// Prefer adding required attributes to <see cref="DirectorySearch.PropertiesToLoad"/> over
        /// calling Bind().
        /// </para>
        /// <para>If the object is already bound, this is a no-op.</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if no LDAP path is available to bind against.</exception>
        public void Bind()
        {
            if (adobject != null) return;
            if (_ldapPath == null)
                throw new InvalidOperationException("No Active Directory object is bound.");

            // Prefer GUID-based binding — survives OU moves between search and bind.
            string path = _ldapPath;
            if (_cache != null
                && _cache.TryGetValue(ADProperties.ObjectGuid, out var guidVals)
                && guidVals.Count > 0
                && guidVals[0] is byte[] bytes)
            {
                path = "LDAP://<GUID=" + new ADGuid(bytes).ToString() + ">";
            }

            adobject = new DirectoryEntry(path);

            // Drop the search-result cache so all subsequent property reads go to the live
            // DirectoryEntry rather than returning stale values from the search snapshot.
            _cache = null;
        }

        /// <summary>
        /// Discards ADSI's internal property cache and re-fetches all attributes from Active
        /// Directory on the next property access.
        /// <para>
        /// After <see cref="Bind"/> is called, <see cref="DirectoryEntry"/> builds its own
        /// ADSI-level cache as properties are read. If the underlying AD object changes after
        /// binding, those changes will not be visible until <see cref="Refresh"/> is called.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the object is not yet bound.</exception>
        public void Refresh()
        {
            AssertBound().RefreshCache();
        }

        /// <summary>
        /// Constructs an <see cref="ADObject"/> from a full LDAP path or a distinguished name.
        /// If the string already starts with <c>LDAP://</c> it is used as-is; otherwise
        /// <c>LDAP://</c> is prepended.
        /// </summary>
        /// <param name="ldap">A full LDAP path (e.g. <c>LDAP://CN=...,DC=...</c>) or a bare DN.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="ldap"/> is null or empty.</exception>
        public ADObject(String ldap)
        {
            if (string.IsNullOrEmpty(ldap))
                throw new ArgumentNullException(nameof(ldap));

            if (ldap.Contains("LDAP://"))
            {
                adobject = new DirectoryEntry(ldap);
            }
            else
            {
                adobject = new DirectoryEntry("LDAP://" + ldap);
            }
        }

        /// <summary>
        /// Constructs an <see cref="ADObject"/> wrapping an existing <see cref="DirectoryEntry"/>.
        /// </summary>
        /// <param name="adobject">An already-opened <see cref="DirectoryEntry"/>.</param>
        public ADObject(DirectoryEntry adobject)
        {
            this.adobject = adobject;
        }

        /// <summary>
        /// Constructs an <see cref="ADObject"/> by GUID, using the <c>LDAP://&lt;GUID=...&gt;</c> syntax
        /// to locate the object regardless of its current OU placement.
        /// </summary>
        /// <param name="guid">The object's AD GUID.</param>
        public ADObject(ADGuid guid)
        {
            adobject = new DirectoryEntry("LDAP://<GUID=" + guid.ToString() + ">");
        }

        /// <summary>
        /// Gets the AD schema class of this object, derived from <see cref="System.DirectoryServices.DirectoryEntry.SchemaClassName"/>.
        /// Returns <see cref="ObjectClass.Unknown"/> for unrecognised classes.
        /// </summary>
        public ObjectClass SchemaClass
        {
            get
            {
                return AssertBound().SchemaClassName switch
                {
                    "user"       => ObjectClass.User,
                    "group"      => ObjectClass.Group,
                    "computer"   => ObjectClass.Computer,
                    "printQueue" => ObjectClass.Printer,
                    _            => ObjectClass.Unknown
                };
            }
        }

        /// <summary>Gets the underlying <see cref="DirectoryEntry"/> for direct access when needed.</summary>
        public DirectoryEntry Get
        {
            get { return AssertBound(); }
        }

        /// <summary>
        /// Gets the object's AD GUID.
        /// When populated from a search result that included <c>objectguid</c> in
        /// <see cref="DirectorySearch.PropertiesToLoad"/>, the GUID is read from the cache
        /// without triggering an AD round-trip.
        /// </summary>
        public ADGuid ADGuid
        {
            get
            {
                if (_cache != null)
                {
                    if (_cache.TryGetValue(ADProperties.ObjectGuid, out var vals)
                        && vals.Count > 0 && vals[0] is byte[] bytes)
                        return new ADGuid(bytes);
                    return new ADGuid(); // objectguid is always loaded by DirectorySearch.CreateSearcher
                }
                return new ADGuid(AssertBound().NativeGuid);
            }
        }

        /// <summary>Gets the object's common name (the <c>cn</c> attribute).</summary>
        public String Name
        {
            get { return GetProperty(ADProperties.CommonName); }
        }

        /// <summary>Gets the object's fully qualified distinguished name (the <c>distinguishedName</c> attribute).</summary>
        public String QualifiedName
        {
            get { return GetProperty(ADProperties.DistinguishedName); }
        }

        /// <summary>
        /// Gets the object's security identifier (the <c>objectSid</c> attribute) in string form,
        /// e.g. <c>S-1-5-21-…</c>, or an empty string when the object has none (or, for a search
        /// result, when <c>objectSid</c> was not in <see cref="DirectorySearch.PropertiesToLoad"/>).
        /// This is the value Windows puts in a logon token and the identity server issues as <c>sub</c>.
        /// </summary>
        public String Sid
        {
            get
            {
                byte[]? bytes = GetBytes(ADProperties.ObjectSid);
                return bytes != null ? new SecurityIdentifier(bytes, 0).Value : "";
            }
        }

        /// <summary>
        /// Gets the object's LDAP name as returned by <see cref="DirectoryEntry.Name"/>
        /// (e.g. <c>CN=John Smith</c>). Always requires a live AD connection.
        /// </summary>
        public String LDAPName
        {
            get { return AssertBound().Name; }
        }

        /// <summary>
        /// Gets the UTC time at which this object was created.
        /// <para>
        /// Note: <c>whenCreated</c> is not replicated between domain controllers,
        /// so the value depends on which DC is queried.
        /// </para>
        /// </summary>
        public DateTime WhenCreated
        {
            get
            {
                if (_cache != null && _cache.TryGetValue(ADProperties.WhenCreated, out var vals) && vals.Count > 0 && vals[0] is DateTime dt)
                    return dt;
                object? val = AssertBound().Properties[ADProperties.WhenCreated].Value;
                if (val == null)
                    throw new InvalidOperationException(
                        $"Active Directory object has no '{ADProperties.WhenCreated}' attribute.");
                return (DateTime)val;
            }
        }

        /// <summary>
        /// Gets the UTC time at which this object was last modified, or <c>null</c> if not set.
        /// <para>
        /// Note: <c>whenChanged</c> is not replicated between domain controllers,
        /// so the value depends on which DC is queried.
        /// </para>
        /// </summary>
        public DateTime? WhenChanged
        {
            get
            {
                if (_cache != null && _cache.TryGetValue(ADProperties.WhenChanged, out var vals) && vals.Count > 0 && vals[0] is DateTime dt)
                    return dt;
                return (DateTime?)AssertBound().Properties[ADProperties.WhenChanged].Value;
            }
        }

        /// <summary>
        /// Gets the list of distinguished names of all groups this object is a direct member of
        /// (the <c>memberOf</c> attribute, returned as full LDAP strings).
        /// Nested memberships are not expanded here, so this list is not the complete set of
        /// groups Windows may use when deciding access to a resource.
        /// </summary>
        public List<String> Groups
        {
            get { return GetList(ADProperties.MemberOf); }
        }

        /// <summary>
        /// Same as <see cref="Groups"/>, but returns only the common name (CN) portion of each
        /// group's distinguished name rather than the full LDAP string.
        /// </summary>
        public List<String> GroupsCommonName
        {
            get
            {
                List<String> result = new List<String>();

                foreach (String group in Groups)
                {
                    foreach (String el in group.Split(','))
                    {
                        if (el.StartsWith("CN="))
                        {
                            result.Add(el.Substring(3));
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Returns a list of all AD attribute names present on this object.
        /// Always requires a live AD connection to enumerate the full property set.
        /// </summary>
        public List<String> Attributes
        {
            get
            {
                List<String> list = new List<String>();

                foreach (string prop in AssertBound().Properties.PropertyNames)
                {
                    list.Add(prop);
                }

                return list;
            }
        }

        /// <summary>
        /// Safely retrieves a string-valued AD attribute by name.
        /// When populated from a search result, reads from the pre-fetched cache without binding:
        /// an attribute missing from the result (no value in AD, or not requested in
        /// <see cref="DirectorySearch.PropertiesToLoad"/>) reads as absent. Call <see cref="Bind"/>
        /// first to read an attribute that was not requested.
        /// Returns an empty string if the attribute is absent, null, or not a string.
        /// </summary>
        /// <param name="propertyName">The LDAP attribute name (e.g. <c>"givenName"</c>).</param>
        /// <returns>The attribute value, or an empty string if the attribute is not present.</returns>
        public String GetProperty(String propertyName)
        {
            if (_cache != null)
            {
                if (_cache.TryGetValue(propertyName, out var vals) && vals.Count > 0)
                    return vals[0] as string ?? "";
                if (adobject == null)
                    return "";
            }
            return AssertBound().Properties[propertyName].Value as string ?? "";
        }

        /// <summary>
        /// Sets a string-valued AD attribute by name.
        /// Passing an empty string clears the attribute (sets it to <c>null</c> in AD).
        /// Call <see cref="Save"/> afterwards to commit the change.
        /// </summary>
        /// <param name="propertyName">The LDAP attribute name (e.g. <c>"givenName"</c>).</param>
        /// <param name="value">The value to write, or an empty string to clear the attribute.</param>
        public void SetProperty(String propertyName, String value)
        {
            AssertBound().Properties[propertyName].Value = value.Length > 0 ? value : null;
        }

        /// <summary>
        /// Retrieves a multi-valued string attribute as a <see cref="List{String}"/>.
        /// When populated from a search result, reads from the pre-fetched cache without binding:
        /// an attribute missing from the result (no value in AD, or not requested in
        /// <see cref="DirectorySearch.PropertiesToLoad"/>) reads as absent. Call <see cref="Bind"/>
        /// first to read an attribute that was not requested.
        /// If the attribute holds a single value it is wrapped in a list.
        /// Returns an empty list if the attribute is absent or null.
        /// </summary>
        /// <param name="property">The LDAP attribute name.</param>
        /// <returns>A list of all string values for the attribute.</returns>
        public List<String> GetList(String property)
        {
            if (_cache != null)
            {
                if (_cache.TryGetValue(property, out var vals) && vals.Count > 0)
                {
                    var result = new List<string>(vals.Count);
                    foreach (var v in vals)
                        if (v is string s) result.Add(s);
                    return result;
                }
                if (adobject == null)
                    return new List<string>();
            }

            object? val = AssertBound().Properties[property].Value;
            if (val == null) return new List<String>();
            if (val is Array arr) return arr.OfType<String>().ToList();
            try { return new List<String> { (String)val }; }
            catch (InvalidCastException ex)
            {
                throw new InvalidOperationException(
                    $"Property '{property}' has an unexpected type '{val.GetType().FullName}'; expected String.", ex);
            }
        }

        /// <summary>
        /// Retrieves an integer-valued AD attribute.
        /// When populated from a search result, reads from the pre-fetched cache without binding:
        /// an attribute missing from the result (no value in AD, or not requested in
        /// <see cref="DirectorySearch.PropertiesToLoad"/>) reads as absent. Call <see cref="Bind"/>
        /// first to read an attribute that was not requested.
        /// Returns <c>null</c> if the attribute is absent.
        /// </summary>
        /// <param name="property">The LDAP attribute name.</param>
        /// <returns>The integer value, or <c>null</c> if the attribute is not present.</returns>
        public Nullable<Int32> GetInt32(String property)
        {
            if (_cache != null)
            {
                if (_cache.TryGetValue(property, out var vals) && vals.Count > 0)
                    return vals[0] is int i ? i : null;
                if (adobject == null)
                    return null;
            }

            object? val = AssertBound().Properties[property].Value;
            if (val == null) return null;
            try { return (Int32)val; }
            catch (InvalidCastException ex)
            {
                throw new InvalidOperationException(
                    $"Property '{property}' has an unexpected type '{val.GetType().FullName}'; expected Int32.", ex);
            }
        }

        /// <summary>
        /// Retrieves a boolean-valued AD attribute.
        /// When populated from a search result, reads from the pre-fetched cache without binding:
        /// an attribute missing from the result (no value in AD, or not requested in
        /// <see cref="DirectorySearch.PropertiesToLoad"/>) reads as absent. Call <see cref="Bind"/>
        /// first to read an attribute that was not requested.
        /// Returns <paramref name="defaultValue"/> if the attribute is absent or null.
        /// </summary>
        /// <param name="property">The LDAP attribute name.</param>
        /// <param name="defaultValue">Value to return when the attribute is not present. Defaults to <c>false</c>.</param>
        /// <returns>The boolean value, or <paramref name="defaultValue"/> if the attribute is not present.</returns>
        public bool GetBool(String property, bool defaultValue = false)
        {
            if (_cache != null)
            {
                if (_cache.TryGetValue(property, out var vals) && vals.Count > 0)
                    return vals[0] is bool b ? b : defaultValue;
                if (adobject == null)
                    return defaultValue;
            }

            object? val = AssertBound().Properties[property].Value;
            return val is bool bVal ? bVal : defaultValue;
        }

        /// <summary>
        /// Retrieves a binary (octet string) AD attribute such as <c>objectSid</c>.
        /// When populated from a search result, reads from the pre-fetched cache without binding:
        /// an attribute missing from the result (no value in AD, or not requested in
        /// <see cref="DirectorySearch.PropertiesToLoad"/>) reads as absent. Call <see cref="Bind"/>
        /// first to read an attribute that was not requested.
        /// Returns <c>null</c> if the attribute is absent or not binary.
        /// </summary>
        /// <param name="property">The LDAP attribute name.</param>
        /// <returns>The first value's bytes, or <c>null</c> if the attribute is not present.</returns>
        public byte[]? GetBytes(String property)
        {
            if (_cache != null)
            {
                if (_cache.TryGetValue(property, out var vals) && vals.Count > 0)
                    return vals[0] as byte[];
                if (adobject == null)
                    return null;
            }

            return AssertBound().Properties[property].Value as byte[];
        }

        /// <summary>
        /// Retrieves a 64-bit integer AD attribute.
        /// When populated from a search result, reads from the pre-fetched cache without binding:
        /// an attribute missing from the result (no value in AD, or not requested in
        /// <see cref="DirectorySearch.PropertiesToLoad"/>) reads as absent. Call <see cref="Bind"/>
        /// first to read an attribute that was not requested.
        /// Handles both plain <c>Int64</c> values and the <c>IADsLargeInteger</c> COM object
        /// that ADSI returns for large-integer attributes such as <c>pwdLastSet</c>.
        /// Returns <c>null</c> if the attribute is absent.
        /// </summary>
        /// <param name="property">The LDAP attribute name.</param>
        public long? GetInt64(string property)
        {
            object? val;
            if (_cache != null)
            {
                if (_cache.TryGetValue(property, out var vals) && vals.Count > 0)
                {
                    val = vals[0];
                    if (val == null) return null;
                    if (val is long l) return l;
                    if (val is int  i) return i;
                    return null; // DirectorySearcher always returns Int64 for large-integer attributes
                }
                if (adobject == null)
                    return null;
            }

            val = AssertBound().Properties[property].Value;
            if (val == null) return null;
            if (val is long lv) return lv;
            if (val is int  iv) return iv;
            // IADsLargeInteger COM object — access HighPart/LowPart via reflection
            // to avoid a compile-time dependency on the ActiveDs type library.
            try
            {
                var t = val.GetType();
                int hi = (int)t.InvokeMember("HighPart", System.Reflection.BindingFlags.GetProperty, null, val, null)!;
                int lo = (int)t.InvokeMember("LowPart",  System.Reflection.BindingFlags.GetProperty, null, val, null)!;
                return ((long)hi << 32) | (uint)lo;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read IADsLargeInteger value for property '{property}' (type: {val.GetType().FullName})", ex);
            }
        }

        /// <summary>
        /// Retrieves a Windows FILETIME attribute as a UTC <see cref="DateTime"/>.
        /// Returns <c>null</c> if the attribute is absent, zero (not set),
        /// or <see cref="long.MaxValue"/> (the AD sentinel for "never").
        /// </summary>
        /// <param name="property">The LDAP attribute name (e.g. <c>"pwdLastSet"</c>).</param>
        public DateTime? GetDateTime(string property)
        {
            long? ft = GetInt64(property);
            if (ft == null || ft == 0 || ft == long.MaxValue) return null;
            try { return DateTime.FromFileTimeUtc(ft.Value); }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert FILETIME value {ft.Value} to DateTime for property '{property}'", ex);
            }
        }

        /// <summary>
        /// Returns <c>true</c> if the specified <see cref="UACFlags"/> bit is set in the
        /// object's <c>userAccountControl</c> attribute.
        /// </summary>
        /// <param name="uacflag">The flag bit to test.</param>
        /// <returns><c>true</c> if the flag is set; <c>false</c> otherwise.</returns>
        public bool UACValue(UACFlags uacflag)
        {
            return ((GetInt32(ADProperties.UserAccountControl) ?? 0) & (int)uacflag) != 0;
        }

        /// <summary>Gets whether this account is disabled (<see cref="UACFlags.ACCOUNTDISABLE"/>).</summary>
        public bool IsDisabled          => UACValue(UACFlags.ACCOUNTDISABLE);

        /// <summary>Gets whether this account is currently locked out (<see cref="UACFlags.LOCKOUT"/>).</summary>
        public bool IsLockedOut         => UACValue(UACFlags.LOCKOUT);

        /// <summary>Gets whether this account's password is set never to expire (<see cref="UACFlags.DONT_EXPIRE_PASSWORD"/>).</summary>
        public bool PasswordNeverExpires => UACValue(UACFlags.DONT_EXPIRE_PASSWORD);

        /// <summary>Gets whether a smart card is required to log on interactively (<see cref="UACFlags.SMARTCARD_REQUIRED"/>).</summary>
        public bool SmartCardRequired   => UACValue(UACFlags.SMARTCARD_REQUIRED);

        /// <summary>Gets whether this is a user object (SchemaClass == User) without triggering a full AD bind.</summary>
        public bool IsUser()  => SchemaClass == ObjectClass.User;

        /// <summary>Gets whether this is a group object (SchemaClass == Group) without triggering a full AD bind.</summary>
        public bool IsGroup() => SchemaClass == ObjectClass.Group;

        /// <summary>
        /// Flags corresponding to the <c>userAccountControl</c> attribute bits.
        /// Pass individual values to <see cref="UACValue"/> to test whether a flag is set.
        /// </summary>
        public enum UACFlags
        {
            /// <summary>A logon script is assigned to this account.</summary>
            SCRIPT = 0x0001,
            /// <summary>The account is disabled and cannot be used to log on.</summary>
            ACCOUNTDISABLE = 0x0002,
            /// <summary>A home directory is required for this account.</summary>
            HOMEDIR_REQUIRED = 0x0008,
            /// <summary>The account is currently locked out.</summary>
            LOCKOUT = 0x0010,
            /// <summary>No password is required to log on with this account.</summary>
            PASSWD_NOTREQD = 0x0020,
            /// <summary>The user cannot change their own password.</summary>
            PASSWD_CANT_CHANGE = 0x0040,
            /// <summary>The account can use reversible password encryption.</summary>
            ENCRYPTED_TEXT_PWD_ALLOWED = 0x0080,
            /// <summary>A duplicate account in the same domain for users whose primary account is in another domain.</summary>
            TEMP_DUPLICATE_ACCOUNT = 0x0100,
            /// <summary>A normal user account (the most common account type).</summary>
            NORMAL_ACCOUNT = 0x0200,
            /// <summary>A trust account for a domain that trusts other domains.</summary>
            INTERDOMAIN_TRUST_ACCOUNT = 0x0800,
            /// <summary>A computer account for a workstation or server joined to the domain.</summary>
            WORKSTATION_TRUST_ACCOUNT = 0x1000,
            /// <summary>A computer account for a domain controller.</summary>
            SERVER_TRUST_ACCOUNT = 0x2000,
            /// <summary>The password on this account will never expire.</summary>
            DONT_EXPIRE_PASSWORD = 0x10000,
            /// <summary>An MNS (Majority Node Set) logon account used in cluster environments.</summary>
            MNS_LOGON_ACCOUNT = 0x20000,
            /// <summary>A smart card is required to log on interactively.</summary>
            SMARTCARD_REQUIRED = 0x40000,
            /// <summary>The account is trusted for Kerberos delegation (unconstrained).</summary>
            TRUSTED_FOR_DELEGATION = 0x80000,
            /// <summary>The account cannot be delegated to another account.</summary>
            NOT_DELEGATED = 0x100000,
            /// <summary>DES encryption is used for this account's Kerberos keys.</summary>
            USE_DES_KEY_ONLY = 0x200000,
            /// <summary>Kerberos pre-authentication is not required for this account.</summary>
            DONT_REQ_PREAUTH = 0x400000,
            /// <summary>The account's password has expired.</summary>
            PASSWORD_EXPIRED = 0x800000,
            /// <summary>The account is trusted to authenticate for other accounts (constrained delegation).</summary>
            TRUSTED_TO_AUTH_FOR_DELEGATION = 0x1000000
        }

        /// <summary>
        /// The AD schema class of an Active Directory object,
        /// as reported by <see cref="ADObject.SchemaClass"/>.
        /// </summary>
        public enum ObjectClass
        {
            /// <summary>An unrecognised or unsupported schema class.</summary>
            Unknown,
            /// <summary>User account (<c>objectClass=user</c>).</summary>
            User,
            /// <summary>Security or distribution group (<c>objectClass=group</c>).</summary>
            Group,
            /// <summary>Computer account (<c>objectClass=computer</c>).</summary>
            Computer,
            /// <summary>Published printer (<c>objectClass=printQueue</c>).</summary>
            Printer
        }

        /// <summary>
        /// Closes the underlying <see cref="DirectoryEntry"/> connection and releases all resources.
        /// Equivalent to calling <see cref="Dispose()"/>. Prefer a <c>using</c> block instead.
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        /// <summary>
        /// Commits any pending changes made to this object's properties back to Active Directory.
        /// </summary>
        public void Save()
        {
            AssertBound().CommitChanges();
        }

        /// <summary>
        /// Moves this object to a different organisational unit or container.
        /// The object retains its current name; only its parent changes.
        /// </summary>
        /// <param name="destinationOU">
        /// The LDAP path of the target OU or container
        /// (e.g. <c>LDAP://OU=Staff,DC=example,DC=com</c> or a bare DN).
        /// </param>
        public void Move(string destinationOU)
        {
            if (string.IsNullOrEmpty(destinationOU))
                throw new ArgumentNullException(nameof(destinationOU));
            using var destination = new DirectoryEntry(
                destinationOU.Contains("LDAP://") ? destinationOU : "LDAP://" + destinationOU);
            AssertBound().MoveTo(destination);
        }

        /// <inheritdoc/>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects)
                }

                // free unmanaged resources (unmanaged objects) and override finalizer
                adobject?.Dispose();

                // set large fields to null
                adobject = null;
                _cache = null;
                _ldapPath = null;

                disposedValue = true;
            }
        }

        /// <summary>Finalizer — ensures unmanaged resources are released if <see cref="Dispose()"/> was not called.</summary>
        ~ADObject()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        /// <summary>
        /// Releases all resources held by this object, including the underlying
        /// <see cref="DirectoryEntry"/>. Use a <c>using</c> block to call this automatically.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
