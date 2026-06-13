namespace CloudShell.Abstractions.Authorization;

public static class CommonResourceOperationPermissions
{
    public const string LifecycleAction =
        "CloudShell.Resources/resources/lifecycle/action";

    public const string ExecuteCustomAction =
        "CloudShell.Resources/resources/actions/execute/action";
}

public static class NetworkResourceOperationPermissions
{
    public const string ReconcileEndpointMappings =
        "CloudShell.Network/networks/reconcileEndpointMappings/action";
}

public static class LoadBalancerResourceOperationPermissions
{
    public const string ApplyConfiguration =
        "CloudShell.Network/loadBalancers/applyConfiguration/action";
}

public static class ConfigurationStoreResourceOperationPermissions
{
    public const string ReadEntries =
        "CloudShell.Configuration/stores/entries/read/action";
}

public static class SecretsVaultResourceOperationPermissions
{
    public const string ReadSecrets =
        "CloudShell.Secrets/vaults/secrets/read/action";
}

public static class ResourceIdentityProvisioningOperationPermissions
{
    public const string ProvisionIdentities =
        "CloudShell.Identity/provisioningServices/identities/provision/action";
}

public static class CloudShellPermissions
{
    public const string All = "*";

    public static class Shell
    {
        public const string Read = "shell.read";
        public const string Configure = "shell.configure";
    }

    public static class ResourceGroups
    {
        public const string Read = "resource-groups.read";
        public const string Create = "resource-groups.create";
        public const string Manage = "resource-groups.manage";
    }

    public static class Resources
    {
        public const string Read = "resources.read";
        public const string Create = "resources.create";
        public const string Manage = "resources.manage";

        public static class Actions
        {
            public const string Lifecycle = CommonResourceOperationPermissions.LifecycleAction;
            public const string Execute = CommonResourceOperationPermissions.ExecuteCustomAction;
        }
    }

    public static class Network
    {
        public static class Actions
        {
            public const string ReconcileEndpointMappings =
                NetworkResourceOperationPermissions.ReconcileEndpointMappings;
            public const string ApplyLoadBalancerConfiguration =
                LoadBalancerResourceOperationPermissions.ApplyConfiguration;
        }
    }

    public static class Secrets
    {
        public static class Actions
        {
            public const string Read =
                SecretsVaultResourceOperationPermissions.ReadSecrets;
        }
    }

    public static class Configuration
    {
        public static class Actions
        {
            public const string Read =
                ConfigurationStoreResourceOperationPermissions.ReadEntries;
        }
    }

    public static class Identity
    {
        public static class Actions
        {
            public const string ProvisionIdentities =
                ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities;
        }
    }
}
