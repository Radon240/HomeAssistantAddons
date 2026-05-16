namespace HomeAiAddon.Api.Semantic;

public sealed record SemanticOverrideEntry(
    string EntityId,
    string? Role,
    string? Intent,
    bool? CanTrigger,
    bool? CanAction,
    bool? Noisy,
    bool? Significant,
    bool? SystemEvent,
    string? Reason);

public sealed record SemanticOverridesResponse(IReadOnlyList<SemanticOverrideEntry> Overrides);

public sealed record UpsertSemanticOverrideRequest(
    string EntityId,
    string? Role,
    string? Intent,
    bool? CanTrigger,
    bool? CanAction,
    bool? Noisy,
    bool? Significant,
    bool? SystemEvent,
    string? Reason);

internal sealed record SemanticOverrideFile(Dictionary<string, SemanticOverrideFileEntry> Entities);

internal sealed record SemanticOverrideFileEntry(
    string? Role,
    string? Intent,
    bool? CanTrigger,
    bool? CanAction,
    bool? Noisy,
    bool? Significant,
    bool? SystemEvent,
    string? Reason);
