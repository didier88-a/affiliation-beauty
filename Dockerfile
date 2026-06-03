# ============================================
# Build
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["wsaffiliation/wsaffiliation/wsaffiliation.csproj", "wsaffiliation/wsaffiliation/"]

RUN dotnet restore "wsaffiliation/wsaffiliation/wsaffiliation.csproj"

COPY . .

WORKDIR "/src/wsaffiliation/wsaffiliation"

RUN dotnet publish "wsaffiliation.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# ============================================
# Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .

# Installer les dépendances Playwright
RUN apt-get update && apt-get install -y \
    wget \
    curl \
    gnupg \
    ca-certificates \
    fonts-liberation \
    libasound2 \
    libatk-bridge2.0-0 \
    libatk1.0-0 \
    libcups2 \
    libdbus-1-3 \
    libdrm2 \
    libgbm1 \
    libgtk-3-0 \
    libnspr4 \
    libnss3 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxkbcommon0 \
    libxrandr2 \
    xdg-utils \
    && rm -rf /var/lib/apt/lists/*

# Installer Playwright CLI
RUN dotnet tool install --global Microsoft.Playwright.CLI

ENV PATH="${PATH}:/root/.dotnet/tools"

# Télécharger Chromium
RUN playwright install chromium

# Port Render
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "wsaffiliation.dll"]