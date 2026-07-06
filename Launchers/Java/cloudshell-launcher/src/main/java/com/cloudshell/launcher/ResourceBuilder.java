package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.property;

import com.cloudshell.launcher.JsonSupport.ResourceReferenceValue;
import java.util.ArrayList;
import java.util.List;

public abstract class ResourceBuilder<T extends ResourceBuilder<T>> {
    private final String name;
    private String type;
    private String providerId;
    private final List<ResourceReferenceValue> dependencies = new ArrayList<>();
    private final List<ResourceIdentityGrant> accessGrants = new ArrayList<>();
    private String resourceId;
    private String displayName;
    private String identityKind;
    private String identityProviderId;
    private String identityName;
    private String identitySubject;
    private Boolean provisionIdentityOnStartup;

    protected ResourceBuilder(String name, String type, String providerId) {
        this.name = name;
        this.type = type;
        this.providerId = providerId;
        this.resourceId = type + ":" + name;
    }

    public final String name() {
        return name;
    }

    public final String type() {
        return type;
    }

    public final String providerId() {
        return providerId;
    }

    public final String resourceId() {
        return resourceId;
    }

    public final String displayName() {
        return displayName;
    }

    public T withResourceId(String resourceId) {
        this.resourceId = resourceId;
        return self();
    }

    public T withDisplayName(String displayName) {
        this.displayName = displayName;
        return self();
    }

    public T dependsOn(ResourceBuilder<?> resource) {
        dependencies.add(JsonSupport.dependsOn(resource));
        return self();
    }

    public T dependsOn(String resourceId) {
        dependencies.add(JsonSupport.dependsOn(resourceId));
        return self();
    }

    public T withIdentity(String providerId) {
        return withIdentity(providerId, null, null);
    }

    public T withIdentity(String providerId, String name) {
        return withIdentity(providerId, name, null);
    }

    public T withIdentity(String providerId, String name, String subject) {
        identityKind = "provider";
        identityProviderId = trimToNull(providerId);
        identityName = trimToNull(name);
        identitySubject = trimToNull(subject);
        return self();
    }

    public T requireIdentity() {
        return requireIdentity(null);
    }

    public T requireIdentity(String name) {
        identityKind = "required";
        identityName = trimToNull(name);
        return self();
    }

    public T provisionIdentityOnStartup() {
        return provisionIdentityOnStartup(true);
    }

    public T provisionIdentityOnStartup(boolean provision) {
        provisionIdentityOnStartup = provision;
        return self();
    }

    public T allowResourceIdentity(ResourceBuilder<?> resource, String permission) {
        return allowResourceIdentity(resource, permission, null, null, null);
    }

    public T allowResourceIdentity(ResourceBuilder<?> resource, String permission, String identityName) {
        return allowResourceIdentity(resource, permission, identityName, null, null);
    }

    public T allowResourceIdentity(
        ResourceBuilder<?> resource,
        String permission,
        String identityName,
        String displayName,
        String providerId) {
        if (resource == null) {
            throw new IllegalArgumentException("Resource is required.");
        }

        String sourceResourceId = resource.resourceId();
        String normalizedIdentityName = trimToNull(identityName);
        String principalId = normalizedIdentityName == null
            ? sourceResourceId
            : sourceResourceId + "/identities/" + normalizedIdentityName;
        accessGrants.add(new ResourceIdentityGrant(
            principalId,
            sourceResourceId,
            normalizedIdentityName,
            trimToNull(displayName),
            trimToNull(providerId),
            requireNotBlank(permission, "Permission")));
        return self();
    }

    abstract String toJson(int indent);

    protected abstract T self();

    protected final void projectAsContainerApp() {
        String previousDefaultResourceId = type + ":" + name;
        type = "application.container-app";
        providerId = "applications.container-app";
        if (resourceId == null || resourceId.equalsIgnoreCase(previousDefaultResourceId)) {
            resourceId = type + ":" + name;
        }
    }

    protected final void appendCommon(StringBuilder builder, int indent) {
        property(builder, indent, "name", json(name), true);
        property(builder, indent, "type", json(type), true);
        property(builder, indent, "resourceId", json(resourceId), true);
        property(builder, indent, "providerId", json(providerId), true);
        if (displayName != null) {
            property(builder, indent, "displayName", json(displayName), true);
        }

        if (!dependencies.isEmpty()) {
            JsonSupport.appendResourceReferences(builder, indent, "dependsOn", dependencies, true);
        }

        if (hasDeclarationAttributes()) {
            appendDeclarationAttributes(builder, indent, true);
        }
    }

    private static String requireNotBlank(String value, String name) {
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(name + " is required.");
        }

        return value.trim();
    }

    private static String trimToNull(String value) {
        if (value == null || value.isBlank()) {
            return null;
        }

        return value.trim();
    }

    private boolean hasDeclarationAttributes() {
        return identityKind != null || provisionIdentityOnStartup != null || !accessGrants.isEmpty();
    }

    private int declarationAttributeCount() {
        int count = 0;
        if (identityKind != null) {
            count++;
        }

        if (identityProviderId != null) {
            count++;
        }

        if (identityName != null) {
            count++;
        }

        if (identitySubject != null) {
            count++;
        }

        if (provisionIdentityOnStartup != null) {
            count++;
        }

        if (!accessGrants.isEmpty()) {
            count++;
        }

        return count;
    }

    private void appendDeclarationAttributes(StringBuilder builder, int indent, boolean trailingComma) {
        int remaining = declarationAttributeCount();
        JsonSupport.line(builder, indent, "\"attributes\": {");
        if (identityKind != null) {
            property(builder, indent + 1, "identity.kind", json(identityKind), --remaining > 0);
        }

        if (identityProviderId != null) {
            property(builder, indent + 1, "identity.providerId", json(identityProviderId), --remaining > 0);
        }

        if (identityName != null) {
            property(builder, indent + 1, "identity.name", json(identityName), --remaining > 0);
        }

        if (identitySubject != null) {
            property(builder, indent + 1, "identity.subject", json(identitySubject), --remaining > 0);
        }

        if (provisionIdentityOnStartup != null) {
            property(
                builder,
                indent + 1,
                "identity.provisionOnStartup",
                provisionIdentityOnStartup.toString(),
                --remaining > 0);
        }

        if (!accessGrants.isEmpty()) {
            appendAccessGrants(builder, indent + 1);
        }

        JsonSupport.line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    private void appendAccessGrants(StringBuilder builder, int indent) {
        JsonSupport.line(builder, indent, "\"access.grants\": [");
        for (int index = 0; index < accessGrants.size(); index++) {
            ResourceIdentityGrant grant = accessGrants.get(index);
            JsonSupport.line(builder, indent + 1, "{");
            JsonSupport.line(builder, indent + 2, "\"principal\": {");
            property(builder, indent + 3, "kind", json("resourceIdentity"), true);
            property(builder, indent + 3, "id", json(grant.principalId()), true);
            if (grant.displayName() != null) {
                property(builder, indent + 3, "displayName", json(grant.displayName()), true);
            }

            if (grant.providerId() != null) {
                property(builder, indent + 3, "providerId", json(grant.providerId()), true);
            }

            property(builder, indent + 3, "sourceResourceId", json(grant.sourceResourceId()), grant.identityName() != null);
            if (grant.identityName() != null) {
                property(builder, indent + 3, "sourceIdentityName", json(grant.identityName()), false);
            }

            JsonSupport.line(builder, indent + 2, "},");
            property(builder, indent + 2, "permission", json(grant.permission()), false);
            JsonSupport.line(builder, indent + 1, "}" + (index < accessGrants.size() - 1 ? "," : ""));
        }

        JsonSupport.line(builder, indent, "]");
    }

    private record ResourceIdentityGrant(
        String principalId,
        String sourceResourceId,
        String identityName,
        String displayName,
        String providerId,
        String permission) {
    }
}
