using System.Text.Json.Serialization;

namespace Inscribed.Application.Contracts.Responses;

public sealed record SyncSlugResult(
    string Slug,
    int Created,
    int Deleted,
    int Unchanged,
    int Restored,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Reseeded = null
);
