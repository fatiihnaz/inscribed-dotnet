using System.Text.RegularExpressions;

namespace Inscribed.Application.Contracts.Schemas;

public sealed record ClaimSlugRule(
    string Claim,
    string? EndsWith = null,
    string? StartsWith = null,
    Regex? Pattern = null
);
