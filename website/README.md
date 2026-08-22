# CloudShell website maintenance

The files in this directory are short, user-facing website content. They are deliberately separate from the project documentation in `docs/`. Pull requests and pushes validate the DocFX build through `.github/workflows/website.yml`; publishing is a separate, manual action through `.github/workflows/pages.yml`.

## Build and preview

```bash
dotnet tool restore
dotnet docfx docfx.json
python3 -m http.server 8097 --directory _site
```

Open `http://127.0.0.1:8097`. Check the landing page in both light and dark modes before publishing.

## Publish

Run the **Publish website** workflow manually in GitHub Actions after the site changes have been reviewed. Repository pushes do not publish the website.

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

## Reproduce resource guide screenshots

Resource guide images are feature crops, not full application screenshots. Each crop should retain enough CloudShell context to identify the surface while making the resource-specific capability the dominant subject.

### Shared capture contract

1. Use a fresh local sample run with English, the light product theme, and no browser chrome in the capture.
2. Wait until resource state, health, topology, and timestamps are stable. Dismiss notifications and command-result banners.
3. Capture at 2× pixel density when the browser supports it. Crop on whole CSS-pixel boundaries so the preferred output dimensions are exactly twice the crop dimensions. Store every final website asset as PNG.
4. When capture tooling only provides 1× output, crop first and perform one documented 2× PNG conversion. Never chain resizes or recompress an existing website asset.
5. Compare the result with the existing file before replacing it. Keep approximately the same subject, scale, aspect ratio, and surrounding context when the UI layout changes.
6. Rebuild the DocFX site and inspect the image at its rendered page width in light and dark site themes.

Crop coordinates below use `x, y, width, height` from the top-left of the source viewport in CSS pixels.

| Output | Sample and state | Route | Source viewport | Feature crop | Output size |
| --- | --- | --- | --- | --- | --- |
| `images/resource-rabbitmq-graph.png` | RabbitMQ Messaging; start RabbitMQ plus both publishers; graph shows 4 resources, 3 dependencies, and running status for the broker and publishers | `/resources/graph` | 1440 × 900 | graph canvas around RabbitMQ, .NET Publisher, Java Publisher, and RabbitMQ Data; `420, 235, 800, 535` | 1600 × 1070 |
| `images/resource-rabbitmq-topology.png` | RabbitMQ Messaging; both publishers running; topology reports the sample virtual host, 2 queues, 8 exchanges, and 4 bindings | `/resources/application.rabbitmq%3Arabbitmq/topology` | 1440 × 900 | RabbitMQ title, resource-view navigation, topology summary, and both sample queue rows; `225, 80, 1200, 790` | 2400 × 1580 |
| `images/resource-container-app-replicas.png` | SignalR Container App; start SignalR API and wait for one active replica group with 3 of 3 replicas running | `/environment` | 1440 × 900 | Zoom the map out once, bring the **Environment map** heading into view, then crop the card containing the container app, replica group, routing binding, and replicas 1–3; `240, 302, 780, 598` | 1560 × 1196 |
| `images/resource-dotnet-trace.png` | Application Topology; request `/upstream/fallback` and select the resulting eight-span trace | `/telemetry/traces` | 1280 × 720 | trace source, request summary, span waterfall, and selected-span details; `190, 130, 1050, 560` | 2100 × 1120 |

### Sample commands

For the RabbitMQ images:

```bash
cd samples/RabbitMQMessaging
./cloudshell.sh run-no-auth
./cloudshell.sh start-apps
```

For the container replica image, run the SignalR sample as described in the carousel procedure, start **SignalR API**, and wait until the Environment summary reports 7 resources, 1 deployment record, 1 replica group, and 0 replica issues. If the map composition changes, use its zoom controls to keep all three replica nodes visible before capturing.

The .NET trace crop is derived from the same stable trace state used by `images/showcase-traces.png`. Update both files in the same session so service names, durations, trace ID, and timestamps remain consistent.
