using Microsoft.AspNetCore.Components;

namespace CloudShell.Components;

/// <summary>A provider-owned registration step. Validation returns a user-facing error, or null.</summary>
public sealed record ResourceRegistrationWizardStep(string Title, RenderFragment Content, Func<string?>? Validate = null);
