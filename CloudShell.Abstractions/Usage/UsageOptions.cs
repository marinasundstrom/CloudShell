namespace CloudShell.Abstractions.Usage;

public sealed class UsageOptions
{
    public const string SectionName = "Usage";

    public string Store { get; set; } = UsageStores.InMemory;

    public int RetainedSamplesPerResource { get; set; } = 10_000;
}

public static class UsageStores
{
    public const string InMemory = "InMemory";

    public const string Database = "Database";
}
