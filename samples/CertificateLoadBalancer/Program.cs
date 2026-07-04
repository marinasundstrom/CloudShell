using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Traefik;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;

var builder = CloudShellApplication.CreateBuilder(args);

const string resourceGroupId = "certificate-load-balancer";
var dynamicConfigurationDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "Data",
    "traefik");
var developmentCertificate = CreateDevelopmentCertificate();

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var resourceGroup = resources.AddResourceGroup(
                resourceGroupId,
                "Certificate Load Balancer",
                "Resource model resources used by the CertificateLoadBalancer sample.");

            var dockerHostResource = resources
                .AddDockerHost("sample-host")
                .WithResourceGroup(resourceGroup);
            var webResource = resources
                .AddContainerApplication("web")
                .WithDisplayName("HTTPS Web App")
                .WithResourceGroup(resourceGroup)
                .UseDockerHost(dockerHostResource)
                .WithImage("nginx:1.27-alpine");
            var certificates = resources
                .AddSecretsVault("edge-certificates")
                .WithDisplayName("Edge Certificates")
                .WithResourceGroup(resourceGroup)
                .WithSeed(seed => seed.Certificate(developmentCertificate));

            var loadBalancerResource = resources
                .AddLoadBalancer("public")
                .WithDisplayName("Certificate Load Balancer")
                .WithResourceGroup(resourceGroup)
                .WithProvider("traefik")
                .UseHost(dockerHostResource)
                .ExposeHttps(certificates.Certificate("EdgeTls"), port: 4443)
                .MapHost(
                    "secure.cloudshell.local",
                    webResource,
                    port: 80,
                    entrypoint: "https");

            resources
                .AddDnsZone("cloudshell-local", zoneName: "cloudshell.local")
                .WithDisplayName("CloudShell Local DNS")
                .WithResourceGroup(resourceGroup)
                .UseLocalHostNames()
                .MapHost(
                    "secure.cloudshell.local",
                    loadBalancerResource,
                    endpointName: "https",
                    name: "secure-cloudshell-local",
                    configure: mapping => mapping.WithResourceGroup(resourceGroup));
        });
    });

builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<TelemetryExtension>()
        .AddExtension<UsageExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

cloudShell
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.Certificates.Add(new(
            developmentCertificate.Name,
            developmentCertificate.Value,
            developmentCertificate.Version,
            developmentCertificate.ContentType,
            developmentCertificate.Thumbprint,
            developmentCertificate.Subject,
            developmentCertificate.NotBefore,
            developmentCertificate.Expires,
            developmentCertificate.HasPrivateKey));
    })
    .AddTraefikProvider(options =>
    {
        options.DynamicConfigurationDirectory = dynamicConfigurationDirectory;
        options.ManageRuntimeContainer = !string.Equals(
            Environment.GetEnvironmentVariable("CLOUDSHELL_CERTIFICATE_LOADBALANCER_SKIP_TRAEFIK_RUNTIME"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    });

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();

static SecretsVaultSeedCertificate CreateDevelopmentCertificate()
{
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest(
        "CN=secure.cloudshell.local",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(
        new X509BasicConstraintsExtension(false, false, 0, false));
    request.CertificateExtensions.Add(
        new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
    request.CertificateExtensions.Add(
        new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

    var san = new SubjectAlternativeNameBuilder();
    san.AddDnsName("secure.cloudshell.local");
    san.AddDnsName("localhost");
    request.CertificateExtensions.Add(san.Build());

    var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
    var expires = notBefore.AddDays(30);
    using var certificate = request.CreateSelfSigned(notBefore, expires);

    return new(
        "EdgeTls",
        $"{rsa.ExportPkcs8PrivateKeyPem()}{certificate.ExportCertificatePem()}",
        ContentType: "application/x-pem-file",
        Thumbprint: certificate.Thumbprint,
        Subject: certificate.Subject,
        NotBefore: notBefore,
        Expires: expires,
        HasPrivateKey: true);
}
