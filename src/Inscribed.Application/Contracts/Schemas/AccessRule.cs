namespace Inscribed.Application.Contracts.Schemas;

public enum AccessMatch
{
    AnyOf,
    AllOf,
    Equals,
    Present,
}

public enum AccessCombine
{
    All,
    Any,
}

public sealed record AccessLeaf(string Claim, AccessMatch Match, IReadOnlyList<string> Values, bool Present);

public sealed record AccessRule(AccessCombine Combine, IReadOnlyList<AccessLeaf> Leaves);

public sealed record CollectionAccess(AccessRule? Read, AccessRule? Create, AccessRule? Write);
