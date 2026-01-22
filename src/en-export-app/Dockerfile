
# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /out

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Install PowerShell, using the correct Microsoft repo for the base OS
# (works across Debian/Ubuntu variants used by dotnet images)
RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends wget curl gnupg apt-transport-https ca-certificates; \
    . /etc/os-release; \
    wget -q "https://packages.microsoft.com/config/${ID}/${VERSION_ID}/packages-microsoft-prod.deb"; \
    dpkg -i packages-microsoft-prod.deb; rm packages-microsoft-prod.deb; \
    apt-get update; \
    apt-get install -y --no-install-recommends powershell; \
    rm -rf /var/lib/apt/lists/*

# App
COPY --from=build /out .

# Install Playwright browsers + OS deps in the runtime image
# (this is the Playwright .NET recommended flow)
RUN pwsh ./playwright.ps1 install --with-deps chromium

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","en-export-app.dll"]
