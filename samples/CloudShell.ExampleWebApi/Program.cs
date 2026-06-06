var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    application = Environment.GetEnvironmentVariable("CLOUDSHELL_APPLICATION") ?? "Example Web API",
    machine = Environment.MachineName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/configuration", async (IHttpClientFactory httpClientFactory) =>
{
    var endpoint = Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_EXAMPLE_CONFIGURATION_ENDPOINT");
    var token = Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_EXAMPLE_CONFIGURATION_TOKEN");
    if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token))
    {
        return Results.Problem(
            "The Example Configuration service endpoint or token was not injected into this application.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    var client = httpClientFactory.CreateClient();
    var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        return Results.Problem(
            $"CloudShell configuration service returned {(int)response.StatusCode}.",
            statusCode: StatusCodes.Status502BadGateway);
    }

    var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<ConfigurationEntryResponse>>();
    return Results.Ok(new
    {
        source = endpoint,
        entries = entries?.Select(entry => new
        {
            entry.Name,
            Value = entry.IsSecret ? "Secret" : entry.Value,
            entry.IsSecret
        })
    });
});

app.MapGet("/echo/{message}", (string message) => Results.Ok(new
{
    message,
    length = message.Length
}));

app.Run();

public sealed record ConfigurationEntryResponse(
    string Name,
    string Value,
    bool IsSecret);
