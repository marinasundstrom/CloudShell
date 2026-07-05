namespace CloudShell.EventBrokerService;

public sealed class EventBrokerServiceOptions
{
    public const string SectionName = "CloudShell:EventBrokerService";

    public string? DefinitionsPath { get; set; }

    public string? ResourceId { get; set; }

    public string? EventsPath { get; set; }
}
