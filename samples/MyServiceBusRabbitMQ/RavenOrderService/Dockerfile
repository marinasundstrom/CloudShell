FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build

WORKDIR /src
COPY RavenOrderService ./RavenOrderService
RUN dotnet publish RavenOrderService/OrderIntake.rvnproj \
    --configuration Release \
    --output /app/publish \
    --property WarningLevel=0

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview

WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080

ENTRYPOINT ["dotnet", "OrderFulfillment.OrderIntake.dll"]
