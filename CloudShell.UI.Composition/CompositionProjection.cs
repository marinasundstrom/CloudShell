namespace CloudShell.UI.Composition;

public sealed record CompositionPageProjection(
    CompositionModuleId ModuleId,
    CompositionPageRegistration Page);

public sealed record CompositionMenuProjection(
    CompositionModuleId ModuleId,
    CompositionMenuRegistration Menu);

public sealed record CompositionSectionOutletProjection(
    CompositionModuleId ModuleId,
    CompositionSectionOutletRegistration Outlet);

public sealed record CompositionSectionProjection(
    CompositionModuleId ModuleId,
    CompositionSectionRegistration Section);
