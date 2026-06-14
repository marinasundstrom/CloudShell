# Load Balancer

This sample declares a Traefik-backed Load Balancer resource, real public
container images for web/API/Postgres targets, and a replicated API container
app. Applying the load-balancer action writes Traefik dynamic configuration
with backend entries for the target container app instances. Starting the
load-balancer resource starts the provider-owned Traefik container on the
selected Docker host.

The sample also declares a **CloudShell Local DNS** zone with
`app.cloudshell.local` and `api.cloudshell.local` name mappings that target
the public load balancer frontend. The zone opts into the local host-name
publisher. Use **Reconcile name mappings** on the DNS zone to apply or
re-apply those exact host mappings.

```bash
dotnet run --project samples/LoadBalancer/CloudShell.LoadBalancer.csproj -- --urls http://localhost:5011
```

Open `http://127.0.0.1:5011/resources`, start **Web App**, **API Service**,
and **Postgres Replica Set**, then start **Public Load Balancer**. You can
invoke **Apply load balancer configuration** afterwards to reconcile route
configuration without restarting the Traefik runtime container.

For the normal container-app path, **API Service** exposes its own app endpoint
as soon as it starts. Because it has three replicas, CloudShell starts
app-owned ingress for `http://localhost:5081` automatically; no public load
balancer action is required for that endpoint.

```bash
curl http://localhost:5081/
```

The HTTP routes are host-based, so `http://localhost/` is not a configured
public load-balancer route. Use the declared hosts after starting the public
load balancer. If you do not reconcile local host-name mappings, use
`--resolve` to provide local name resolution:

```bash
curl --resolve app.cloudshell.local:80:127.0.0.1 http://app.cloudshell.local/
curl --resolve api.cloudshell.local:80:127.0.0.1 http://api.cloudshell.local/v1/get
```

By default the local host-name publisher targets the system hosts file when
you invoke **Reconcile name mappings**, which may require elevated
permissions. To inspect the generated entries without changing the system
hosts file, set `CLOUDSHELL_LOCAL_HOSTS_FILE` before running the sample:

```bash
CLOUDSHELL_LOCAL_HOSTS_FILE=samples/LoadBalancer/Data/cloudshell.hosts \
  dotnet run --project samples/LoadBalancer/CloudShell.LoadBalancer.csproj -- --urls http://localhost:5011
```

The sample exposes Postgres through the TCP entrypoint on `localhost:5432`.
