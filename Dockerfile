FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Digital_Pharma/Digital_Pharma.csproj", "Digital_Pharma/"]
RUN dotnet restore "Digital_Pharma/Digital_Pharma.csproj"

COPY . .
RUN dotnet publish "Digital_Pharma/Digital_Pharma.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish .
COPY docker-entrypoint.sh /usr/local/bin/digital-pharma-entrypoint
RUN sed -i 's/\r$//' /usr/local/bin/digital-pharma-entrypoint \
    && chmod 0755 /usr/local/bin/digital-pharma-entrypoint \
    && command -v setpriv

USER $APP_UID
ENTRYPOINT ["/usr/local/bin/digital-pharma-entrypoint"]
