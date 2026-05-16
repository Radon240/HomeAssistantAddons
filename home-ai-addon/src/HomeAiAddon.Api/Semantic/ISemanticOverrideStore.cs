namespace HomeAiAddon.Api.Semantic;

public interface ISemanticOverrideStore
{
    Task<IReadOnlyList<SemanticOverrideEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticOverrideEntry>> UpsertAsync(
        UpsertSemanticOverrideRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticOverrideEntry>> DeleteAsync(
        string entityId,
        CancellationToken cancellationToken = default);
}
