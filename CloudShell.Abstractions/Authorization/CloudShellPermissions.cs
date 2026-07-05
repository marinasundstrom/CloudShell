namespace CloudShell.Abstractions.Authorization;

public static class CloudShellAuthorizationClaimTypes
{
    public const string Permission = "cloudshell.permission";
    public const string ResourceGroup = "cloudshell.resource-group";
    public const string Resource = "cloudshell.resource";
    public const string ResourcePermission = "cloudshell.resource-permission";
    public const char ResourcePermissionSeparator = '\u001f';
}

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

    public const string ReconcileNameMappings =
        "CloudShell.Network/dnsZones/reconcileNameMappings/action";
}

public static class LoadBalancerResourceOperationPermissions
{
    public const string ApplyConfiguration =
        "CloudShell.Network/loadBalancers/applyConfiguration/action";
}

public static class StorageVolumeResourceOperationPermissions
{
    public const string MountRead =
        "CloudShell.Storage/volumes/mount/read/action";

    public const string MountWrite =
        "CloudShell.Storage/volumes/mount/write/action";
}

public static class ConfigurationStoreResourceOperationPermissions
{
    public const string ReadSettings =
        "CloudShell.Configuration/stores/settings/read/action";
}

public static class SecretsVaultResourceOperationPermissions
{
    public const string ReadSecrets =
        "CloudShell.Secrets/vaults/secrets/read/action";
}

public static class DeviceRegistryResourceOperationPermissions
{
    public const string EnrollDevices =
        "CloudShell.IoT/deviceRegistries/devices/enroll/action";

    public const string ManageDevices =
        "CloudShell.IoT/deviceRegistries/devices/manage/action";
}

public static class DatabaseResourceOperationPermissions
{
    public const string ReadWrite =
        "CloudShell.Database/databases/readWrite/action";

    public const string ReconcileAccess =
        "CloudShell.Database/databases/access/reconcile/action";
}

public static class RabbitMQResourceOperationPermissions
{
    public const string Publish =
        "CloudShell.Messaging/rabbitMQ/publish/action";

    public const string Consume =
        "CloudShell.Messaging/rabbitMQ/consume/action";

    public const string Configure =
        "CloudShell.Messaging/rabbitMQ/configure/action";

    public const string ReconcileAccess =
        "CloudShell.Messaging/rabbitMQ/access/reconcile/action";
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
        public const string Reference = "resources.reference";
        public const string Read = "resources.read";
        public const string ReadRuntimeManaged = "resources.runtime-managed.read";
        public const string Create = "resources.create";
        public const string Manage = "resources.manage";

        public static class Actions
        {
            public const string Lifecycle = CommonResourceOperationPermissions.LifecycleAction;
            public const string Execute = CommonResourceOperationPermissions.ExecuteCustomAction;
        }
    }

    public static class Observability
    {
        public const string Read = "observability.read";

        public static class Logs
        {
            public const string Read = "observability.logs.read";
        }

        public static class Traces
        {
            public const string Read = "observability.traces.read";
        }

        public static class Metrics
        {
            public const string Read = "observability.metrics.read";
        }
    }

    public static class Usage
    {
        public const string Read = "usage.read";
    }

    public static class Network
    {
        public static class Actions
        {
            public const string ReconcileEndpointMappings =
                NetworkResourceOperationPermissions.ReconcileEndpointMappings;
            public const string ReconcileNameMappings =
                NetworkResourceOperationPermissions.ReconcileNameMappings;
            public const string ApplyLoadBalancerConfiguration =
                LoadBalancerResourceOperationPermissions.ApplyConfiguration;
        }
    }

    public static class Storage
    {
        public static class Actions
        {
            public const string MountRead =
                StorageVolumeResourceOperationPermissions.MountRead;
            public const string MountWrite =
                StorageVolumeResourceOperationPermissions.MountWrite;
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
                ConfigurationStoreResourceOperationPermissions.ReadSettings;
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

    public static class IoT
    {
        public static class Actions
        {
            public const string EnrollDevices =
                DeviceRegistryResourceOperationPermissions.EnrollDevices;

            public const string ManageDevices =
                DeviceRegistryResourceOperationPermissions.ManageDevices;
        }
    }

    public static class Database
    {
        public static class Actions
        {
            public const string ReadWrite =
                DatabaseResourceOperationPermissions.ReadWrite;
        }
    }

    public static class Messaging
    {
        public static class RabbitMQ
        {
            public static class Actions
            {
                public const string Publish =
                    RabbitMQResourceOperationPermissions.Publish;

                public const string Consume =
                    RabbitMQResourceOperationPermissions.Consume;

                public const string Configure =
                    RabbitMQResourceOperationPermissions.Configure;

                public const string ReconcileAccess =
                    RabbitMQResourceOperationPermissions.ReconcileAccess;
            }
        }
    }
}
