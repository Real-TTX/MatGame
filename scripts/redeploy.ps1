#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Rebuilds MatGame with its games (../BunnyWarfare, ../KingBee) and starts the local stack (docker-compose.local.yml).
.DESCRIPTION
    The data (database, keys) stays in the volumes; -Clean removes them first (empty database: the first account becomes the administrator again).
    -Games chooses the games compiled in: all (default), none, or a list like "bunnywarfare;kingbee".
.EXAMPLE
    ./scripts/redeploy.ps1
.EXAMPLE
    ./scripts/redeploy.ps1 -Clean -Games none
#>
param([switch]$Clean, [string]$Games = 'all')

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # Windows PowerShell 5.1 turns the progress lines docker writes to stderr into errors: judge by the exit code.
    $ErrorActionPreference = 'Continue'
    $compose = @('compose', '-f', 'docker-compose.local.yml')
    if ($Clean) {
        docker @compose down -v --remove-orphans
    }

    $date = (Get-Date).ToUniversalTime().ToString('yyyyMMdd')
    docker @compose build --build-arg "GAMES=$Games" --build-arg "BuildDate=$date" web
    if ($LASTEXITCODE -ne 0) { throw 'docker compose build ist fehlgeschlagen.' }

    docker @compose up -d --remove-orphans
    if ($LASTEXITCODE -ne 0) { throw 'docker compose ist fehlgeschlagen.' }

    Write-Host ''
    Write-Host "MatGame laeuft auf http://localhost:2770 (Spiele: $Games, pgAdmin: http://localhost:2771)" -ForegroundColor Green
}
finally {
    Pop-Location
}
