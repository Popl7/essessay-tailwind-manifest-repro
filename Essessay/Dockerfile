# Build context is the repository root (see compose.yaml), so paths here are
# repo-relative.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project file alone: editing a .cs or .cshtml then keeps
# this layer, which is the slow one.
COPY Essessay/Essessay.csproj Essessay/
RUN dotnet restore Essessay/Essessay.csproj

COPY Essessay/ Essessay/
# Publishing runs the Tailwind target, which downloads the pinned standalone CLI
# for this image's architecture and generates wwwroot/css/site.css. Still no Node.
RUN dotnet publish Essessay/Essessay.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# The image ships no app.db; the migration in Program.cs creates it on startup.
# It needs somewhere writable to do that, and /app is root-owned while the app
# runs as $APP_UID — SQLite's "attempt to write a readonly database" otherwise.
#
# The database is therefore per-container and dies with it, which also means
# `--scale essessay=3` gives you three separate sets of user accounts. The board
# is shared because it lives in Redis; Identity is not. A real deployment points
# ConnectionStrings__DefaultConnection at something the instances share.
RUN mkdir -p /data && chown $APP_UID /data
ENV ConnectionStrings__DefaultConnection="DataSource=/data/app.db;Cache=Shared"

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "Essessay.dll"]
