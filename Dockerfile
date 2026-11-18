# syntax=docker/dockerfile:1

# MatGame with its games. The games come from the sibling repositories as named build contexts:
#   docker build --build-context bunnywarfare=../BunnyWarfare --build-context kingbee=../KingBee --build-context turbotoys=../TurboToys -t matgame:local .
# (docker-compose.local.yml and the CI pass them; the folder layout /src/MatGame, /src/BunnyWarfare, /src/KingBee, /src/TurboToys keeps the relative ProjectReferences).
# GAMES chooses the games compiled in: "all" (default), "none", or a list like "bunnywarfare;kingbee" (MSBuild property MatGameGames).

# ---------------------------------------------------------------------------
# King Bee's browser part (Vite: base /kingbee/, output server/KingBee.Module/wwwroot)
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM node:24-alpine AS kingbee-web
ARG GAMES=all
ARG GIT_SHA=dev
ENV GIT_SHA=$GIT_SHA
WORKDIR /src/KingBee
COPY --from=kingbee . .
RUN rm -rf node_modules && \
    if [ "$GAMES" = "none" ] || ! echo ";$GAMES;" | grep -Eq ";(all|kingbee);"; then \
        mkdir -p server/KingBee.Module/wwwroot; \
    else \
        npm ci && npm run build; \
    fi && rm -rf node_modules

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
# The build runs on the builder's own platform: the published app is portable (no native host), so the arm64 image needs no emulation here.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG Channel=local
ARG BuildNumber=0
ARG BuildDate=
ARG GAMES=all
WORKDIR /src

COPY . MatGame/
COPY --from=bunnywarfare . BunnyWarfare/
COPY --from=kingbee-web /src/KingBee KingBee/
COPY --from=turbotoys . TurboToys/
# build output of a developer's machine (Windows paths in obj/) must never reach the restore
RUN find . -type d \( -name bin -o -name obj -o -name node_modules -o -name .git \) -prune -exec rm -rf {} + && \
    dotnet restore "MatGame/src/MatGame/MatGame.csproj" -p:MatGameGames="$GAMES"

# Fall back to today's date when BuildDate is not supplied, so the version never ends in "-".
RUN BUILD_DATE="${BuildDate:-$(date -u +%Y%m%d)}" && \
    dotnet publish "MatGame/src/MatGame/MatGame.csproj" \
    -c Release -o /app/publish --no-restore \
    -p:UseAppHost=false -p:MatGameGames="$GAMES" \
    -p:Channel=$Channel -p:BuildNumber=$BuildNumber -p:BuildDate=$BUILD_DATE

# ---------------------------------------------------------------------------
# Runtime stage (ASP.NET Core base image)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL org.opencontainers.image.title="MatGame" \
      org.opencontainers.image.description="Der Server der Mat-Spiele: Portal, Konten und Online-Partien (BunnyWarfare, King Bee, TurboToys) mit Administration" \
      org.opencontainers.image.source="https://github.com/Real-TTX/MatGame"

WORKDIR /app

ENV HTTP_PORTS="" \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    TZ=Europe/Berlin

EXPOSE 2770

COPY --from=build /app/publish .

# One volume for what the app keeps in files: the keys that protect the sign-in cookies, the bindings, the games' folders. The database is the other container.
RUN mkdir -p /data/keys /data/games && chown -R $APP_UID:$APP_UID /data
VOLUME /data
USER $APP_UID

# The app checks itself on /api/health (the Mat platform contract), so the image needs no curl.
HEALTHCHECK --interval=30s --timeout=8s --start-period=40s --retries=3 \
    CMD ["dotnet", "MatGame.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "MatGame.dll"]
