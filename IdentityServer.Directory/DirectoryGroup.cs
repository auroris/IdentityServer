using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace IdentityServer.Directory;

/// <summary>An Active Directory group as group searches and lookups return it.</summary>
public record DirectoryGroup
{
    /// <summary>The group's security identifier (<c>objectSid</c>).</summary>
    public required string Sid { get; init; }

    /// <summary>The group's <c>objectGUID</c> in the standard 36-character form.</summary>
    public required string Guid { get; init; }

    /// <summary>The group's common name (<c>cn</c>), the name role claims and access checks use.</summary>
    public required string Name { get; init; }

    /// <summary>The group's description (<c>description</c>).</summary>
    public string? Description { get; init; }
}

/// <summary>A <see cref="DirectoryGroup"/> with its direct members, split by kind.</summary>
public sealed record DirectoryGroupDetail : DirectoryGroup
{
    /// <summary>Creates an empty detail record; used by serializers and object initializers.</summary>
    public DirectoryGroupDetail()
    {
    }

    /// <summary>Creates a detail record from a group summary; set the member lists with an initializer.</summary>
    /// <param name="group">The group's summary record; every property is copied.</param>
    [SetsRequiredMembers]
    public DirectoryGroupDetail(DirectoryGroup group)
        : base(group)
    {
    }

    /// <summary>User accounts that are direct members, sorted by name.</summary>
    public IReadOnlyList<DirectoryMember> MemberUsers { get; init; } = [];

    /// <summary>Groups that are direct members, sorted by name.</summary>
    public IReadOnlyList<DirectoryMember> MemberGroups { get; init; } = [];

    /// <summary>Computer accounts that are direct members, sorted by name.</summary>
    public IReadOnlyList<DirectoryMember> MemberComputers { get; init; } = [];

    /// <summary>
    /// Whether the group has more members than <see cref="DirectoryOptions.MaxGroupMembers"/>, in
    /// which case the lists hold only the first ones the directory returned.
    /// </summary>
    public bool MembersTruncated { get; init; }
}

/// <summary>One direct member of a group.</summary>
public sealed record DirectoryMember
{
    /// <summary>Value of <see cref="Type"/> for a user account.</summary>
    public const string UserType = "user";

    /// <summary>Value of <see cref="Type"/> for a group.</summary>
    public const string GroupType = "group";

    /// <summary>Value of <see cref="Type"/> for a computer account.</summary>
    public const string ComputerType = "computer";

    /// <summary>The member's <c>objectGUID</c> in the standard 36-character form.</summary>
    public required string Guid { get; init; }

    /// <summary>The member's security identifier, when it has one.</summary>
    public string? Sid { get; init; }

    /// <summary>The display name of a user; the common name of a group or computer.</summary>
    public required string Name { get; init; }

    /// <summary><c>user</c>, <c>group</c> or <c>computer</c>.</summary>
    public required string Type { get; init; }
}

/// <summary>A group and the people in it, for an address-book style group listing.</summary>
public sealed record DirectoryGroupListing
{
    /// <summary>The group that was listed.</summary>
    public required DirectoryGroup Group { get; init; }

    /// <summary>When the group object last changed (<c>whenChanged</c>), when the directory returned it.</summary>
    public DateTimeOffset? Modified { get; init; }

    /// <summary>The members who pass the listing rules, sorted by display name.</summary>
    public required IReadOnlyList<DirectoryUser> Members { get; init; }
}
