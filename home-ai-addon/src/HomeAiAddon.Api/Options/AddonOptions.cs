using System.ComponentModel.DataAnnotations;

namespace HomeAiAddon.Api.Options;

public sealed class AddonOptions
{
    [ConfigurationKeyName("display_name")]
    [StringLength(120)]
    public string DisplayName { get; set; } = "Home AI Addon";

    [ConfigurationKeyName("enable_verbose_api")]
    public bool EnableVerboseApi { get; set; }

    /// <summary>Фильтр entity_id (точное совпадение или префикс с *, напр. light.living_room, sensor.*).</summary>
    [ConfigurationKeyName("entity_filter")]
    public List<string> EntityFilter { get; set; } = [];

    /// <summary>Фильтр по domain (напр. light, sensor, light.*).</summary>
    [ConfigurationKeyName("domain_filter")]
    public List<string> DomainFilter { get; set; } = [];

    /// <summary>Исключить entity из ML-анализа (sensor.*, device_tracker.phone).</summary>
    [ConfigurationKeyName("analysis_exclude_entities")]
    public List<string> AnalysisExcludeEntities { get; set; } = [];

    /// <summary>Исключить domain из ML-анализа (sensor, sun, weather).</summary>
    [ConfigurationKeyName("analysis_exclude_domains")]
    public List<string> AnalysisExcludeDomains { get; set; } = [];
}
