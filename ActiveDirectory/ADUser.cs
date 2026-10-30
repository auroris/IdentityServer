using System;
using System.DirectoryServices;

namespace ActiveDirectory
{
    /// <summary>
    /// Represents an Active Directory user account.
    /// Provides typed accessors for all standard user attributes (General, Address, Telephone,
    /// Organization tabs), and Active Directory account settings.
    /// Inherits GUID support, safe property access, and LDAP helpers from <see cref="ADObject"/>.
    /// </summary>
    public class ADUser : ADObject
    {
        /// <summary>Parameterless constructor for use when <see cref="ADObject.adobject"/> is set separately.</summary>
        public ADUser() { }

        /// <summary>
        /// Constructs an <see cref="ADUser"/> from a SAM account name, domain-qualified logon name,
        /// or full LDAP path.
        /// </summary>
        /// <param name="userName">A SAM account name, <c>DOMAIN\username</c>, or <c>LDAP://...</c> path.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="userName"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown if no matching user is found in Active Directory.</exception>
        public ADUser(String userName)
        {
            if (String.IsNullOrEmpty(userName))
                throw new ArgumentNullException(nameof(userName));

            if (userName.Contains("LDAP://"))
            {
                if (!DirectoryEntry.Exists(userName))
                    throw new InvalidOperationException($"No Active Directory user was found at path '{userName}'.");
                base.adobject = new DirectoryEntry(userName);
            }
            else
            {
                string? ldapRoot = null;
                if (userName.Contains("\\"))
                {
                    var parts = userName.Split('\\');
                    ldapRoot = $"LDAP://{parts[0]}";
                    userName = parts[1];
                }

                var search = new DirectorySearch(ldapRoot, ObjectClass.User);
                search.PropertiesToSearch.Add(ADProperties.User.SamAccountName, userName);
                search.PropertiesToLoad.Add(ADProperties.CommonName);
                base.adobject = search.FindOneEntry();
                if (base.adobject == null)
                    throw new InvalidOperationException($"User '{userName}' was not found in Active Directory.");
            }
        }

        /// <summary>Constructs an <see cref="ADUser"/> from its AD GUID.</summary>
        /// <param name="guid">The user object's AD GUID.</param>
        public ADUser(ADGuid guid) : base(guid) { }

        /// <summary>Constructs an <see cref="ADUser"/> wrapping an existing <see cref="DirectoryEntry"/>.</summary>
        /// <param name="entry">An already-opened <see cref="DirectoryEntry"/> for a user object.</param>
        public ADUser(DirectoryEntry entry) : base(entry) { }

        #region General Tab
        /// <summary>Gets or sets the user's given name.</summary>
        public String GivenName
        {
            get { return GetProperty(ADProperties.User.GivenName); }
            set { SetProperty(ADProperties.User.GivenName, value); }
        }

        /// <summary>Gets or sets the user's last name.</summary>
        public String Surname
        {
            get { return GetProperty(ADProperties.User.Surname); }
            set { SetProperty(ADProperties.User.Surname, value); }
        }

        /// <summary>Gets or sets the user's initials.</summary>
        public String Initials
        {
            get { return GetProperty(ADProperties.User.Initials); }
            set { SetProperty(ADProperties.User.Initials, value); }
        }

        /// <summary>Gets or sets the user's display name.</summary>
        public string DisplayName
        {
            get { return GetProperty(ADProperties.User.DisplayName); }
            set { SetProperty(ADProperties.User.DisplayName, value); }
        }

        /// <summary>Gets or sets the user's description.</summary>
        public String Description
        {
            get { return GetProperty(ADProperties.User.Description); }
            set { SetProperty(ADProperties.User.Description, value); }
        }

        /// <summary>Gets or sets the user's office location.</summary>
        public String Office
        {
            get { return GetProperty(ADProperties.User.Office); }
            set { SetProperty(ADProperties.User.Office, value); }
        }

        /// <summary>Gets or sets the user's telephone number.</summary>
        public String TelephoneNumber
        {
            get { return GetProperty(ADProperties.User.TelephoneNumber); }
            set { SetProperty(ADProperties.User.TelephoneNumber, value); }
        }

        /// <summary>Gets or sets the user's email address.</summary>
        public String Email
        {
            get { return GetProperty(ADProperties.User.Email); }
            set { SetProperty(ADProperties.User.Email, value); }
        }

        /// <summary>Gets or sets the user's web page.</summary>
        public String WebPage
        {
            get { return GetProperty(ADProperties.User.WebPage); }
            set { SetProperty(ADProperties.User.WebPage, value); }
        }
        #endregion

        #region Address Tab
        /// <summary>Gets or sets the user's street address.</summary>
        public String Street
        {
            get { return GetProperty(ADProperties.User.Street); }
            set { SetProperty(ADProperties.User.Street, value); }
        }

        /// <summary>Gets or sets the user's PO box.</summary>
        public String POBox
        {
            get { return GetProperty(ADProperties.User.POBox); }
            set { SetProperty(ADProperties.User.POBox, value); }
        }

        /// <summary>Gets or sets the user's city.</summary>
        public String City
        {
            get { return GetProperty(ADProperties.User.City); }
            set { SetProperty(ADProperties.User.City, value); }
        }

        /// <summary>Gets or sets the user's province or state.</summary>
        public String Province
        {
            get { return GetProperty(ADProperties.User.Province); }
            set { SetProperty(ADProperties.User.Province, value); }
        }

        /// <summary>Gets or sets the user's postal code.</summary>
        public String PostalCode
        {
            get { return GetProperty(ADProperties.User.PostalCode); }
            set { SetProperty(ADProperties.User.PostalCode, value); }
        }

        /// <summary>Gets or sets the user's country.</summary>
        public String Country
        {
            get { return GetProperty(ADProperties.User.Country); }
            set { SetProperty(ADProperties.User.Country, value); }
        }
        #endregion

        #region Telephone Tab
        /// <summary>Gets or sets the user's home phone number.</summary>
        public String HomePhone
        {
            get { return GetProperty(ADProperties.User.HomePhone); }
            set { SetProperty(ADProperties.User.HomePhone, value); }
        }

        /// <summary>Gets or sets the user's pager number.</summary>
        public String Pager
        {
            get { return GetProperty(ADProperties.User.Pager); }
            set { SetProperty(ADProperties.User.Pager, value); }
        }

        /// <summary>Gets or sets the user's mobile phone number.</summary>
        public String MobilePhone
        {
            get { return GetProperty(ADProperties.User.MobilePhone); }
            set { SetProperty(ADProperties.User.MobilePhone, value); }
        }

        /// <summary>Gets or sets the user's fax number.</summary>
        public String FaxNumber
        {
            get { return GetProperty(ADProperties.User.FaxNumber); }
            set { SetProperty(ADProperties.User.FaxNumber, value); }
        }

        /// <summary>Gets or sets the user's IP Phone number.</summary>
        public String IPPhone
        {
            get { return GetProperty(ADProperties.User.IPPhone); }
            set { SetProperty(ADProperties.User.IPPhone, value); }
        }

        /// <summary>Gets or sets the user's notes field.</summary>
        public String Notes
        {
            get { return GetProperty(ADProperties.User.Notes); }
            set { SetProperty(ADProperties.User.Notes, value); }
        }
        #endregion

        #region Organization Tab
        /// <summary>Gets or sets the user's title.</summary>
        public String Title
        {
            get { return GetProperty(ADProperties.User.Title); }
            set { SetProperty(ADProperties.User.Title, value); }
        }

        /// <summary>Gets or sets the user's department.</summary>
        public String Department
        {
            get { return GetProperty(ADProperties.User.Department); }
            set { SetProperty(ADProperties.User.Department, value); }
        }

        /// <summary>Gets or sets the user's company.</summary>
        public String Company
        {
            get { return GetProperty(ADProperties.User.Company); }
            set { SetProperty(ADProperties.User.Company, value); }
        }
        #endregion

        #region Additional Attributes
        /// <summary>Gets or sets the user's employee ID (the <c>employeeID</c> attribute).</summary>
        public String EmployeeID
        {
            get { return GetProperty(ADProperties.User.EmployeeID); }
            set { SetProperty(ADProperties.User.EmployeeID, value); }
        }

        /// <summary>Gets or sets the user's employee number (the <c>employeeNumber</c> attribute).</summary>
        public String EmployeeNumber
        {
            get { return GetProperty(ADProperties.User.EmployeeNumber); }
            set { SetProperty(ADProperties.User.EmployeeNumber, value); }
        }
        #endregion

        #region Account Dates
        /// <summary>
        /// Gets the UTC date and time the user's password was last set (the <c>pwdLastSet</c> attribute),
        /// or <c>null</c> if the password has never been set or must be changed at next logon.
        /// </summary>
        public DateTime? PasswordLastSet
        {
            get { return GetDateTime(ADProperties.User.PasswordLastSet); }
        }

        /// <summary>
        /// Gets the UTC date and time of the user's most recent logon, replicated across all domain
        /// controllers (the <c>lastLogonTimestamp</c> attribute), or <c>null</c> if never logged on.
        /// Note: replication of this attribute is intentionally delayed by up to 14 days by default.
        /// </summary>
        public DateTime? LastLogonTimestamp
        {
            get { return GetDateTime(ADProperties.User.LastLogonTimestamp); }
        }

        /// <summary>
        /// Gets the UTC date and time at which this account expires (the <c>accountExpires</c> attribute),
        /// or <c>null</c> if the account never expires.
        /// </summary>
        public DateTime? AccountExpires
        {
            get { return GetDateTime(ADProperties.User.AccountExpires); }
        }
        #endregion

        #region Manager
        /// <summary>
        /// Gets the AD object designated as this user's manager (the <c>manager</c> attribute).
        /// Returns <c>null</c> if no manager is set.
        /// Check <see cref="ADObject.SchemaClass"/> on the returned object to determine its type.
        /// </summary>
        public ADObject? Manager
        {
            get
            {
                String dn = GetProperty(ADProperties.User.Manager);
                return dn.Length > 0 ? new ADObject(dn) : null;
            }
        }
        #endregion

        #region Active Directory Settings
        /// <summary>Gets or sets the path to the logon script executed when the user signs in (the <c>scriptPath</c> attribute).</summary>
        public String LogonScript
        {
            get { return GetProperty(ADProperties.User.LogonScript); }
            set { SetProperty(ADProperties.User.LogonScript, value); }
        }

        /// <summary>Gets or sets the UNC path to the user's home directory (the <c>homeDirectory</c> attribute).</summary>
        public String HomeDirectory
        {
            get { return GetProperty(ADProperties.User.HomeDirectory); }
            set { SetProperty(ADProperties.User.HomeDirectory, value); }
        }

        /// <summary>Gets the user's SAM account name (pre-Windows 2000 logon name), from the <c>sAMAccountName</c> attribute.</summary>
        public String Username
        {
            get { return GetProperty(ADProperties.User.SamAccountName); }
        }

        /// <summary>
        /// Gets the distinguished name of the Exchange MTA (Message Transfer Agent) server group
        /// this user's mailbox is homed on (the <c>homeMTA</c> attribute).
        /// </summary>
        public String ExchangeServerGroup
        {
            get { return GetProperty(ADProperties.User.ExchangeServerGroup); }
        }

        /// <summary>Gets the UNC path to the user's roaming profile directory (the <c>profilePath</c> attribute).</summary>
        public String ProfilePath
        {
            get { return GetProperty(ADProperties.User.ProfilePath); }
        }
        #endregion

    }
}
