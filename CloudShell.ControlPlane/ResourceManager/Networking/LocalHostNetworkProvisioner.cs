using System.Net;
using System.Net.Sockets;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Networking;

public sealed class LocalHostNetworkProvisioner : IResourceEndpointMappingProvisioner, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, EndpointMappingProxy> proxies = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSupported => true;

    public int ProvisionedMappingCount
    {
        get
        {
            lock (gate)
            {
                return proxies.Count;
            }
        }
    }

    public bool CanProvisionEndpointMapping(ResourceEndpointMappingProvisioningContext context) =>
        IsSupported &&
        IsSupportedProviderResource(context.ProviderResource.Id) &&
        IsSupportedProtocol(context.SourceEndpoint.Protocol) &&
        IsSupportedProtocol(context.TargetEndpoint.Protocol);

    public async Task<ResourceProcedureResult> ProvisionEndpointMappingAsync(
        ResourceEndpointMappingProvisioningContext context,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException(
                "The local host networking provider is not supported on this host.");
        }

        if (!CanProvisionEndpointMapping(context))
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{context.Mapping.Id}' cannot be provisioned by the local host networking provider.");
        }

        var source = ParseEndpoint(
            context.SourceEndpoint,
            context.SourceEndpointNetworkMapping,
            "source",
            context.Mapping.Id);
        var target = ParseEndpoint(
            context.TargetEndpoint,
            context.TargetEndpointNetworkMapping,
            "target",
            context.Mapping.Id);
        EndpointMappingProxy? previous = null;
        lock (gate)
        {
            if (proxies.Remove(context.Mapping.Id, out var existing))
            {
                previous = existing;
            }
        }

        if (previous is not null)
        {
            await previous.DisposeAsync();
        }

        var proxy = await EndpointMappingProxy.StartAsync(
            context.Mapping.Id,
            source,
            target,
            cancellationToken);
        lock (gate)
        {
            proxies[context.Mapping.Id] = proxy;
        }

        return ResourceProcedureResult.Completed(
            $"Provisioned endpoint mapping '{context.Mapping.Id}' through the local host networking provider.");
    }

    public async ValueTask DisposeAsync()
    {
        EndpointMappingProxy[] snapshot;
        lock (gate)
        {
            snapshot = proxies.Values.ToArray();
            proxies.Clear();
        }

        foreach (var proxy in snapshot)
        {
            await proxy.DisposeAsync();
        }
    }

    private static bool IsSupportedProtocol(string protocol) =>
        protocol.Equals("http", StringComparison.OrdinalIgnoreCase) ||
        protocol.Equals("https", StringComparison.OrdinalIgnoreCase) ||
        protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedProviderResource(string resourceId) =>
        string.Equals(resourceId, LocalHostNetworkProvider.ResourceId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceId, MacOSHostNetworkProvider.ResourceId, StringComparison.OrdinalIgnoreCase);

    private static HostEndpoint ParseEndpoint(
        ResourceEndpoint endpoint,
        ResourceEndpointNetworkMapping? endpointNetworkMapping,
        string role,
        string mappingId)
    {
        if (!TryGetEndpointUri(endpoint, endpointNetworkMapping, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port <= 0)
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mappingId}' {role} endpoint '{endpoint.Name}' must use a mapped absolute host:port address.");
        }

        return new HostEndpoint(uri.Host, uri.Port);
    }

    private static bool TryGetEndpointUri(
        ResourceEndpoint endpoint,
        ResourceEndpointNetworkMapping? endpointNetworkMapping,
        out Uri uri)
    {
        if (endpointNetworkMapping is not null && endpointNetworkMapping.TryGetUri(out uri))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private sealed record HostEndpoint(string Host, int Port);

    private sealed class EndpointMappingProxy : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly TcpListener listener;
        private Task acceptLoop = Task.CompletedTask;

        private EndpointMappingProxy(TcpListener listener)
        {
            this.listener = listener;
        }

        public static async Task<EndpointMappingProxy> StartAsync(
            string mappingId,
            HostEndpoint source,
            HostEndpoint target,
            CancellationToken cancellationToken)
        {
            var address = await ResolveBindAddressAsync(source.Host, cancellationToken);
            var listener = new TcpListener(address, source.Port);
            try
            {
                listener.Start();
            }
            catch (SocketException exception)
            {
                throw new InvalidOperationException(
                    $"Endpoint mapping '{mappingId}' could not bind {source.Host}:{source.Port}. The port may already be in use.",
                    exception);
            }

            var proxy = new EndpointMappingProxy(listener);
            proxy.acceptLoop = proxy.AcceptLoopAsync(mappingId, target, proxy.cancellation.Token);
            return proxy;
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                await acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync(
            string mappingId,
            HostEndpoint target,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient incoming;
                try
                {
                    incoming = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(
                    () => ForwardAsync(incoming, target, cancellationToken),
                    CancellationToken.None);
            }
        }

        private static async Task ForwardAsync(
            TcpClient incoming,
            HostEndpoint target,
            CancellationToken cancellationToken)
        {
            using var incomingClient = incoming;
            using var outgoing = new TcpClient();
            try
            {
                await outgoing.ConnectAsync(target.Host, target.Port, cancellationToken).ConfigureAwait(false);
                using var incomingStream = incoming.GetStream();
                using var outgoingStream = outgoing.GetStream();
                var incomingToOutgoing = incomingStream.CopyToAsync(outgoingStream, cancellationToken);
                var outgoingToIncoming = outgoingStream.CopyToAsync(incomingStream, cancellationToken);
                await Task.WhenAny(incomingToOutgoing, outgoingToIncoming).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                // Individual connection failures should not stop the mapping listener.
            }
        }

        private static async Task<IPAddress> ResolveBindAddressAsync(
            string host,
            CancellationToken cancellationToken)
        {
            if (IPAddress.TryParse(host, out var address))
            {
                return address;
            }

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.Loopback;
            }

            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            return addresses.FirstOrDefault(address =>
                    address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                ?? IPAddress.Loopback;
        }
    }
}
