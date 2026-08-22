---
title: CloudShell Preview
description: Model, run, inspect, and operate distributed applications in local and self-hosted environments with the CloudShell CLI.
_layout: landing
_disableContribution: true
_disableToc: true
---

<div class="cs-landing">
  <section class="cs-hero" aria-labelledby="hero-title">
    <div class="cs-hero-copy">
      <p class="cs-eyebrow"><span></span> Preview · Self-hosted · Resource-oriented</p>
      <h1 id="hero-title">Your application environment,<br><em>in one clear view.</em></h1>
      <p class="cs-lead">CloudShell is a language-neutral control plane for modeling, running, inspecting, and operating distributed applications—locally or on infrastructure your team owns.</p>
      <div class="cs-actions">
        <a class="cs-button cs-button-primary" href="get-started.md">Get started with the CLI <span aria-hidden="true">→</span></a>
        <a class="cs-button cs-button-secondary" href="https://github.com/marinasundstrom/CloudShell">View on GitHub</a>
      </div>
      <p class="cs-stage"><strong>Preview software.</strong> CloudShell is evolving quickly and is not a committed product.</p>
    </div>
    <div class="cs-hero-visual" aria-hidden="true">
      <div class="cs-orbit cs-orbit-one"></div>
      <div class="cs-orbit cs-orbit-two"></div>
      <div class="cs-node cs-node-main"><span class="cs-logo">CS</span><strong>CloudShell</strong><small>Control Plane</small></div>
      <div class="cs-node cs-node-app"><span class="cs-node-dot violet"></span><strong>Application</strong><small>Running</small></div>
      <div class="cs-node cs-node-data"><span class="cs-node-dot teal"></span><strong>SQL database</strong><small>Healthy</small></div>
      <div class="cs-node cs-node-telemetry"><span class="cs-node-dot blue"></span><strong>Telemetry</strong><small>Live signals</small></div>
    </div>
  </section>

  <section class="cs-section cs-product" aria-labelledby="product-title">
    <div class="cs-section-heading">
      <div>
        <p class="cs-kicker">One operational workspace</p>
        <h2 id="product-title">See the system, not a pile of tools.</h2>
      </div>
      <p>Follow resources from declared intent to live runtime state. Open endpoints, run lifecycle actions, trace dependencies, and diagnose failures without losing context.</p>
    </div>
    <div class="cs-carousel" data-carousel aria-roledescription="carousel" aria-label="CloudShell product highlights">
      <div class="cs-carousel-stage" aria-live="polite">
        <figure class="cs-slide is-active" data-carousel-slide data-title="Telemetry" data-copy="Follow logs, traces, metrics, dependencies, and service flow from one observability workspace.">
          <img src="../images/telemetry.jpg" alt="CloudShell telemetry workspace overview" loading="eager">
        </figure>
        <figure class="cs-slide" data-carousel-slide data-title="Resource Manager" data-copy="Filter the environment, check health, open endpoints, and run resource actions from a shared inventory.">
          <img src="../images/resources.png" alt="CloudShell Resource Manager inventory" loading="lazy">
        </figure>
        <figure class="cs-slide" data-carousel-slide data-title="Resource graph" data-copy="Understand applications, infrastructure, endpoints, and dependencies as one provider-neutral graph.">
          <img src="../images/resource-graph.png" alt="CloudShell application resource graph" loading="lazy">
        </figure>
        <figure class="cs-slide" data-carousel-slide data-title="Runtime graph" data-copy="Reveal replicas and runtime-managed resources when you need to move from intent into diagnosis.">
          <img src="../images/runtime-graph.png" alt="CloudShell expanded runtime graph" loading="lazy">
        </figure>
        <button class="cs-carousel-arrow cs-carousel-prev" type="button" data-carousel-prev aria-label="Previous highlight">←</button>
        <button class="cs-carousel-arrow cs-carousel-next" type="button" data-carousel-next aria-label="Next highlight">→</button>
      </div>
      <div class="cs-carousel-caption">
        <div><p class="cs-kicker" data-carousel-count>01 / 04</p><h3 data-carousel-title>Telemetry</h3></div>
        <p data-carousel-copy>Follow logs, traces, metrics, dependencies, and service flow from one observability workspace.</p>
        <div class="cs-carousel-dots" role="tablist" aria-label="Choose a product highlight">
          <button class="is-active" type="button" role="tab" aria-selected="true" aria-label="Show Telemetry" data-carousel-dot></button>
          <button type="button" role="tab" aria-selected="false" aria-label="Show Resource Manager" data-carousel-dot></button>
          <button type="button" role="tab" aria-selected="false" aria-label="Show Resource graph" data-carousel-dot></button>
          <button type="button" role="tab" aria-selected="false" aria-label="Show Runtime graph" data-carousel-dot></button>
        </div>
      </div>
    </div>
  </section>

  <section class="cs-section" aria-labelledby="resources-title">
    <div class="cs-section-heading">
      <div><p class="cs-kicker">Services and building blocks</p><h2 id="resources-title">Run the stack your application needs.</h2></div>
      <p>CloudShell presents workloads and infrastructure as resources with consistent identity, relationships, state, operations, and diagnostics.</p>
    </div>
    <div class="cs-resource-grid">
      <article class="cs-resource-card"><span class="cs-card-index">01</span><h3>Applications</h3><p>.NET, JavaScript, Java, Go, Python, executables, and container apps.</p><a href="resources/applications.md">Application resources <span aria-hidden="true">→</span></a></article>
      <article class="cs-resource-card"><span class="cs-card-index">02</span><h3>Data & messaging</h3><p>SQL Server, RabbitMQ, configuration stores, secrets vaults, event brokers, and device registries.</p><a href="resources/data-services.md">Data and service resources <span aria-hidden="true">→</span></a></article>
      <article class="cs-resource-card"><span class="cs-card-index">03</span><h3>Platform</h3><p>Container hosts, networks, load balancers, DNS names, storage, volumes, and identities.</p><a href="resources/platform.md">Platform resources <span aria-hidden="true">→</span></a></article>
      <article class="cs-resource-card cs-resource-card-accent"><span class="cs-card-index">04</span><h3>Operations</h3><p>Health, lifecycle actions, endpoints, logs, traces, metrics, monitoring, usage, and activity.</p><a href="observability.md">See observability <span aria-hidden="true">→</span></a></article>
    </div>
  </section>

  <section class="cs-section cs-flow" aria-labelledby="flow-title">
    <div class="cs-section-heading">
      <div><p class="cs-kicker">One model, several entry points</p><h2 id="flow-title">Start near the code. Grow into a platform.</h2></div>
      <p>The same domain-shaped resource graph moves between developer workflows, Resource Manager, automation, and self-hosted environments.</p>
    </div>
    <ol class="cs-steps">
      <li><span>01</span><div><h3>Declare</h3><p>Describe the environment with YAML, code-first launchers, templates, or the Control Plane API.</p></div></li>
      <li><span>02</span><div><h3>Run</h3><p>Providers turn resource intent into local processes, containers, managed services, and platform state.</p></div></li>
      <li><span>03</span><div><h3>Operate</h3><p>Inspect the graph, use endpoints and actions, and correlate health with telemetry in Resource Manager.</p></div></li>
    </ol>
  </section>

  <section class="cs-cta" aria-labelledby="cta-title">
    <p class="cs-kicker">Cloud-like primitives, without a public cloud account</p>
    <h2 id="cta-title">Build a clearer local environment.</h2>
    <p>Explore the architecture, try the YAML app host, or help shape CloudShell while the project is young.</p>
    <div class="cs-actions"><a class="cs-button cs-button-light" href="get-started.md">Get started <span aria-hidden="true">→</span></a><a class="cs-button cs-button-ghost" href="https://github.com/marinasundstrom/CloudShell">Contribute on GitHub</a></div>
  </section>
</div>
