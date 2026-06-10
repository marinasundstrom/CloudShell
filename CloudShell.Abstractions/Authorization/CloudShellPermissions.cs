namespace CloudShell.Abstractions.Authorization;

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
            public const string Lifecycle = "CloudShell.Resources/resources/lifecycle/action";
            public const string Execute = "CloudShell.Resources/resources/actions/execute/action";
        }
    }

    public static class Network
    {
        public static class Actions
        {
            public const string ReconcileEndpointMappings =
                "CloudShell.Network/networks/reconcileEndpointMappings/action";
            public const string ApplyLoadBalancerConfiguration =
                "CloudShell.Network/loadBalancers/applyConfiguration/action";
        }
    }
}
