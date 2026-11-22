# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching)
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/AtlasLiterario.Domain/AtlasLiterario.Domain.csproj src/AtlasLiterario.Domain/
COPY src/AtlasLiterario.Application/AtlasLiterario.Application.csproj src/AtlasLiterario.Application/
COPY src/AtlasLiterario.Infrastructure/AtlasLiterario.Infrastructure.csproj src/AtlasLiterario.Infrastructure/
COPY src/AtlasLiterario.API/AtlasLiterario.API.csproj src/AtlasLiterario.API/
RUN dotnet restore src/AtlasLiterario.API/AtlasLiterario.API.csproj

COPY src/ src/
RUN dotnet publish src/AtlasLiterario.API/AtlasLiterario.API.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=5080
EXPOSE 5080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AtlasLiterario.API.dll"]
