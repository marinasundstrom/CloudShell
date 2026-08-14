ARG DOTNET_VERSION=11.0-preview
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY . .
RUN dotnet publish CloudShell.DeviceRegistryService/CloudShell.DeviceRegistryService.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
EXPOSE 1883
ENTRYPOINT ["dotnet", "CloudShell.DeviceRegistryService.dll"]
