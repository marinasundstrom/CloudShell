namespace CloudShell.Abstractions.ResourceManager;

public interface ICertificateReferenceCatalog
{
    ValueTask<IReadOnlyList<CertificateReferenceDescriptor>> ListCertificatesAsync(
        IReadOnlyList<Resource> resources,
        CancellationToken cancellationToken = default);
}

public sealed record CertificateReferenceDescriptor(
    string VaultResourceId,
    string Name,
    string? Version = null,
    string? ContentType = null,
    string? Thumbprint = null,
    string? Subject = null,
    DateTimeOffset? Expires = null);
