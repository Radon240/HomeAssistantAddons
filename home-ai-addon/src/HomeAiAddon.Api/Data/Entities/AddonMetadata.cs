namespace HomeAiAddon.Api.Data.Entities;

public sealed class AddonMetadata
{
    public int Id { get; set; }

    public string SchemaVersion { get; set; } = "1";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
