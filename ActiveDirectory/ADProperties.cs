namespace ActiveDirectory
{
    /// <summary>
    /// LDAP attribute name constants for Active Directory objects.
    /// Use these instead of raw strings when building <see cref="DirectorySearch.PropertiesToLoad"/>
    /// lists or calling <see cref="ADObject.GetProperty"/>.
    /// <para>
    /// Properties common to all object types (objectGUID, distinguishedName, etc.) are declared
    /// at the top level. Type-specific properties are in the nested <see cref="User"/>,
    /// <see cref="Group"/>, <see cref="Computer"/>, and <see cref="Printer"/> classes.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// var search = new DirectorySearch(ldapRoot, ObjectClass.User);
    /// search.PropertiesToLoad.Add(ADProperties.User.DisplayName);
    /// search.PropertiesToLoad.Add(ADProperties.User.TelephoneNumber);
    /// search.PropertiesToLoad.Add(ADProperties.ObjectGuid);
    /// </code>
    /// </example>
    public static class ADProperties
    {
        // ── Common to all object types ────────────────────────────────────────────

        /// <summary>The object's unique identifier byte array (<c>objectGUID</c>).</summary>
        public const string ObjectGuid = "objectguid";

        /// <summary>
        /// The object's schema class hierarchy (<c>objectClass</c>).
        /// Multi-valued; contains all classes from <c>top</c> down to the most derived class
        /// (e.g. <c>["top", "person", "organizationalPerson", "user"]</c> for a user account).
        /// Used by <see cref="DirectorySearch"/> to instantiate the most specific wrapper type.
        /// </summary>
        public const string ObjectClass = "objectClass";

        /// <summary>
        /// The object's coarse schema category (<c>objectCategory</c>). Filtering on
        /// <c>objectCategory=person</c> together with <c>objectClass=user</c> selects user accounts
        /// without the computer accounts that also carry <c>objectClass=user</c>.
        /// </summary>
        public const string ObjectCategory = "objectCategory";

        /// <summary>The security identifier of a user, group or computer as a byte array (<c>objectSid</c>).</summary>
        public const string ObjectSid = "objectSid";

        /// <summary>The object's fully-qualified LDAP distinguished name (<c>distinguishedName</c>).</summary>
        public const string DistinguishedName = "distinguishedName";

        /// <summary>The object's common name (<c>cn</c>).</summary>
        public const string CommonName = "cn";

        /// <summary>Distinguished names of groups this object directly belongs to (<c>memberOf</c>).</summary>
        public const string MemberOf = "memberOf";

        /// <summary>UTC timestamp when the object was created (<c>whenCreated</c>).</summary>
        public const string WhenCreated = "whenCreated";

        /// <summary>UTC timestamp when the object was last modified (<c>whenChanged</c>).</summary>
        public const string WhenChanged = "whenChanged";

        /// <summary>
        /// Bit field controlling account state flags such as disabled, locked out, and
        /// password-never-expires (<c>userAccountControl</c>).
        /// </summary>
        public const string UserAccountControl = "userAccountControl";

        // ── ADUser ────────────────────────────────────────────────────────────────

        /// <summary>
        /// LDAP attribute name constants for <see cref="ADUser"/> objects.
        /// </summary>
        public static class User
        {
            // General tab

            /// <summary>The user's display name, typically in <c>firstname.lastname@unit</c> format (<c>displayName</c>).</summary>
            public const string DisplayName = "displayName";

            /// <summary>The user's given (first) name (<c>givenName</c>).</summary>
            public const string GivenName = "givenName";

            /// <summary>The user's surname (<c>sn</c>).</summary>
            public const string Surname = "sn";

            /// <summary>The user's initials (<c>initials</c>).</summary>
            public const string Initials = "initials";

            /// <summary>The user's description (<c>description</c>).</summary>
            public const string Description = "description";

            /// <summary>The user's office location (<c>physicalDeliveryOfficeName</c>).</summary>
            public const string Office = "physicalDeliveryOfficeName";

            /// <summary>The user's primary work telephone number (<c>telephoneNumber</c>).</summary>
            public const string TelephoneNumber = "telephoneNumber";

            /// <summary>The user's email address (<c>mail</c>).</summary>
            public const string Email = "mail";

            /// <summary>The user's web page URL (<c>wWWHomePage</c>).</summary>
            public const string WebPage = "wWWHomePage";

            // Address tab

            /// <summary>The user's street address (<c>street</c>).</summary>
            public const string Street = "street";

            /// <summary>The user's PO box number (<c>postOfficeBox</c>).</summary>
            public const string POBox = "postOfficeBox";

            /// <summary>The user's city (<c>l</c>).</summary>
            public const string City = "l";

            /// <summary>The user's province or state (<c>st</c>).</summary>
            public const string Province = "st";

            /// <summary>The user's postal code (<c>postalCode</c>).</summary>
            public const string PostalCode = "postalCode";

            /// <summary>The user's country (<c>co</c>).</summary>
            public const string Country = "co";

            // Telephone tab

            /// <summary>The user's home phone number (<c>homePhone</c>).</summary>
            public const string HomePhone = "homePhone";

            /// <summary>The user's pager number (<c>pager</c>).</summary>
            public const string Pager = "pager";

            /// <summary>The user's mobile phone number (<c>mobile</c>).</summary>
            public const string MobilePhone = "mobile";

            /// <summary>The user's fax number (<c>facsimileTelephoneNumber</c>).</summary>
            public const string FaxNumber = "facsimileTelephoneNumber";

            /// <summary>The user's IP phone number (<c>ipPhone</c>).</summary>
            public const string IPPhone = "ipPhone";

            /// <summary>The user's free-text notes field (<c>notes</c>).</summary>
            public const string Notes = "notes";

            // Organization tab

            /// <summary>
            /// The user's job title (<c>title</c>).
            /// </summary>
            public const string Title = "title";

            /// <summary>The user's department (<c>department</c>).</summary>
            public const string Department = "department";

            /// <summary>The user's company (<c>company</c>).</summary>
            public const string Company = "company";

            // Additional attributes

            /// <summary>The user's employee ID (<c>employeeID</c>).</summary>
            public const string EmployeeID = "employeeID";

            /// <summary>The user's employee number (<c>employeeNumber</c>).</summary>
            public const string EmployeeNumber = "employeeNumber";

            // Account dates

            /// <summary>Windows FILETIME recording when the password was last set (<c>pwdLastSet</c>).</summary>
            public const string PasswordLastSet = "pwdLastSet";

            /// <summary>
            /// Replicated last-logon timestamp, updated across all DCs with up to a 14-day delay
            /// (<c>lastLogonTimestamp</c>).
            /// </summary>
            public const string LastLogonTimestamp = "lastLogonTimestamp";

            /// <summary>Windows FILETIME at which this account expires, or the "never" sentinel (<c>accountExpires</c>).</summary>
            public const string AccountExpires = "accountExpires";

            // Organisational relations

            /// <summary>Distinguished name of this user's manager (<c>manager</c>).</summary>
            public const string Manager = "manager";

            // AD account settings

            /// <summary>Path to the logon script executed at sign-in (<c>scriptPath</c>).</summary>
            public const string LogonScript = "scriptPath";

            /// <summary>UNC path to the user's home directory (<c>homeDirectory</c>).</summary>
            public const string HomeDirectory = "homeDirectory";

            /// <summary>SAM account name (pre-Windows 2000 logon name) (<c>sAMAccountName</c>).</summary>
            public const string SamAccountName = "sAMAccountName";

            /// <summary>UNC path to the user's roaming profile directory (<c>profilePath</c>).</summary>
            public const string ProfilePath = "profilePath";

            /// <summary>Distinguished name of the Exchange MTA server group for this mailbox (<c>homeMTA</c>).</summary>
            public const string ExchangeServerGroup = "homemta";
        }

        // ── ADGroup ───────────────────────────────────────────────────────────────

        /// <summary>
        /// LDAP attribute name constants for <see cref="ADGroup"/> objects.
        /// </summary>
        public static class Group
        {
            /// <summary>The group's description (<c>description</c>).</summary>
            public const string Description = "description";

            /// <summary>The group's free-text notes/info field (<c>info</c>).</summary>
            public const string Notes = "info";

            /// <summary>Distinguished name of the object designated as the group's manager (<c>managedBy</c>).</summary>
            public const string ManagedBy = "managedBy";

            /// <summary>The group's SAM account name (<c>sAMAccountName</c>).</summary>
            public const string SamAccountName = "sAMAccountName";

            /// <summary>
            /// Multi-valued attribute listing the distinguished names of all direct members
            /// (<c>member</c>). Used when adding or removing members programmatically.
            /// </summary>
            public const string Member = "member";
        }

        // ── ADComputer ────────────────────────────────────────────────────────────

        /// <summary>
        /// LDAP attribute name constants for <see cref="ADComputer"/> objects.
        /// </summary>
        public static class Computer
        {
            /// <summary>The computer object's description (<c>description</c>).</summary>
            public const string Description = "description";

            /// <summary>The computer's fully-qualified DNS host name (<c>dNSHostName</c>).</summary>
            public const string DnsHostName = "dNSHostName";
        }

        // ── ADPrinter ─────────────────────────────────────────────────────────────

        /// <summary>
        /// LDAP attribute name constants for <see cref="ADPrinter"/> objects.
        /// </summary>
        public static class Printer
        {
            /// <summary>The printer's published name as shown to users (<c>printerName</c>).</summary>
            public const string PrinterName = "printerName";

            /// <summary>The printer object's description (<c>description</c>).</summary>
            public const string Description = "description";

            /// <summary>The physical location of the printer as published in the directory (<c>location</c>).</summary>
            public const string Location = "location";

            /// <summary>The fully-qualified name of the print server hosting this printer (<c>serverName</c>).</summary>
            public const string ServerName = "serverName";

            /// <summary>The NetBIOS (short) name of the print server (<c>shortServerName</c>).</summary>
            public const string ServerShortName = "shortServerName";

            /// <summary>The port name used by the print server to communicate with the printer (<c>portName</c>).</summary>
            public const string PortName = "portName";

            /// <summary>The name of the printer driver installed on the print server (<c>driverName</c>).</summary>
            public const string DriverName = "driverName";

            /// <summary>The version number of the printer driver (<c>driverVersion</c>).</summary>
            public const string DriverVersion = "driverVersion";

            /// <summary>The list of paper-bin names available on this printer (<c>printBinNames</c>).</summary>
            public const string BinNames = "printBinNames";

            /// <summary>The maximum print resolution supported by this printer in DPI (<c>printMaxResolutionSupported</c>).</summary>
            public const string MaxResolutionSupported = "printMaxResolutionSupported";

            /// <summary>The page orientations this printer supports, e.g. <c>PORTRAIT</c>, <c>LANDSCAPE</c> (<c>printOrientationsSupported</c>).</summary>
            public const string OrientationsSupported = "printOrientationsSupported";

            /// <summary>Whether this printer supports output collation (<c>printCollate</c>).</summary>
            public const string Collate = "printCollate";

            /// <summary>Whether this printer supports colour printing (<c>printColor</c>).</summary>
            public const string Color = "printColor";

            /// <summary>The page-description language used by this printer, such as PCL or PostScript (<c>printLanguage</c>).</summary>
            public const string Language = "printLanguage";

            /// <summary>The network share name used to connect to this printer (<c>printShareName</c>).</summary>
            public const string ShareName = "printShareName";

            /// <summary>The spooling mode for this printer (<c>printSpooling</c>).</summary>
            public const string Spooling = "printSpooling";

            /// <summary>Whether completed print jobs are retained in the spooler after printing (<c>printKeepPrintedJobs</c>).</summary>
            public const string KeepPrintedJobs = "printKeepPrintedJobs";

            /// <summary>Whether this printer has a built-in stapler (<c>printStaplingSupported</c>).</summary>
            public const string StaplingSupported = "printStaplingSupported";

            /// <summary>The amount of printer memory in kilobytes (<c>printMemory</c>).</summary>
            public const string Memory = "printMemory";

            /// <summary>The list of media types currently loaded in the printer (<c>printMediaReady</c>).</summary>
            public const string MediaReady = "printMediaReady";

            /// <summary>The list of all media types this printer is capable of handling (<c>printMediaSupported</c>).</summary>
            public const string MediaSupported = "printMediaSupported";

            /// <summary>The rated print speed in pages per minute (<c>printPagesPerMinute</c>).</summary>
            public const string PagesPerMinute = "printPagesPerMinute";

            /// <summary>Whether this printer supports duplex (double-sided) printing (<c>printDuplexSupported</c>).</summary>
            public const string DuplexSupported = "printDuplexSupported";
        }
    }
}
