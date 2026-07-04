export const defaultConfigurationScope = "ControlPlane.Access";

export interface CloudShellConfigurationSetting {
  name: string;
  value: string;
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

export class StaticTokenCredential implements TokenCredential {
  public constructor(private readonly token: string) {
  }

  public async getToken(): Promise<AccessToken> {
    return { token: this.token };
  }
}

export class EnvironmentTokenCredential implements TokenCredential {
  public constructor(
    private readonly variableNames: string[] = [
      "CLOUDSHELL_CONFIGURATION_TOKEN",
      "CLOUDSHELL_CONTROL_PLANE_TOKEN",
      "CLOUDSHELL_TOKEN"
    ],
    private readonly environment: Record<string, string | undefined> = process.env) {
  }

  public async getToken(): Promise<AccessToken | null> {
    for (const variableName of this.variableNames) {
      const token = this.environment[variableName];
      if (token && token.trim().length > 0) {
        return { token: token.trim() };
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
    this.credential = options.credential ?? new EnvironmentTokenCredential();
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

function findEndpoint(
  serviceName: string | undefined,
  environment: Record<string, string | undefined>): string | undefined {
  const normalizedServiceName = serviceName
    ? normalizeEnvironmentSegment(serviceName)
    : undefined;

  return Object.entries(environment)
    .filter(([key, value]) =>
      value &&
      key.toUpperCase().startsWith("CLOUDSHELL_CONFIGURATION_") &&
      key.toUpperCase().endsWith("_ENDPOINT") &&
      (!normalizedServiceName ||
        key.toUpperCase().includes(`CLOUDSHELL_CONFIGURATION_${normalizedServiceName}_`)))
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

function assertNotBlank(value: string, message: string): void {
  if (!value || value.trim().length === 0) {
    throw new Error(message);
  }
}
