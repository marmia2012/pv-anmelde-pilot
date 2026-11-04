FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/PvAutopilot.Host/PvAutopilot.Host.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app .
# Instanz-Konfigurationen (ohne Secrets); Auswahl über Instance__ConfigFile.
COPY config/instances ./config/instances
# Chromium + Systemabhängigkeiten für Playwright (Netze-Duisburg-Portal, MaStR).
RUN dotnet PvAutopilot.Host.dll playwright-install && rm -rf /var/lib/apt/lists/* && chmod -R a+rX /ms-playwright
# Nicht als root laufen; das aspnet-Image bringt den Benutzer „app“ mit.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PvAutopilot.Host.dll"]
