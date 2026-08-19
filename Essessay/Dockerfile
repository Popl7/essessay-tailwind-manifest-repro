# Build context is the repository root (see compose.yaml), so paths here are
# repo-relative.
#
# .NET 8 variant: testing whether the BeforeBuild-vs-static-web-assets race
# (see README, https://github.com/dotnet/aspnetcore/issues/68641) predates
# .NET 9's MapStaticAssets()/staticwebassets.endpoints.json manifest, or is
# specific to that newer pipeline.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Essessay/Essessay.csproj Essessay/
RUN dotnet restore Essessay/Essessay.csproj

COPY Essessay/ Essessay/
RUN dotnet publish Essessay/Essessay.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "Essessay.dll"]
