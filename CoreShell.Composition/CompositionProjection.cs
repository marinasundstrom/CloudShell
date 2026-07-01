namespace CoreShell.Composition;

public sealed record CompositionPageProjection(
    CompositionModuleId ModuleId,
    CompositionPageRegistration Page);

public sealed record CompositionMenuProjection(
    CompositionModuleId ModuleId,
    CompositionMenuRegistration Menu);

public sealed record CompositionMenuItemProjection(
    CompositionModuleId ModuleId,
    CompositionMenuRegistration Menu,
    CompositionMenuGroupRegistration? Group,
    CompositionMenuItemRegistration Item);

public sealed record CompositionSectionOutletProjection(
    CompositionModuleId ModuleId,
    CompositionSectionOutletRegistration Outlet);

public sealed record CompositionSectionProjection(
    CompositionModuleId ModuleId,
    CompositionSectionRegistration Section);
