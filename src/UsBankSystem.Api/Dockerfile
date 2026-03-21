FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Kopiuj pliki projektów i przywróć zależności (osobna warstwa — cache)
COPY src/UsBankSystem.Api/UsBankSystem.Api.csproj src/UsBankSystem.Api/
COPY src/UsBankSystem.Core/UsBankSystem.Core.csproj src/UsBankSystem.Core/
COPY src/UsBankSystem.Infrastructure/UsBankSystem.Infrastructure.csproj src/UsBankSystem.Infrastructure/
RUN dotnet restore src/UsBankSystem.Api/UsBankSystem.Api.csproj

# Kopiuj resztę i zbuduj
COPY src/ src/
RUN dotnet publish src/UsBankSystem.Api/UsBankSystem.Api.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "UsBankSystem.Api.dll"]