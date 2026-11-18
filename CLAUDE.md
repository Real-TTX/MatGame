# CLAUDE.md

Guidance for working in this repository. Keep it in sync with the code.

## What MatGame is

The **master server of the Mat games**: one Docker app with the games as **plugins** (the MatCloud module pattern). It has the portal (`/`, player-facing,
cartoon look), one account for all games (or guests), the device sign-in of the apps (`/link`), the discovery for the apps (`/.well-known/matgame`), the
administration (`/Admin`, MatUi) with one group per game, and the Mat platform contract (`/api`, MatPlatform 2.4, optional MatIdentity). Each game is a
plugin (`MatGame.Sdk.IGameModule`) in its own repository: BunnyWarfare (`../BunnyWarfare/src/BunnyWarfare.Module`), KingBee (`../KingBee/server/KingBee.Module`), TurboToys (`../TurboToys/src/TurboToys.Module`; it also builds an own image with only itself on port 2773 through `MATGAME_PORT`/`MATGAME_GAMES`).
The concept is docs/KONZEPT.md (German), the protocol docs/PROTOCOL.md (update it with every change of the host API, discovery, SDK or bridge).
Stack: ASP.NET Core **.NET 10**, **Razor Pages**, EF Core + **PostgreSQL 17** (schema `matgame`; every game its own schema), MatUi (copied, never edited),
MatPlatform (copied by `node ../MatDesign/tools/matplatform-copy.js src/MatGame`, never edited here), Docker. Port **2770** (pgAdmin 2771, local Postgres 2775).
Accent violet `#6d28d9` (not yet in MatDesign's `AppRegistry.cs`: add it there).

## Build / run / test

- **All repositories side by side:** `Development/MatGame`, `Development/BunnyWarfare`, `Development/KingBee`, `Development/TurboToys`. MatGame.csproj references the plugins from
  there (ProjectReference), the plugins reference `../MatGame/src/MatGame.Sdk`. The same layout in the CI (three checkouts) and in the Docker build
  (named build contexts `bunnywarfare`, `kingbee`, `turbotoys`).
- `dotnet build MatGame.slnx` (0 warnings expected), `dotnet test MatGame.slnx`. `-p:MatGameGames=none` (or the environment variable `MatGameGames=none`)
  builds the host without the games; a list `bunnywarfare;kingbee;turbotoys` chooses some. `MATGAME_PORT` changes the port (default 2770; the health check follows it).
- Tests: `RulesTests` always; `HostTests` (the whole host in memory with the sample plugin `tests/MatGame.Tests.SampleGame`: discovery, sign-in, guests,
  tokens, device link, 426, switched-off game, plugin admin page inside the host layout) only with
  `$env:MATGAME_TEST_DB = "Host=localhost;Port=2775;Database=postgres;Username=matgame;Password=matgame"` (each test makes and drops its own database).
- `./scripts/redeploy.ps1 [-Games none] [-Clean]` rebuilds and starts the local stack (needs `-ExecutionPolicy Bypass` when scripts are blocked). This is how every
  change of the host **and of the games** is tested. King Bee's browser part is built by Vite inside the Docker build (`base: '/kingbee/'`, output
  `server/KingBee.Module/wwwroot`); for a `dotnet build` outside Docker run `npm ci && npm run build` in ../KingBee first.
- Migrations of the host: `dotnet tool restore`, then `MatGameGames=none dotnet ef migrations add <Name> -p src/MatGame -o Data/Migrations`. The host migrates at
  start, then starts every game (each migrates its own schema in `StartAsync`).
- JS: `node --check --input-type=module < file.js` for `sdk/js/*.js` and `src/MatGame/wwwroot/js/*.js`.
- Browser checks: Playwright with the installed Edge (headless), screenshots at 1366x768 and 390px – look at them.
- The app shell (`src/MatGame.Shell`, Windows): test it in a real game app (BunnyWarfare.App / KingBee.App): start the exe with
  `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` and `MATGAME_NO_UPDATES=1`, then drive the page with Playwright's `chromium.connectOverCDP`.
  Local builds (`local-…`) never offer to install or update; `MATGAME_WEBROOT=<folder containing <game>/>` serves the web files from disk, `MATGAME_DEVTOOLS=1` the dev tools,
  `MATGAME_PORTABLE=1` skips the install question. MAUI (iOS/Android, later) needs the MAUI workload (administrator rights): it will speak the same bridge.

## Rules (from the owner's guidelines — keep them)

- **Git:** branches `main` (release) and `dev` (nightly). Commits only under the owner's name; **no Co-Authored-By or other contributor lines**, no generated-by lines in PRs.
- **Versions:** release `<major>.<minor>.<build>-<date>`, nightly `nightly-<build>-<date>`, local `local-<date>` (`Directory.Build.props`, CI sets Channel/BuildNumber/BuildDate).
  The platform protocol (`MatGameProtocol.Version`, `sdk/js/version.js PROTOCOL`) rises only with a change old apps cannot follow; each game has its own protocol number.
- **Docker:** one Dockerfile, compose files for release/dev/local, **no `${VARIABLES}` and no `.env`**, always a stack (`name:`); volumes `web-data`, `db-data`.
- **C#:** Microsoft conventions, readable names, flat code. **Business layer = static classes** in `Business/`, each method takes an `Actor` and **checks the permission
  itself** (`Guard`). **No database queries in pages or models.**
- **Database:** PascalCase singular tables, `Id bigint`, audit columns (`AppDbContext.SaveChangesAsync`), soft delete with `UpdateState` for accounts. Schema `matgame`;
  the migrations history lives in the schema too. A game never touches the host's schema.
- **UI:** the administration is MatUi (rules of the MatDesign rulebook: toolbar/table/pager lists, edit pages with md-buttons, tabs, show-if). The admin kit
  (tag helpers, partials `_Toolbar`, `_Pager`, `_FormSummary`, `_Empty`, `ToolbarModel`, `PagerModel`, `Ui`) lives in **MatGame.Sdk** so plugins use the same.
  The portal (`Pages/Index`, `Link`, `Account/*`, `About`, `Error`: layout `_PortalLayout`, `css/portal.css`, `js/portal.js`) is the games' cartoon look, not MatUi.

## Map

```
src/MatGame.Sdk         the contract of the plugins: IGameModule, GameInfo, GameEnvironment, AdminLink, GameAccount(s) (claims, schemes, IGameAccounts),
                        MatGameProtocol + GameVersion, GameApi.ForGame (version check, antiforgery for cookie requests, CORS), the admin kit (Controls/, Web/, Pages/Shared)
src/MatGame.Local       the offline contract of the app shell: ILocalGame, ILocalHost, LocalResponse, LocalJson (no ASP.NET)
src/MatGame.Shell       the Windows app shell (WinForms + WebView2): ShellApp.Run (setup args, one instance, update offer), GameWindow (serves the embedded web files at
                        https://<game>.matgame.invalid/<game>/, SPA fallback, external links → browser, F11), Bridge (app/storage/secure/local, docs/PROTOCOL.md "Bridge"),
                        ShellStorage (storage.json + DPAPI secure.json in %APPDATA%MatGame<game>), Installer (per user, Start menu, uninstall entry, --install/--update/--uninstall/--quiet),
                        Updater (<game>-update.json in the release-channel / nightly-channel release, SHA-256), MatGame.Shell.targets (import it in every app project)
sdk/js                  the JS SDK for the browser part of a game (host/bridge, servers, session, transport, version, status); copied into the games with tools/sdk-copy.js
src/MatGame             the host
  Program.cs            database, data protection, MatPlatform, auth (cookie + device token + MatIdentity), CORS, rate limits, games, pipeline
  Games.cs              the compiled-in games (#if GAME_<ID> from MatGame.csproj / MatGameGames) + Games.Extra for tests
  Data/                 Entities (Account, Device, DeviceLink, Setting, GameSetting), AppDbContext (schema matgame), Migrations
  Business/             Common (Actor, Guard, ListRequest), AccountLogic (register, sign-in, guests, upgrade, admin, import, MatIdentity), DeviceLogic (tokens, device link),
                        SettingLogic (name, registration, guests, moved), GameLogic (on/off, min client), AdminLogic
  Web/                  Identity (schemes, DeviceTokenHandler), GameHost (modules, environments, states cache, GameGate middleware: off/426), HostApi (/api/matgame),
                        Discovery, Services (AppInfo, HealthCheck, Assets sprite incl. <game>/img/admin-icons.svg, HostGameAccounts, Housekeeping, SecurityHeaders)
  Platform/             MatGamePlatform (definition, state, configure, actions, health), MatGameIdentityUsers
  Pages/                portal (Index, Link, Account/Login|Logout|Denied, About, Error, Shared/_PortalLayout, _AuthPanel), Admin (Index, Accounts, Devices, Games, Settings),
                        Shared/_Layout (the admin shell; plugins' admin pages use it too)
tests/MatGame.Tests     RulesTests, HostTests;  tests/MatGame.Tests.SampleGame: the sample plugin (the template of a new game)
```

## Plugins (how a game plugs in)

- A Razor class library with `StaticWebAssetBasePath` = its id (its web client is served at `/<id>/...`), referencing `../MatGame/src/MatGame.Sdk`.
- `IGameModule`: `Info` (id, name, tagline, version, protocol, min client, accent, cover, icon), `AddServices` (its DbContext on `GameEnvironment.ConnectionString`
  with its own schema and migrations history in it), `MapGame` (on the group `/<id>`: web client, `/api` with `GameApi.ForGame(Info)`, `/hub`), `Admin` (the entries of the game's own menu in `/Admin`:
  the switcher under the logo (MatUi SHELL-15) shows MatGame's menu or one game's, the current page decides), `StartAsync` (migrate, seed, close stale), `StatsAsync`, `ProfileAsync`, `AccountDeletedAsync`.
- Accounts are the host's: read `HttpContext.User.ToAccount()`; keep the game's own row per account id. Hubs and APIs mark `[Authorize]` / `.RequireAuthorization()`
  themselves (the default scheme accepts the cookie and device tokens; hubs get the token as `access_token`). The apps call from another origin: the hub
  needs `.RequireCors(MatGameProtocol.AppCorsPolicy)` too (`ForGame` does it for the API group).
- Admin pages: `Pages/Admin/<Game>/...` with their own `_ViewImports.cshtml` (`@addTagHelper *, MatGame.Sdk`); the host's `/Admin` folder rule makes them admin-only.
- A new game: plugin project, ProjectReference + switch in `MatGame.csproj`, a line in `Games.cs`, a build context in the Dockerfile/compose, a checkout in the CI.
- Running matches live in memory; a restart aborts them (each game closes them in `StartAsync`).
