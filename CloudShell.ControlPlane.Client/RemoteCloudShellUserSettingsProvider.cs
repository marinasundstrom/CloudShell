using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Shell;

namespace CloudShell.ControlPlane.Client;

public sealed class RemoteCloudShellUserSettingsProvider(HttpClient httpClient) :
    ICloudShellControlPlaneUserSettingsProvider
{
    private const string RoutePrefix = "api/control-plane/v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, CloudShellUserSetting>> GetSettingsAsync(
        CancellationToken cancellationToken = default) =>
        (await GetRequiredAsync<IReadOnlyList<CloudShellUserSettingResponse>>(
            "environment-settings",
            cancellationToken))
        .Select(ToUserSetting)
        .ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);

    public async Task<CloudShellUserSetting?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var response = await GetOptionalAsync<CloudShellUserSettingResponse>(
            $"environment-settings/{Escape(key)}",
            cancellationToken);
        return response is null ? null : ToUserSetting(response);
    }

    public async Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            BuildUri($"environment-settings/{Escape(key)}"),
            new SetCloudShellUserSettingRequest(value),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RemoveSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            BuildUri($"environment-settings/{Escape(key)}"),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<T?> GetOptionalAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(BuildUri(path), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private async Task<T> GetRequiredAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(BuildUri(path), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadRequiredAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return value ?? throw new InvalidOperationException("The settings service returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var problem = await ReadProblemAsync(response, cancellationToken);
        var message = problem?.Detail ?? problem?.Title;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"The settings service returned {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        if (!string.IsNullOrWhiteSpace(problem?.Code))
        {
            var error = new ControlPlaneError(problem.Code, message);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                throw new ControlPlaneAccessDeniedException(error);
            }

            throw new ControlPlaneException(error);
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized ||
            ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400))
        {
            throw new UnauthorizedAccessException(message);
        }

        throw new ControlPlaneException(new ControlPlaneError(ControlPlaneErrorCodes.OperationFailed, message));
    }

    private static async Task<ProblemResponse?> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemResponse>(
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildUri(string path) =>
        $"{RoutePrefix}/{path.TrimStart('/')}";

    private static string Escape(string value) =>
        Uri.EscapeDataString(value);

    private static CloudShellUserSetting ToUserSetting(CloudShellUserSettingResponse response) =>
        new(response.Key, response.Value, response.UpdatedAt);

    private sealed record CloudShellUserSettingResponse(
        string Key,
        string Value,
        DateTimeOffset UpdatedAt);

    private sealed record SetCloudShellUserSettingRequest(string Value);

    private sealed record ProblemResponse(string? Title, string? Detail, string? Code);
}
