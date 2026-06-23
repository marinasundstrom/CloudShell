namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceRunningStateOperations(
    IApplicationResourceDefinitionSource definitions,
    LocalProcessRunner localProcesses,
    ApplicationContainerProcessTracker containerProcesses) : IApplicationResourceRunningStateOperations
{
    public bool IsRunning(string applicationId)
    {
        var application = definitions.GetApplication(applicationId);
        if (application is null)
        {
            return false;
        }

        return ApplicationResourceProjectionSupport.IsContainerBacked(application)
            ? containerProcesses.IsRunning(application)
            : localProcesses.IsRunning(ApplicationProcessDefinitions.Create(application));
    }
}
