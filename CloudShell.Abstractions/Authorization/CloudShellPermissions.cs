namespace CloudShell.Abstractions.Authorization;

public static class CloudShellPermissions
{
    public const string All = "*";

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
    }
}
