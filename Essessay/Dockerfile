# Build context is the repository root (see compose.yaml), so paths here are
# repo-relative.
#
# .NET 9 variant: MapStaticAssets() exists here (added in .NET 9), unlike .NET
# 8. Testing whether the discovery gap (see README,
# https://github.com/dotnet/aspnetcore/issues/68641) is already user-visible
# on .NET 9, or only from .NET 10 onward.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Essessay/Essessay.csproj Essessay/
RUN dotnet restore Essessay/Essessay.csproj

COPY Essessay/ Essessay/
RUN dotnet publish Essessay/Essessay.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "Essessay.dll"]
