# Load Balancer

This sample declares a Traefik-backed Load Balancer resource, container app
targets, and a replicated API container app. Applying the load-balancer action
writes Traefik dynamic configuration with backend entries for the API replicas.

```bash
dotnet run --project samples/LoadBalancer/CloudShell.LoadBalancer.csproj -- --urls http://localhost:5011
```
