namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceEnrollmentRequest(
    string Subject,
    IReadOnlyDictionary<string, string>? Claims = null,
    IReadOnlyDictionary<string, string>? Properties = null,
    string? EnrollmentToken = null);
