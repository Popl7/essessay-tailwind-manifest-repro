# Build context is the repository root (see compose.yaml), so paths here are
# repo-relative.
#
# .NET 11 (preview) variant: testing whether the discovery gap (see README,
# https://github.com/dotnet/aspnetcore/issues/68641), confirmed present and
# user-visible on .NET 9 and .NET 10, is still there on the next release too.

FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

COPY Essessay/Essessay.csproj Essessay/
RUN dotnet restore Essessay/Essessay.csproj

COPY Essessay/ Essessay/
RUN dotnet publish Essessay/Essessay.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "Essessay.dll"]
