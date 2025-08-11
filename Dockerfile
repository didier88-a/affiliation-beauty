# Base image pour l'exécution
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Image de build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copier et restaurer
COPY ["wsaffiliation/wsaffiliation/wsaffiliation.csproj", "wsaffiliation/wsaffiliation/"]
RUN dotnet restore "wsaffiliation/wsaffiliation/wsaffiliation.csproj"

# Copier tout le code
COPY . .
WORKDIR "/src/wsaffiliation/wsaffiliation"
RUN dotnet build "wsaffiliation.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publier
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "wsaffiliation.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Image finale
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "wsaffiliation.dll"]
