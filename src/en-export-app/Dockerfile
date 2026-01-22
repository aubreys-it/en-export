
# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project
COPY . .

RUN dotnet restore
RUN dotnet publish -c Release -o /out

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy published output
COPY --from=build /out .

# Install Playwright browsers
RUN dotnet tool install --global Microsoft.Playwright.CLI \
 && /root/.dotnet/tools/playwright install --with-deps chromium

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "en-export-app.dll"]
