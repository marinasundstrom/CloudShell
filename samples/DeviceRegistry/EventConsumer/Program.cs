using CloudShell.EventBroker.Client;
using System.Text.Json;

var endpoint = GetOption(args, "--event-broker-endpoint") ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_EVENT_BROKER_ENDPOINT") ??
    "http://localhost:7184";
var brokerResourceId = GetOption(args, "--event-broker-resource-id") ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_EVENT_BROKER_RESOURCE_ID") ??
    "event.broker:events";
var stream = GetOption(args, "--stream") ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_EVENT_BROKER_STREAM") ??
    "device-checkins";
var fromSequence = long.TryParse(GetOption(args, "--from-sequence"), out var parsedSequence)
    ? parsedSequence
    : 0;

var client = new EventBrokerClient(new Uri(endpoint));
Console.WriteLine($"Reading CloudShell Event Broker stream '{stream}' from {endpoint}.");
var response = await client.ReadEventsAsync(
    brokerResourceId,
    stream,
    fromSequence);

if (response.Events.Count == 0)
{
    Console.WriteLine("No retained events were found.");
    return;
}

foreach (var item in response.Events)
{
    Console.WriteLine(
        $"#{item.Sequence} {item.Timestamp:O} {item.Type} source={Format(item.Source)} subject={Format(item.Subject)}");
    Console.WriteLine(JsonSerializer.Serialize(
        item.Data,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
}

static string? GetOption(string[] args, string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static string Format(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "-" : value;
