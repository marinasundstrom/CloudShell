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

Resource guide screenshots use the same English, light-theme product state as the carousel. Capture them at 2× pixel density and keep the important resource details visible without browser chrome.

For `images/resource-rabbitmq.png`:

1. From `samples/RabbitMQMessaging`, run `./cloudshell.sh run-no-auth`.
2. Open the Resources view, start **RabbitMQ**, wait for its state to become **Running**, and select its table row.
3. Dismiss notifications or refresh the view so no command result remains. Keep the resource list and RabbitMQ detail panel open together; the image should show the publishers, volume, broker state, and AMQP and management endpoints.
4. Use a 1440 × 900 viewport and save a 2880 × 1800 PNG.
