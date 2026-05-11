FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 6001 6002 6003 6004

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY ["src/UsBankSystem.MockGateways/UsBankSystem.MockGateways.csproj", "src/UsBankSystem.MockGateways/"]
COPY ["src/UsBankSystem.Api/payment-config.json", "src/UsBankSystem.Api/"]
RUN dotnet restore "src/UsBankSystem.MockGateways/UsBankSystem.MockGateways.csproj"
COPY . .
RUN dotnet publish "src/UsBankSystem.MockGateways/UsBankSystem.MockGateways.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UsBankSystem.MockGateways.dll"]
