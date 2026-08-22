# CloudShell website maintenance

The files in this directory are short, user-facing website content. They are deliberately separate from the project documentation in `docs/` and are published with DocFX by `.github/workflows/pages.yml`.

## Build and preview

```bash
dotnet tool restore
dotnet docfx docfx.json
python3 -m http.server 8097 --directory _site
```

Open `http://127.0.0.1:8097`. Check the landing page in both light and dark modes before publishing.

## Reproduce the showcase screenshots

The four carousel images are one coherent, high-resolution capture set. Replace all four when the product UI changes so navigation, data, and timestamps stay consistent.

1. Start Docker.
2. From the repository root, run the SignalR container app sample on a fixed port:

   ```bash
   dotnet run --project samples/SignalRContainerApp/CloudShell.SignalRContainerApp.csproj --no-launch-profile --urls http://127.0.0.1:5094
   ```

3. Open `http://127.0.0.1:5094/resources`. Start **SignalR API**, wait until its three replicas are running and healthy, then start **SignalR Frontend**.
4. For the trace-detail image, run the Application Topology sample instead. Start its API and Frontend resources, request `/upstream/fallback`, and open the resulting trace ID. This produces a frontend-to-API trace with a failed attempt, a successful fallback, and eight spans.
5. Use a 1280 × 720 browser viewport, English, the light product theme, and the expanded primary navigation. Capture at 2× pixel density (2560 × 1440 output), dismiss notifications, and wait for each view to become stable.
6. Capture these routes and states:

   | File | Route and state |
   | --- | --- |
   | `images/showcase-resources.png` | SignalR sample `/resources`; both declared applications running and healthy |
   | `images/showcase-traces.png` | Application Topology `/telemetry/traces`; one `/upstream/fallback` request with its eight-span breakdown visible |
   | `images/showcase-runtime-environment.png` | SignalR sample `/environment`; environment summary and the three-replica container runtime map visible |
   | `images/showcase-resource-graph.png` | SignalR sample `/resources/graph`; the declared frontend-to-API dependency visible |

7. Rebuild the DocFX site. Verify the carousel controls, captions, image crops, accessible alternative text, and both site color modes.

Screenshots must come from a fresh local run. Do not reuse historical images or include secrets, private endpoints, personal account information, or unrelated resources.
