#!/usr/bin/env sh
# Rebuilds MatGame with its games (../BunnyWarfare, ../KingBee) and starts the local stack (docker-compose.local.yml).
#   ./scripts/redeploy.sh [--clean] [--games all|none|bunnywarfare;kingbee]
set -e
cd "$(dirname "$0")/.."
GAMES=all
CLEAN=0
while [ $# -gt 0 ]; do
  case "$1" in
    --clean) CLEAN=1 ;;
    --games) shift; GAMES="$1" ;;
  esac
  shift
done
if [ "$CLEAN" = 1 ]; then docker compose -f docker-compose.local.yml down -v --remove-orphans; fi
docker compose -f docker-compose.local.yml build --build-arg "GAMES=$GAMES" --build-arg "BuildDate=$(date -u +%Y%m%d)" web
docker compose -f docker-compose.local.yml up -d --remove-orphans
echo "MatGame laeuft auf http://localhost:2770 (Spiele: $GAMES, pgAdmin: http://localhost:2771)"
