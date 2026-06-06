namespace CloudShell.Host.Localization;

public sealed class CloudShellLocalizationOptions
{
    public const string SectionName = "Localization";

    public string DefaultCulture { get; set; } = "en";

    public IReadOnlyList<string> SupportedCultures { get; set; } = ["en"];
}
