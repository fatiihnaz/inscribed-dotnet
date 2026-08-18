using System.Security.Claims;
using Inscribed.Application.Contracts.Schemas;

namespace Inscribed.Application.Services.Helpers;

public static class AccessRuleEvaluator
{
    public static bool Allows(AccessRule? rule, ClaimsPrincipal user)
    {
        if (rule is null)
            return true;

        return rule.Combine == AccessCombine.All
            ? rule.Leaves.All(leaf => Matches(leaf, user))
            : rule.Leaves.Any(leaf => Matches(leaf, user));
    }

    private static bool Matches(AccessLeaf leaf, ClaimsPrincipal user)
    {
        var values = user.FindAll(leaf.Claim).Select(claim => claim.Value).ToArray();

        return leaf.Match switch
        {
            AccessMatch.Present => values.Length > 0 == leaf.Present,
            AccessMatch.Equals => values.Contains(leaf.Values[0], StringComparer.Ordinal),
            AccessMatch.AnyOf => values.Any(value => leaf.Values.Contains(value, StringComparer.Ordinal)),
            AccessMatch.AllOf => leaf.Values.All(required => values.Contains(required, StringComparer.Ordinal)),
            _ => false,
        };
    }
}
