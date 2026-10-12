# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/OnlineBanking.Web/OnlineBanking.Web.csproj src/OnlineBanking.Web/
RUN dotnet restore src/OnlineBanking.Web/OnlineBanking.Web.csproj
COPY src/OnlineBanking.Web/ src/OnlineBanking.Web/
RUN dotnet publish src/OnlineBanking.Web/OnlineBanking.Web.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# SQLite file lives in /app/data (writable by the non-root app user)
RUN mkdir -p /app/data && chown -R $APP_UID /app/data
USER $APP_UID
# FORWARDEDHEADERS_ENABLED: trust X-Forwarded-Proto/For from the hosting proxy (Render terminates HTTPS),
# so the app knows requests are https (secure cookies, correct redirects)
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ConnectionStrings__Default="Data Source=/app/data/onlinebanking.db"
EXPOSE 8080
HEALTHCHECK NONE
ENTRYPOINT ["dotnet", "OnlineBanking.Web.dll"]
