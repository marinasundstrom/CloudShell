import { spawn } from "node:child_process";
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";

export type ResourceReferenceRelationship = "dependsOn" | "reference" | "belongsTo";
export type ResourceReferenceAddressingMode = "resourceId" | "projectedResource" | "providerNative";
export type TemplateApplyMode = "create-or-update" | "create-only" | "update-existing";

export interface ResourceReferenceDocument {
  resourceId: string;
  relationship?: ResourceReferenceRelationship;
  addressingMode?: ResourceReferenceAddressingMode;
  typeId?: string;
  providerId?: string;
}

export interface ResourceDefinitionDocument {
  name: string;
  type: string;
  resourceId?: string;
  providerId?: string;
  displayName?: string;
  dependsOn?: ResourceReferenceDocument[];
  attributes?: Record<string, unknown>;
  configuration?: Record<string, unknown>;
  capabilities?: Record<string, unknown>;
  operations?: Record<string, unknown>;
  metadata?: Record<string, string>;
  [key: string]: unknown;
}

export interface ResourceTemplateDocument {
  name: string;
  resources: ResourceDefinitionDocument[];
  environmentId?: string;
  metadata?: Record<string, string>;
}

export interface ResourceHandle {
  readonly name: string;
  readonly type: string;
  readonly providerId?: string;
  readonly effectiveResourceId: string;
}

export interface TemplateOptions {
  environmentId?: string;
  metadata?: Record<string, string>;
}

export interface EndpointRequestOptions {
  name?: string;
  protocol?: string;
  targetPort?: number;
  host?: string;
  port?: number;
  exposure?: string;
  ipAddress?: string;
  assignment?: string;
  network?: ResourceHandle | string;
}

export type EnvironmentVariableValue =
  | string
  | {
      value?: string;
      configurationEntryRef?: ConfigurationEntryReference;
      secretRef?: SecretReference;
    };

export interface ConfigurationEntryReference {
  storeResourceId: string;
  name: string;
  version?: string;
}

export interface SecretReference {
  vaultResourceId: string;
  name: string;
  version?: string;
}

export interface ApplyOptions {
  cliProject?: string;
  cloudshellCommand?: string;
  templatePath?: string;
  controlPlaneUrl?: string;
  stateDir?: string;
  start?: boolean;
  hostProject?: string;
  url?: string;
  noBuild?: boolean;
  timeoutSeconds?: number;
  mode?: TemplateApplyMode;
  bearerToken?: string;
  cwd?: string;
  stdio?: "inherit" | "pipe";
}

export interface CommandResult {
  command: string;
  args: string[];
  exitCode: number;
}

export function cloudshell(
  name: string,
  options?: TemplateOptions): CloudShellApp {
  return new CloudShellApp(name, options);
}

export class CloudShellApp {
  private readonly resources: ResourceBuilder[] = [];

  public constructor(
    public readonly name: string,
    private readonly options: TemplateOptions = {}) {
    assertNotBlank(name, "Template name is required.");
  }

  public add(resource: ResourceBuilder): ResourceBuilder {
    if (this.resources.some(existing =>
      existing.effectiveResourceId.toLowerCase() === resource.effectiveResourceId.toLowerCase())) {
      throw new Error(`Resource '${resource.effectiveResourceId}' is already defined.`);
    }

    resource.useGraph(this);
    this.resources.push(resource);
    return resource;
  }

  public resource(
    name: string,
    type: string,
    options: { providerId?: string; resourceId?: string } = {}): ResourceBuilder {
    const builder = new ResourceBuilder(name, type, options.providerId);
    if (options.resourceId) {
      builder.withResourceId(options.resourceId);
    }

    this.add(builder);
    return builder;
  }

  public addConfigurationStore(name: string): ConfigurationStoreResourceBuilder {
    const builder = new ConfigurationStoreResourceBuilder(name);
    this.add(builder);
    return builder;
  }

  public addJavaScriptApp(
    name: string,
    projectPath: string): JavaScriptAppResourceBuilder {
    const builder = new JavaScriptAppResourceBuilder(name)
      .withProjectPath(projectPath)
      .withEngine("node")
      .withPackageManager("npm")
      .withScript("dev")
      .withDefaultConsoleLogSource();
    this.add(builder);
    return builder;
  }

  public getDefaultNetwork(): NetworkResourceBuilder {
    const existing = this.resources.find(resource =>
      resource.effectiveResourceId.toLowerCase() === "network:host");
    if (existing) {
      if (existing instanceof NetworkResourceBuilder) {
        return existing;
      }

      throw new Error(`Resource 'network:host' is already defined as '${existing.type}'.`);
    }

    const builder = new NetworkResourceBuilder("host")
      .withResourceId("network:host")
      .withDisplayName("Host network")
      .withNetworkKind("Host")
      .withHostReadiness("hostReady");
    this.add(builder);
    return builder;
  }

  public buildTemplate(options: TemplateOptions = {}): ResourceTemplateDocument {
    return pruneUndefined({
      name: this.name,
      resources: this.resources.map(resource => resource.build()),
      environmentId: options.environmentId ?? this.options.environmentId,
      metadata: options.metadata ?? this.options.metadata
    }) as ResourceTemplateDocument;
  }

  public toJson(options: TemplateOptions = {}): string {
    return `${JSON.stringify(this.buildTemplate(options), null, 2)}\n`;
  }

  public async writeTemplate(
    path: string,
    options: TemplateOptions = {}): Promise<string> {
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, this.toJson(options), "utf8");
    return path;
  }

  public async apply(options: ApplyOptions = {}): Promise<CommandResult> {
    const templatePath = options.templatePath ??
      join(await mkdtemp(join(tmpdir(), "cloudshell-template-")), "resources.json");
    await this.writeTemplate(templatePath);

    const args = buildTemplateApplyArgs(templatePath, options);
    const command = options.cliProject ? "dotnet" : (options.cloudshellCommand ?? "cloudshell");
    const commandArgs = options.cliProject
      ? ["run", "--project", options.cliProject, "--", ...args]
      : args;

    const exitCode = await spawnCommand(command, commandArgs, options);
    return { command, args: commandArgs, exitCode };
  }

  public run(options: ApplyOptions = {}): Promise<CommandResult> {
    return this.apply({ ...options, start: true });
  }
}

export class ResourceBuilder implements ResourceHandle {
  private graph?: CloudShellApp;
  private resourceId?: string;
  private displayName?: string;
  private readonly dependencies: ResourceReferenceDocument[] = [];
  private readonly attributes: Record<string, unknown> = {};
  private readonly configuration: Record<string, unknown> = {};
  private readonly capabilities: Record<string, unknown> = {};
  private readonly operations: Record<string, unknown> = {};
  private readonly metadata: Record<string, string> = {};

  public constructor(
    public readonly name: string,
    public readonly type: string,
    public readonly providerId?: string) {
    assertNotBlank(name, "Resource name is required.");
    assertNotBlank(type, "Resource type is required.");
  }

  public get effectiveResourceId(): string {
    return this.resourceId ?? `${this.type}:${this.name}`;
  }

  public useGraph(graph: CloudShellApp): void {
    this.graph = graph;
  }

  public withResourceId(resourceId: string): this {
    assertNotBlank(resourceId, "Resource id is required.");
    this.resourceId = resourceId.trim();
    return this;
  }

  public withDisplayName(displayName: string): this {
    assertNotBlank(displayName, "Display name is required.");
    this.displayName = displayName.trim();
    return this;
  }

  public dependsOn(resource: ResourceHandle | string): this {
    this.dependencies.push(dependsOn(resource));
    return this;
  }

  public withAttribute(path: string, value: unknown): this {
    setDottedValue(this.attributes, path, value);
    return this;
  }

  public withConfiguration(sectionName: string, value: unknown): this {
    assertNotBlank(sectionName, "Configuration section name is required.");
    this.configuration[sectionName.trim()] = value;
    return this;
  }

  public withCapability(capabilityId: string, value: unknown = {}): this {
    assertNotBlank(capabilityId, "Capability id is required.");
    this.capabilities[capabilityId.trim()] = value;
    return this;
  }

  public withOperation(operationId: string, value: unknown = {}): this {
    assertNotBlank(operationId, "Operation id is required.");
    this.operations[operationId.trim()] = value;
    return this;
  }

  public withMetadata(name: string, value: string): this {
    assertNotBlank(name, "Metadata name is required.");
    this.metadata[name.trim()] = value;
    return this;
  }

  public build(): ResourceDefinitionDocument {
    const definition: ResourceDefinitionDocument = pruneUndefined({
      name: this.name,
      type: this.type,
      resourceId: this.effectiveResourceId,
      providerId: this.providerId,
      displayName: this.displayName,
      dependsOn: this.dependencies.length === 0 ? undefined : this.dependencies,
      configuration: isEmpty(this.configuration) ? undefined : this.configuration,
      capabilities: isEmpty(this.capabilities) ? undefined : this.capabilities,
      operations: isEmpty(this.operations) ? undefined : this.operations,
      metadata: isEmpty(this.metadata) ? undefined : this.metadata
    }) as ResourceDefinitionDocument;

    mergeResourceAttributes(definition, this.attributes);
    return definition;
  }

  protected reference(
    resource: ResourceHandle | string,
    typeId?: string,
    providerId?: string): ResourceReferenceDocument {
    if (typeof resource === "string") {
      return pruneUndefined({
        resourceId: resource,
        relationship: "reference",
        addressingMode: "resourceId",
        typeId,
        providerId
      }) as ResourceReferenceDocument;
    }

    return pruneUndefined({
      resourceId: resource.effectiveResourceId,
      relationship: "reference",
      addressingMode: "resourceId",
      typeId: typeId ?? resource.type,
      providerId: providerId ?? resource.providerId
    }) as ResourceReferenceDocument;
  }

  protected defaultNetworkReference(): ResourceReferenceDocument | undefined {
    return this.graph?.getDefaultNetwork().asReference();
  }
}

export class NetworkResourceBuilder extends ResourceBuilder {
  public constructor(name: string) {
    super(name, "cloudshell.network", "cloudshell.network");
  }

  public withNetworkKind(kind: string): this {
    return this.withAttribute("network.kind", kind);
  }

  public withHostReadiness(readiness: string): this {
    return this.withAttribute("network.hostReadiness", readiness);
  }

  public withMappingProviders(...providers: string[]): this {
    return this.withAttribute(
      "network.mappingProviders",
      providers.filter(provider => provider.trim().length > 0).join(","));
  }

  public asReference(): ResourceReferenceDocument {
    return this.reference(this);
  }
}

export class ConfigurationStoreResourceBuilder extends ResourceBuilder {
  public constructor(name: string) {
    super(name, "configuration.store", "configuration");
  }

  public withEndpoint(endpoint: string): this {
    return this.withAttribute("configuration.endpoint", endpoint);
  }

  public entry(name: string, version?: string): ConfigurationEntryReference {
    assertNotBlank(name, "Configuration entry name is required.");
    return pruneUndefined({
      storeResourceId: this.effectiveResourceId,
      name: name.trim(),
      version: normalizeOptionalString(version)
    }) as ConfigurationEntryReference;
  }
}

export class JavaScriptAppResourceBuilder extends ResourceBuilder {
  private readonly endpointRequests: unknown[] = [];
  private readonly environmentVariables: Record<string, unknown> = {};
  private readonly references: ResourceReferenceDocument[] = [];
  private readonly healthChecks: unknown[] = [];

  public constructor(name: string) {
    super(name, "application.javascript-app", "applications.javascript-app");
  }

  public withProjectPath(projectPath: string): this {
    return this.withAttribute("project.path", projectPath);
  }

  public withEngine(engine: string): this {
    return this.withAttribute("javascript.engine", engine);
  }

  public withPackageManager(packageManager: string): this {
    return this.withAttribute("javascript.packageManager", packageManager);
  }

  public withScript(script: string): this {
    return this.withAttribute("javascript.script", script);
  }

  public withArguments(args: string): this {
    return this.withAttribute("javascript.arguments", args);
  }

  public withServiceDiscovery(name: string = this.name): this {
    return this.withAttribute("project.serviceDiscoveryName", name);
  }

  public withDefaultConsoleLogSource(format: "plainText" | "jsonConsole" = "plainText"): this {
    return this.withAttribute("logs.sources", [
      {
        id: "console",
        name: "Console logs",
        kind: "processOutput",
        format,
        capabilities: ["read", "stream"],
        description: "Provider-captured process console output.",
        origin: "providerDefault",
        purpose: "default",
        availability: "resourceRunning"
      }
    ]);
  }

  public withEndpoint(options: EndpointRequestOptions): this {
    const network = resolveNetworkReference(options.network) ?? this.defaultNetworkReference();
    this.endpointRequests.push(pruneUndefined({
      name: options.name ?? "http",
      protocol: options.protocol ?? "http",
      targetPort: options.targetPort,
      host: options.host,
      port: options.port,
      exposure: options.exposure ?? "Local",
      ipAddress: options.ipAddress,
      assignment: options.assignment,
      network
    }));
    return this.withAttribute("project.endpointRequests", this.endpointRequests);
  }

  public withHttpEndpoint(options: Omit<EndpointRequestOptions, "protocol"> = {}): this {
    return this.withEndpoint({ ...options, protocol: "http" });
  }

  public withEnvironmentVariable(
    name: string,
    value: EnvironmentVariableValue): this {
    assertNotBlank(name, "Environment variable name is required.");
    this.environmentVariables[name.trim()] = typeof value === "string"
      ? { value }
      : pruneUndefined(value);
    return this.withAttribute("project.environmentVariables", this.environmentVariables);
  }

  public withReference(
    resource: ResourceHandle | string,
    typeId?: string,
    providerId?: string): this {
    this.references.push(this.reference(resource, typeId, providerId));
    return this.withAttribute("project.references", this.references);
  }

  public withHttpHealthCheck(
    path: string,
    options: { endpointName?: string; name?: string; timeoutMilliseconds?: number; intervalSeconds?: number } = {}): this {
    return this.withHttpProbe("health", path, {
      name: options.name ?? "health",
      endpointName: options.endpointName,
      timeoutMilliseconds: options.timeoutMilliseconds,
      intervalSeconds: options.intervalSeconds
    });
  }

  public withHttpLivenessCheck(
    path: string,
    options: { endpointName?: string; name?: string; timeoutMilliseconds?: number; intervalSeconds?: number } = {}): this {
    return this.withHttpProbe("liveness", path, {
      name: options.name ?? "alive",
      endpointName: options.endpointName,
      timeoutMilliseconds: options.timeoutMilliseconds,
      intervalSeconds: options.intervalSeconds
    });
  }

  public withHttpProbe(
    type: string,
    path: string,
    options: { endpointName?: string; name?: string; timeoutMilliseconds?: number; intervalSeconds?: number } = {}): this {
    assertNotBlank(type, "Health check type is required.");
    assertNotBlank(path, "Health check path is required.");
    this.healthChecks.push(pruneUndefined({
      name: options.name ?? type,
      type,
      source: {
        kind: "http",
        http: pruneUndefined({
          path,
          endpointName: options.endpointName,
          timeoutMilliseconds: options.timeoutMilliseconds
        })
      },
      intervalSeconds: options.intervalSeconds
    }));
    return this.withAttribute("health.checks", this.healthChecks);
  }
}

function dependsOn(resource: ResourceHandle | string): ResourceReferenceDocument {
  if (typeof resource === "string") {
    return { resourceId: resource, relationship: "dependsOn", addressingMode: "resourceId" };
  }

  return pruneUndefined({
    resourceId: resource.effectiveResourceId,
    relationship: "dependsOn",
    addressingMode: "resourceId",
    typeId: resource.type,
    providerId: resource.providerId
  }) as ResourceReferenceDocument;
}

function resolveNetworkReference(
  network: ResourceHandle | string | undefined): ResourceReferenceDocument | undefined {
  if (!network) {
    return undefined;
  }

  return typeof network === "string"
    ? { resourceId: network, relationship: "reference", addressingMode: "resourceId" }
    : {
        resourceId: network.effectiveResourceId,
        relationship: "reference",
        addressingMode: "resourceId",
        typeId: network.type,
        providerId: network.providerId
      };
}

function buildTemplateApplyArgs(
  templatePath: string,
  options: ApplyOptions): string[] {
  const args = ["template", "apply", templatePath];
  pushOption(args, "--control-plane", options.controlPlaneUrl);
  pushOption(args, "--state-dir", options.stateDir);
  pushOption(args, "--host-project", options.hostProject);
  pushOption(args, "--url", options.url);
  pushOption(args, "--timeout-seconds", options.timeoutSeconds?.toString());
  pushOption(args, "--mode", options.mode);
  pushOption(args, "--bearer-token", options.bearerToken);
  if (options.start) {
    args.push("--start");
  }

  if (options.noBuild) {
    args.push("--no-build");
  }

  return args;
}

function pushOption(args: string[], name: string, value: string | undefined): void {
  if (value && value.trim().length > 0) {
    args.push(name, value);
  }
}

async function spawnCommand(
  command: string,
  args: string[],
  options: ApplyOptions): Promise<number> {
  return await new Promise<number>((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: options.cwd,
      stdio: options.stdio ?? "inherit",
      shell: false
    });

    child.on("error", reject);
    child.on("close", code => resolve(code ?? 1));
  });
}

function setDottedValue(
  target: Record<string, unknown>,
  path: string,
  value: unknown): void {
  assertNotBlank(path, "Attribute path is required.");
  const segments = path.split(".").map(segment => segment.trim()).filter(Boolean);
  if (segments.length === 0) {
    throw new Error("Attribute path is required.");
  }

  let current = target;
  for (let index = 0; index < segments.length - 1; index++) {
    const segment = segments[index]!;
    const child = current[segment];
    if (!isRecord(child)) {
      current[segment] = {};
    }

    current = current[segment] as Record<string, unknown>;
  }

  current[segments[segments.length - 1]!] = value;
}

function mergeResourceAttributes(
  definition: ResourceDefinitionDocument,
  attributes: Record<string, unknown>): void {
  for (const [key, value] of Object.entries(attributes)) {
    if (value === undefined) {
      continue;
    }

    if (resourceDefinitionProperties.has(key.toLowerCase())) {
      definition.attributes ??= {};
      definition.attributes[key] = value;
    } else {
      definition[key] = value;
    }
  }
}

function pruneUndefined(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(item => pruneUndefined(item));
  }

  if (isRecord(value)) {
    const result: Record<string, unknown> = {};
    for (const [key, child] of Object.entries(value)) {
      if (child !== undefined) {
        result[key] = pruneUndefined(child);
      }
    }

    return result;
  }

  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isEmpty(value: Record<string, unknown>): boolean {
  return Object.keys(value).length === 0;
}

function normalizeOptionalString(value: string | undefined): string | undefined {
  return value && value.trim().length > 0 ? value.trim() : undefined;
}

function assertNotBlank(value: string, message: string): void {
  if (!value || value.trim().length === 0) {
    throw new Error(message);
  }
}

const resourceDefinitionProperties = new Set([
  "name",
  "type",
  "typeid",
  "resourceid",
  "providerid",
  "displayname",
  "version",
  "dependson",
  "attributes",
  "configuration",
  "capabilities",
  "operations",
  "metadata"
]);
