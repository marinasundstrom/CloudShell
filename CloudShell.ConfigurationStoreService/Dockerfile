ARG DOTNET_VERSION=11.0-preview
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY . .
RUN dotnet publish CloudShell.ConfigurationStoreService/CloudShell.ConfigurationStoreService.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}
ARG IMAGE_VERSION=local
WORKDIR /app
COPY --from=build /app/publish .
LABEL org.opencontainers.image.source="https://github.com/marinasundstrom/CloudShell" \
      org.opencontainers.image.description="CloudShell Configuration Store service" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${IMAGE_VERSION}"
EXPOSE 8080
ENTRYPOINT ["dotnet", "CloudShell.ConfigurationStoreService.dll"]
