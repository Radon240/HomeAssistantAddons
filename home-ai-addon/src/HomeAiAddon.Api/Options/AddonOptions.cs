using System.ComponentModel.DataAnnotations;

namespace HomeAiAddon.Api.Options;

public sealed class AddonOptions
{
    [ConfigurationKeyName("display_name")]
    [StringLength(120)]
    public string DisplayName { get; set; } = "Home AI Addon";

    [ConfigurationKeyName("enable_verbose_api")]
    public bool EnableVerboseApi { get; set; }
}
