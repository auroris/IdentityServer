using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using static ActiveDirectory.ADObject;

namespace ActiveDirectory
{
    /// <summary>
    /// A fluent builder for <see cref="DirectorySearcher"/> queries.
    /// Construct with an optional LDAP root and an <see cref="ObjectClass"/>,
    /// add attribute/value pairs to <see cref="PropertiesToSearch"/> (all OR'd together),
    /// then call <see cref="Find{T}"/> or <see cref="FindOne{T}"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var search = new DirectorySearch(ldapRoot, ObjectClass.User);
    /// search.PropertiesToSearch.Add("displayName", query, DirectorySearch.MatchType.StartsWith);
    /// search.PropertiesToSearch.Add("givenName",   query, DirectorySearch.MatchType.StartsWith);
    /// search.PropertiesToLoad.Add("displayName");
    /// List&lt;ADUser&gt; users = search.Find&lt;ADUser&gt;();
    /// // Dispose each returned object when finished; it may own a live directory connection.
    /// </code>
    /// </example>
    public class DirectorySearch
    {
        /// <summary>Controls how a filter value is matched against an attribute.</summary>
        public enum MatchType
        {
            /// <summary>The attribute must exactly equal the value: <c>(attr=value)</c>.</summary>
            Exact,
            /// <summary>The attribute must start with the value: <c>(attr=value*)</c>.</summary>
            StartsWith,
            /// <summary>
            /// The attribute must contain the value anywhere: <c>(attr=*value*)</c>.
            /// Note: leading-wildcard searches are not indexed and may be slow on large directories.
            /// </summary>
            Contains
        }

        private readonly string? _ldapRoot;
        private readonly ObjectClass _objectClass;

        /// <summary>
        /// Attribute/value filter pairs to OR together in the search filter.
        /// All entries are combined with a logical OR; the <see cref="ActiveDirectory.ObjectClass"/> filter
        /// is always AND'd around the result.
        /// </summary>
        public SearchFilterCollection PropertiesToSearch { get; } = new();

        /// <summary>
        /// Attribute names to pre-fetch from the directory. When non-empty, only the listed
        /// attributes are retrieved in the initial query; others read as absent on the returned
        /// objects until <see cref="ADObject.Bind"/> is called.
        /// </summary>
        public List<string> PropertiesToLoad { get; } = new();

        /// <summary>
        /// An additional raw LDAP filter assertion AND'd with the object-class filter and any
        /// <see cref="PropertiesToSearch"/> entries (e.g. <c>memberOf=&lt;GUID=...&gt;</c>).
        /// The value must already be a valid, fully-formed LDAP assertion; it is NOT sanitised
        /// by <see cref="LdapHelper.CleanLDAPString"/>. Do not use for untrusted user input —
        /// use <see cref="PropertiesToSearch"/> for that.
        /// </summary>
        public string? AdditionalFilter { get; set; }

        /// <summary>Creates a search scoped to the entire domain.</summary>
        /// <param name="objectClass">The type of AD object to search for.</param>
        public DirectorySearch(ObjectClass objectClass) : this(null, objectClass) { }

        /// <summary>Creates a search rooted at a specific LDAP path.</summary>
        /// <param name="ldapRoot">
        /// A full LDAP path (e.g. <c>LDAP://OU=Users,DC=example,DC=com</c>),
        /// or <c>null</c> to use the domain default.
        /// </param>
        /// <param name="objectClass">The type of AD object to search for.</param>
        public DirectorySearch(string? ldapRoot, ObjectClass objectClass)
        {
            _ldapRoot = ldapRoot;
            _objectClass = objectClass;
        }

        private string BuildFilter()
        {
            string classFilter = _objectClass switch
            {
                ObjectClass.User     => "objectClass=user",
                ObjectClass.Group    => "objectClass=group",
                ObjectClass.Computer => "objectClass=computer",
                ObjectClass.Printer  => "objectClass=printQueue",
                _                    => "objectClass=*"
            };

            var andTerms = new List<string> { $"({classFilter})" };

            if (AdditionalFilter != null)
                andTerms.Add($"({AdditionalFilter})");

            if (PropertiesToSearch.Count > 0)
            {
                string attrParts = string.Concat(PropertiesToSearch.Items.Select(f =>
                {
                    string v = LdapHelper.CleanLDAPString(f.Value);
                    return f.Match switch
                    {
                        MatchType.Contains   => $"({f.Attribute}=*{v}*)",
                        MatchType.StartsWith => $"({f.Attribute}={v}*)",
                        _                    => $"({f.Attribute}={v})"
                    };
                }));
                andTerms.Add(PropertiesToSearch.Count == 1 ? attrParts : $"(|{attrParts})");
            }

            return andTerms.Count == 1 ? andTerms[0] : $"(&{string.Concat(andTerms)})";
        }

        /// <summary>
        /// Number of results to retrieve per server round-trip when using paging.
        /// Set to a positive value (e.g. <c>1000</c>) to enable server-side paging, which is
        /// required to reliably retrieve more than the server's default result limit (typically 1000).
        /// Defaults to <c>0</c> (no paging).
        /// </summary>
        public int PageSize { get; set; } = 0;

        private DirectorySearcher CreateSearcher(DirectoryEntry? root)
        {
            var searcher = root != null
                ? new DirectorySearcher(root, BuildFilter())
                : new DirectorySearcher(BuildFilter());

            foreach (string prop in PropertiesToLoad)
                searcher.PropertiesToLoad.Add(prop);

            // Always include objectguid (stable GUID-based binding) and objectClass (type dispatch).
            if (!PropertiesToLoad.Contains(ADProperties.ObjectGuid))
                searcher.PropertiesToLoad.Add(ADProperties.ObjectGuid);
            if (!PropertiesToLoad.Contains(ADProperties.ObjectClass))
                searcher.PropertiesToLoad.Add(ADProperties.ObjectClass);

            if (PageSize > 0)
                searcher.PageSize = PageSize;

            return searcher;
        }

        /// <summary>
        /// Creates the most specific <see cref="ADObject"/> subtype for a search result by
        /// inspecting the multi-valued <c>objectClass</c> attribute.
        /// Throws <see cref="InvalidOperationException"/> if the detected type is not assignable
        /// to <typeparamref name="T"/> — this indicates a bug in the search filter or a
        /// misconfigured directory, and should not be silently swallowed.
        /// </summary>
        private static T CreateSpecificObject<T>(SearchResult result) where T : ADObject, new()
        {
            var classes = result.Properties[ADProperties.ObjectClass];
            if (classes != null && classes.Count > 0)
            {
                bool hasComputer   = false;
                bool hasPrintQueue = false;
                bool hasUser       = false;
                bool hasGroup      = false;

                foreach (object? val in classes)
                {
                    if (val is string s)
                        switch (s)
                        {
                            case "computer":   hasComputer   = true; break;
                            case "printQueue": hasPrintQueue = true; break;
                            case "user":       hasUser       = true; break;
                            case "group":      hasGroup      = true; break;
                        }
                }

                // computer must be checked before user — it inherits from user in the AD schema,
                // so a computer result has both "user" and "computer" in its objectClass list.
                ADObject specific =
                    hasComputer   ? new ADComputer() :
                    hasPrintQueue ? new ADPrinter()  :
                    hasUser       ? new ADUser()      :
                    hasGroup      ? new ADGroup()     :
                                    new ADObject();

                if (specific is T typed) return typed;

                throw new InvalidOperationException(
                    $"Search returned an object of type '{specific.GetType().Name}' " +
                    $"but the search was declared for '{typeof(T).Name}'. " +
                    $"AD path: {result.Path}");
            }

            // objectClass was absent — directory data is malformed.
            throw new InvalidOperationException(
                $"Search result has no objectClass attribute and cannot be typed. AD path: {result.Path}");
        }

        /// <summary>
        /// Executes the search and returns matching objects, using the most specific available
        /// wrapper type for each result (<see cref="ADUser"/>, <see cref="ADGroup"/>,
        /// <see cref="ADComputer"/>, or <see cref="ADPrinter"/>).
        /// When <typeparamref name="T"/> is <see cref="ADObject"/>, the returned list may contain
        /// a mix of subtypes. When <typeparamref name="T"/> is a concrete subtype, a result whose
        /// detected type is not assignable to <typeparamref name="T"/> throws
        /// <see cref="InvalidOperationException"/>
        /// </summary>
        /// <param name="howMany">
        /// Maximum number of results to return. Pass <c>null</c> (the default) to return all matches.
        /// Sets <see cref="DirectorySearcher.SizeLimit"/> on the underlying searcher, so the limit
        /// is enforced server-side.
        /// The server chooses which matches fall within this limit. Sorting the returned list
        /// afterwards only sorts that subset; it does not select the first names in the entire domain.
        /// </param>
        public List<T> Find<T>(int? howMany = null) where T : ADObject, new()
        {
            var list = new List<T>();
            DirectoryEntry? searchRoot = _ldapRoot != null ? new DirectoryEntry(_ldapRoot) : null;
            try
            {
                using var searcher = CreateSearcher(searchRoot);
                if (howMany.HasValue)
                    searcher.SizeLimit = howMany.Value;
                // The collection owns native directory resources. Each wrapper copies its loaded
                // fields before disposal, so reading those fields later does not keep this search open.
                using var results = searcher.FindAll();
                foreach (SearchResult result in results)
                {
                    var obj = CreateSpecificObject<T>(result);
                    obj.SetFromResult(result);
                    list.Add(obj);
                }
            }
            finally
            {
                searchRoot?.Dispose();
            }
            return list;
        }

        /// <summary>
        /// Executes the search and returns the <see cref="DirectoryEntry"/> of the first matching
        /// object, or <c>null</c> if no match is found. The caller takes ownership of the returned
        /// entry and is responsible for disposing it.
        /// </summary>
        public DirectoryEntry? FindOneEntry()
        {
            DirectoryEntry? searchRoot = _ldapRoot != null ? new DirectoryEntry(_ldapRoot) : null;
            try
            {
                using var searcher = CreateSearcher(searchRoot);
                SearchResult? result = searcher.FindOne();
                return result?.GetDirectoryEntry();
            }
            finally
            {
                searchRoot?.Dispose();
            }
        }

        /// <summary>
        /// Executes the search and returns the first matching object using the most specific
        /// available wrapper type, or <c>null</c> if no match is found.
        /// See <see cref="Find{T}"/> for type-dispatch behaviour.
        /// </summary>
        public T? FindOne<T>() where T : ADObject, new()
        {
            DirectoryEntry? searchRoot = _ldapRoot != null ? new DirectoryEntry(_ldapRoot) : null;
            try
            {
                using var searcher = CreateSearcher(searchRoot);
                SearchResult? result = searcher.FindOne();
                if (result == null) return null;
                var obj = CreateSpecificObject<T>(result);
                obj.SetFromResult(result);
                return obj;
            }
            finally
            {
                searchRoot?.Dispose();
            }
        }

        /// <summary>
        /// A collection of attribute/value/match-type filter entries used by
        /// <see cref="PropertiesToSearch"/>. All entries are OR'd together in the search filter.
        /// </summary>
        public class SearchFilterCollection
        {
            private readonly List<SearchFilter> _items = new();

            /// <summary>Adds an exact-match filter: <c>(attribute=value)</c>.</summary>
            public void Add(string attribute, string value) =>
                _items.Add(new SearchFilter(attribute, value, MatchType.Exact));

            /// <summary>Adds a filter with the specified <paramref name="matchType"/>.</summary>
            public void Add(string attribute, string value, MatchType matchType) =>
                _items.Add(new SearchFilter(attribute, value, matchType));

            internal IEnumerable<SearchFilter> Items => _items;
            internal int Count => _items.Count;
        }

        internal record SearchFilter(string Attribute, string Value, MatchType Match);
    }
}
