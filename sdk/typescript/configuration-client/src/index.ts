import { readFile } from "node:fs/promises";
import { homedir } from "node:os";
import { dirname, isAbsolute, join } from "node:path";

export const defaultConfigurationScope = "ControlPlane.Access";

export interface CloudShellConfigurationSetting {
  name: string;
  value: string;
}

export interface CloudShellSecretProperties {
  name: string;
  version?: string;
}

export interface CloudShellSecretValue {
  name: string;
  value: string;
  version?: string;
}

export interface AccessToken {
  token: string;
  expiresOnTimestamp?: number;
}

export interface TokenCredential {
  getToken(scopes: string[]): Promise<AccessToken | string | null | undefined>;
}

export interface ConfigurationStoreClientOptions {
  credential?: TokenCredential | (() => Promise<string | null | undefined>) | string;
  scopes?: string[];
  fetch?: typeof fetch;
}

export interface ConfigurationStoreEnvironmentOptions extends ConfigurationStoreClientOptions {
  serviceName?: string;
  environment?: Record<string, string | undefined>;
}

export interface SecretsVaultClientOptions extends ConfigurationStoreClientOptions {
}

export interface SecretsVaultEnvironmentOptions extends SecretsVaultClientOptions {
  vaultName?: string;
  environment?: Record<string, string | undefined>;
}

export interface CloudShellProfileCredentialOptions {
  configDirectory?: string;
  configPath?: string;
  profileName?: string;
  environment?: Record<string, string | undefined>;
}

export interface CloudShellIdentityCredentialOptions {
  tokenEndpoint?: string;
  clientId?: string;
  clientSecret?: string;
  scope?: string;
  defaultScope?: string;
  environment?: Record<string, string | undefined>;
  fetch?: typeof fetch;
}

export class StaticTokenCredential implements TokenCredential {
  public constructor(private readonly token: string) {
  }

  public async getToken(_scopes: string[] = []): Promise<AccessToken> {
    return { token: this.token };
  }
}

export class EnvironmentTokenCredential implements TokenCredential {
  public constructor(
    private readonly variableNames: string[] = [
      "CLOUDSHELL_CONFIGURATION_TOKEN",
      "CLOUDSHELL_SECRETS_TOKEN",
      "CLOUDSHELL_CONTROL_PLANE_TOKEN",
      "CLOUDSHELL_TOKEN"
    ],
    private readonly environment: Record<string, string | undefined> = process.env) {
  }

  public async getToken(_scopes: string[] = []): Promise<AccessToken | null> {
    for (const variableName of this.variableNames) {
      const token = this.environment[variableName];
      if (token && token.trim().length > 0) {
        return { token: token.trim() };
      }
    }

    return null;
  }
}

export class CloudShellIdentityCredential implements TokenCredential {
  public static readonly tokenEndpointEnvironmentVariable = "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT";
  public static readonly clientIdEnvironmentVariable = "CLOUDSHELL_IDENTITY_CLIENT_ID";
  public static readonly clientSecretEnvironmentVariable = "CLOUDSHELL_IDENTITY_CLIENT_SECRET";
  public static readonly scopeEnvironmentVariable = "CLOUDSHELL_IDENTITY_SCOPE";

  private readonly environment: Record<string, string | undefined>;
  private readonly fetchImpl: typeof fetch;
  private cachedToken?: AccessToken;

  public constructor(private readonly options: CloudShellIdentityCredentialOptions = {}) {
    this.environment = options.environment ?? process.env;
    this.fetchImpl = options.fetch ?? fetch;
  }

  public async getToken(scopes: string[] = []): Promise<AccessToken | null> {
    if (this.cachedToken?.expiresOnTimestamp &&
      this.cachedToken.expiresOnTimestamp > Date.now() + 60_000) {
      return this.cachedToken;
    }

    const tokenEndpoint = firstNotBlank(
      this.options.tokenEndpoint,
      this.environment[CloudShellIdentityCredential.tokenEndpointEnvironmentVariable]);
    const clientId = firstNotBlank(
      this.options.clientId,
      this.environment[CloudShellIdentityCredential.clientIdEnvironmentVariable]);
    const clientSecret = firstNotBlank(
      this.options.clientSecret,
      this.environment[CloudShellIdentityCredential.clientSecretEnvironmentVariable]);
    if (!tokenEndpoint || !clientId || !clientSecret) {
      return null;
    }

    const response = await this.fetchImpl(tokenEndpoint, {
      method: "POST",
      headers: {
        "content-type": "application/x-www-form-urlencoded"
      },
      body: new URLSearchParams({
        grant_type: "client_credentials",
        client_id: clientId,
        client_secret: clientSecret,
        scope: this.resolveScope(scopes)
      })
    });
    if (!response.ok) {
      const detail = await response.text();
      throw new Error(
        detail.trim().length === 0
          ? `CloudShell identity token endpoint returned ${response.status}.`
          : `CloudShell identity token endpoint returned ${response.status}. ${detail}`);
    }

    const token = await response.json() as CloudShellIdentityTokenResponse;
    if (!token.access_token || token.access_token.trim().length === 0) {
      throw new Error("CloudShell identity token endpoint returned no access token.");
    }

    this.cachedToken = {
      token: token.access_token,
      expiresOnTimestamp: token.expires_in === undefined
        ? undefined
        : Date.now() + Math.max(0, token.expires_in) * 1000
    };
    return this.cachedToken;
  }

  private resolveScope(scopes: string[]): string {
    return scopes.length > 0
      ? scopes.join(" ")
      : firstNotBlank(
        this.options.scope,
        this.environment[CloudShellIdentityCredential.scopeEnvironmentVariable],
        this.options.defaultScope,
        defaultConfigurationScope)!;
  }
}

export class CloudShellProfileCredential implements TokenCredential {
  public static readonly configDirectoryEnvironmentVariable = "CLOUDSHELL_CONFIG_DIR";
  public static readonly profileEnvironmentVariable = "CLOUDSHELL_PROFILE";
  public static readonly defaultConfigDirectoryName = ".cloudshell";
  public static readonly defaultConfigFileName = "config.json";

  public constructor(private readonly options: CloudShellProfileCredentialOptions = {}) {
  }

  public async getToken(_scopes: string[] = []): Promise<AccessToken | null> {
    const configPath = this.resolveConfigPath();
    const configuration = await readJsonFile<CloudShellProfileConfiguration>(configPath);
    if (!configuration) {
      return null;
    }

    const profileName = firstNotBlank(
      this.options.profileName,
      this.options.environment?.[CloudShellProfileCredential.profileEnvironmentVariable],
      configuration.activeProfile);
    if (!profileName) {
      return null;
    }

    const profile = findProfile(configuration.profiles, profileName);
    const credential = profile?.credential;
    if (!credential ||
      credential.kind?.toLowerCase() !== "staticbearer") {
      return null;
    }

    const expiresOnTimestamp = parseExpiresOn(credential.expiresOn);
    if (expiresOnTimestamp !== undefined && expiresOnTimestamp <= Date.now()) {
      return null;
    }

    const token = await this.resolveStaticBearerToken(credential, configPath);
    return token && token.trim().length > 0
      ? { token: token.trim(), expiresOnTimestamp }
      : null;
  }

  private resolveConfigPath(): string {
    return this.options.configPath ??
      join(this.resolveConfigDirectory(), CloudShellProfileCredential.defaultConfigFileName);
  }

  private resolveConfigDirectory(): string {
    return firstNotBlank(
      this.options.configDirectory,
      this.options.environment?.[CloudShellProfileCredential.configDirectoryEnvironmentVariable],
      join(homedir(), CloudShellProfileCredential.defaultConfigDirectoryName))!;
  }

  private async resolveStaticBearerToken(
    credential: CloudShellProfileCredentialDefinition,
    configPath: string): Promise<string | undefined> {
    if (credential.accessToken && credential.accessToken.trim().length > 0) {
      return credential.accessToken;
    }

    if (!credential.accessTokenPath || credential.accessTokenPath.trim().length === 0) {
      return undefined;
    }

    const tokenPath = isAbsolute(credential.accessTokenPath)
      ? credential.accessTokenPath
      : join(dirname(configPath), credential.accessTokenPath);
    return await readTextFile(tokenPath);
  }
}

export class DefaultCloudShellCredential implements TokenCredential {
  public constructor(
    private readonly credentials: TokenCredential[] = [
      new CloudShellIdentityCredential(),
      new EnvironmentTokenCredential(),
      new CloudShellProfileCredential()
    ]) {
  }

  public async getToken(scopes: string[]): Promise<AccessToken | string | null | undefined> {
    for (const credential of this.credentials) {
      const token = await credential.getToken(scopes);
      const tokenValue = typeof token === "string"
        ? token
        : token?.token;
      if (tokenValue && tokenValue.trim().length > 0) {
        return token;
      }
    }

    return null;
  }
}

export class ConfigurationStoreClient {
  private readonly credential: TokenCredential | (() => Promise<string | null | undefined>) | string;
  private readonly scopes: string[];
  private readonly fetchImpl: typeof fetch;

  public constructor(
    public readonly settingsEndpoint: string | URL,
    options: ConfigurationStoreClientOptions = {}) {
    this.credential = options.credential ?? new DefaultCloudShellCredential();
    this.scopes = options.scopes ?? [defaultConfigurationScope];
    this.fetchImpl = options.fetch ?? fetch;
  }

  public static fromEnvironment(
    options: ConfigurationStoreEnvironmentOptions = {}): ConfigurationStoreClient {
    const client = this.tryFromEnvironment(options);
    if (!client) {
      throw new Error("No CloudShell configuration store endpoint was found in the environment.");
    }

    return client;
  }

  public static tryFromEnvironment(
    options: ConfigurationStoreEnvironmentOptions = {}): ConfigurationStoreClient | undefined {
    const endpoint = findEndpoint(
      "CLOUDSHELL_CONFIGURATION_",
      options.serviceName,
      options.environment ?? process.env);
    return endpoint
      ? new ConfigurationStoreClient(endpoint, options)
      : undefined;
  }

  public async getSettings(): Promise<CloudShellConfigurationSetting[]> {
    const response = await this.send(new URL(this.settingsEndpoint));
    return await response.json() as CloudShellConfigurationSetting[];
  }

  public async getSetting(name: string): Promise<CloudShellConfigurationSetting | undefined> {
    assertNotBlank(name, "Configuration setting name is required.");

    const response = await this.send(this.buildSettingEndpoint(name));
    if (response.status === 404) {
      return undefined;
    }

    return await response.json() as CloudShellConfigurationSetting;
  }

  public buildSettingEndpoint(name: string): URL {
    assertNotBlank(name, "Configuration setting name is required.");

    const endpoint = new URL(this.settingsEndpoint);
    endpoint.pathname = `${endpoint.pathname.replace(/\/+$/, "")}/${encodeURIComponent(name)}`;
    return endpoint;
  }

  public async toObject(
    options: { mapPortableHierarchySeparator?: boolean } = {}): Promise<Record<string, string>> {
    const settings = await this.getSettings();
    const result: Record<string, string> = {};
    for (const setting of settings) {
      result[options.mapPortableHierarchySeparator === false
        ? setting.name
        : setting.name.replaceAll("--", ":")] = setting.value;
    }

    return result;
  }

  private async send(url: URL): Promise<Response> {
    const token = await this.getAccessToken();
    const response = await this.fetchImpl(url, {
      method: "GET",
      headers: {
        authorization: `Bearer ${token}`
      }
    });

    if (response.ok || response.status === 404) {
      return response;
    }

    const detail = await response.text();
    throw new Error(
      detail.trim().length === 0
        ? `CloudShell Configuration Store returned ${response.status}.`
        : `CloudShell Configuration Store returned ${response.status}. ${detail}`);
  }

  private async getAccessToken(): Promise<string> {
    const credential = this.credential;
    const token = typeof credential === "string"
      ? credential
      : typeof credential === "function"
        ? await credential()
        : await credential.getToken(this.scopes);

    const tokenValue = typeof token === "string"
      ? token
      : token?.token;
    if (!tokenValue || tokenValue.trim().length === 0) {
      throw new Error("CloudShell configuration credential returned no access token.");
    }

    return tokenValue.trim();
  }
}

export class SecretsVaultClient {
  private readonly credential: TokenCredential | (() => Promise<string | null | undefined>) | string;
  private readonly scopes: string[];
  private readonly fetchImpl: typeof fetch;

  public constructor(
    public readonly secretsEndpoint: string | URL,
    options: SecretsVaultClientOptions = {}) {
    this.credential = options.credential ?? new DefaultCloudShellCredential();
    this.scopes = options.scopes ?? [defaultConfigurationScope];
    this.fetchImpl = options.fetch ?? fetch;
  }

  public static fromEnvironment(
    options: SecretsVaultEnvironmentOptions = {}): SecretsVaultClient {
    const client = this.tryFromEnvironment(options);
    if (!client) {
      throw new Error("No CloudShell Secrets Vault endpoint was found in the environment.");
    }

    return client;
  }

  public static tryFromEnvironment(
    options: SecretsVaultEnvironmentOptions = {}): SecretsVaultClient | undefined {
    const endpoint = findEndpoint(
      "CLOUDSHELL_SECRETS_",
      options.vaultName,
      options.environment ?? process.env);
    return endpoint
      ? new SecretsVaultClient(endpoint, options)
      : undefined;
  }

  public async getSecrets(): Promise<CloudShellSecretProperties[]> {
    const response = await this.send(new URL(this.secretsEndpoint));
    return await response.json() as CloudShellSecretProperties[];
  }

  public async getSecret(
    name: string,
    options: { version?: string } = {}): Promise<CloudShellSecretValue | undefined> {
    assertNotBlank(name, "Secret name is required.");

    const response = await this.send(this.buildSecretEndpoint(name, options.version));
    if (response.status === 404) {
      return undefined;
    }

    return await response.json() as CloudShellSecretValue;
  }

  public buildSecretEndpoint(name: string, version?: string): URL {
    assertNotBlank(name, "Secret name is required.");

    const endpoint = new URL(this.secretsEndpoint);
    endpoint.pathname = `${endpoint.pathname.replace(/\/+$/, "")}/${encodeURIComponent(name)}`;
    if (version && version.trim().length > 0) {
      endpoint.searchParams.set("version", version.trim());
    }

    return endpoint;
  }

  private async send(url: URL): Promise<Response> {
    const token = await this.getAccessToken();
    const response = await this.fetchImpl(url, {
      method: "GET",
      headers: {
        authorization: `Bearer ${token}`
      }
    });

    if (response.ok || response.status === 404) {
      return response;
    }

    const detail = await response.text();
    throw new Error(
      detail.trim().length === 0
        ? `CloudShell Secrets Vault returned ${response.status}.`
        : `CloudShell Secrets Vault returned ${response.status}. ${detail}`);
  }

  private async getAccessToken(): Promise<string> {
    const credential = this.credential;
    const token = typeof credential === "string"
      ? credential
      : typeof credential === "function"
        ? await credential()
        : await credential.getToken(this.scopes);

    const tokenValue = typeof token === "string"
      ? token
      : token?.token;
    if (!tokenValue || tokenValue.trim().length === 0) {
      throw new Error("CloudShell secrets credential returned no access token.");
    }

    return tokenValue.trim();
  }
}

function findEndpoint(
  prefix: string,
  serviceName: string | undefined,
  environment: Record<string, string | undefined>): string | undefined {
  const normalizedServiceName = serviceName
    ? normalizeEnvironmentSegment(serviceName)
    : undefined;

  return Object.entries(environment)
    .filter(([key, value]) =>
      value &&
      key.toUpperCase().startsWith(prefix) &&
      key.toUpperCase().endsWith("_ENDPOINT") &&
      (!normalizedServiceName ||
        key.toUpperCase().includes(`${prefix}${normalizedServiceName}_`)))
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([, value]) => value)
    .find(value => isAbsoluteUrl(value));
}

function normalizeEnvironmentSegment(value: string): string {
  return value
    .trim()
    .split("")
    .map(character => /[a-z0-9]/i.test(character) ? character.toUpperCase() : "_")
    .join("")
    .replace(/^_+|_+$/g, "");
}

function isAbsoluteUrl(value: string | undefined): value is string {
  if (!value) {
    return false;
  }

  try {
    new URL(value);
    return true;
  } catch {
    return false;
  }
}

async function readJsonFile<T>(path: string): Promise<T | undefined> {
  const content = await readTextFile(path);
  return content === undefined
    ? undefined
    : JSON.parse(content) as T;
}

async function readTextFile(path: string): Promise<string | undefined> {
  try {
    return await readFile(path, "utf8");
  } catch (error) {
    const code = (error as { code?: string }).code;
    if (code === "ENOENT") {
      return undefined;
    }

    throw error;
  }
}

function firstNotBlank(...values: Array<string | undefined>): string | undefined {
  return values.find(value => value && value.trim().length > 0)?.trim();
}

function findProfile(
  profiles: Record<string, CloudShellProfile> | undefined,
  profileName: string): CloudShellProfile | undefined {
  if (!profiles) {
    return undefined;
  }

  return profiles[profileName] ??
    Object.entries(profiles)
      .find(([candidate]) => candidate.toLowerCase() === profileName.toLowerCase())?.[1];
}

function parseExpiresOn(value: string | undefined): number | undefined {
  if (!value || value.trim().length === 0) {
    return undefined;
  }

  const timestamp = Date.parse(value);
  if (Number.isNaN(timestamp)) {
    throw new Error(`CloudShell profile credential has an invalid expiresOn value '${value}'.`);
  }

  return timestamp;
}

function assertNotBlank(value: string, message: string): void {
  if (!value || value.trim().length === 0) {
    throw new Error(message);
  }
}

interface CloudShellProfileConfiguration {
  activeProfile?: string;
  profiles?: Record<string, CloudShellProfile>;
}

interface CloudShellProfile {
  controlPlane?: string;
  environment?: string;
  credential?: CloudShellProfileCredentialDefinition;
}

interface CloudShellProfileCredentialDefinition {
  kind?: string;
  accessToken?: string;
  accessTokenPath?: string;
  expiresOn?: string;
}

interface CloudShellIdentityTokenResponse {
  access_token?: string;
  expires_in?: number;
}
