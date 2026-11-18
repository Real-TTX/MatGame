# MatGame protocol

The contract between the apps / browser parts of the games, the master (MatGame) and the app shell. JSON is camelCase, times ISO 8601 UTC.
Errors: `400 { error, field }` (a rule, field camelCase or null), `401` not signed in, `403` not allowed, `426 { error, update: true }` app too old / other protocol,
`429` too many requests. Platform protocol: **1** (`MatGameProtocol.Version`, `sdk/js/version.js PROTOCOL`).

## Addresses of a master

| Path | What |
|---|---|
| `/` | the portal (games, sign-in, guest, profile) |
| `/link?code=ABCD-EFGH` | confirm the sign-in of a device |
| `/Admin/...` | the administration (MatUi, admins only) and the plugins' admin pages (`/Admin/<Game>/...`) |
| `/<game>/` | the web client of a game (`/bunnywarfare/`, `/kingbee/`); its own paths below (`/bunnywarfare/Play/{token}`) |
| `/<game>/api/...` | the game API (the plugin's, see the game's docs/PROTOCOL.md) |
| `/<game>/hub` | the game's SignalR hub |
| `/api/matgame/...` | the host API (below) |
| `/.well-known/matgame` | discovery (anonymous) |
| `/api`, `/api/health` | the Mat platform contract (MatDesign docs/PLATFORM.md) |

## Requests of the apps

- Every request carries `X-MatGame-Protocol: 1` and `X-MatGame-Client: <game>/<version> (<platform>)` (platform `windows`, `android`, `ios`, `macos`).
  A hub connection carries them as query values `mgp` and `mgc`, and the token as `access_token` (SignalR's way for WebSockets).
- Signed in with `Authorization: Bearer <device token>`; no cookies (`credentials: 'omit'`), so no antiforgery token. CORS allows any origin for `/api/...`,
  `/<game>/api/...`, `/<game>/hub` and the discovery (policy `MatGame.Apps`, no credentials).
- The browser on the master uses the cookie `matgame.auth` and sends the antiforgery token (from `GET /api/matgame/session`) in `RequestVerificationToken` with every change.

## Discovery: `GET /.well-known/matgame`

```json
{
  "kind": "matgame", "protocol": 1, "name": "Matthix Games", "version": "0.1.7-20261118",
  "registration": true, "guests": true, "moved": null,
  "games": [
    { "id": "bunnywarfare", "name": "BunnyWarfare", "tagline": "…", "path": "/bunnywarfare", "protocol": 7, "minClient": "0.2.0",
      "accent": "#c2410c", "cover": "/bunnywarfare/img/cover.jpg", "icon": "/bunnywarfare/img/icon.png", "status": "ok" }
  ]
}
```

`status`: `ok`, `off` (switched off by an admin: apps offer only offline play). `moved`: the new address of this master (set in the admin when the domain changes);
apps replace the stored address and ask again.

## Host API (`/api/matgame`)

Account in every answer: `{ id, name, loginName, isAdmin, isGuest }`.

| Call | Body | Answer |
|---|---|---|
| `GET /session` | – | `{ signedIn, account, canRegister, guests, antiforgery, server: { name, version } }` (browser: also renews the antiforgery token; app: the account of the token) |
| `POST /session/login` | `{ loginName, password }` | session (sets the cookie) |
| `POST /session/register` | `{ displayName, loginName, password }` | session |
| `POST /session/guest` | `{ name }` | session (a guest account, cookie) |
| `POST /session/logout` | `{}` | session |
| `POST /account/upgrade` | `{ displayName, loginName, password }` | session (the guest becomes an account, same id) |
| `PUT /account` | `{ displayName }` | session |
| `POST /account/password` | `{ currentPassword, newPassword }` | `{ ok }` |
| `POST /token` | `{ loginName, password, device: { name, platform, game } }` | `{ token, account }` (app) |
| `POST /token/register` | `{ displayName, loginName, password, device }` | `{ token, account }` |
| `POST /token/guest` | `{ name, device }` | `{ token, account }` |
| `POST /token/revoke` | `{}` | `{ ok }` (removes the device of the token) |
| `POST /device/start` | `{ device }` | `{ code: "ABCD-EFGH", deviceCode, verifyUrl, interval: 3, expiresIn: 600 }` |
| `POST /device/poll` | `{ deviceCode }` | `202 { pending: true }` until confirmed, then `{ token, account }`; `410` expired or denied |
| `GET /games` | – | the games with `stats` (`playersOnline`, `runningMatches`) and the caller's `profile` per game (`level`, `detail`) |

Device tokens: 32 random bytes (base64url), stored as SHA-256, one per device, valid until revoked (unused for 180 days = expired). Guests: name of their choice
(2–24 characters, "(Gast)" is shown after it), no password, no ranking; the admin can switch guests off.

## The plugins (`MatGame.Sdk.IGameModule`)

See `src/MatGame.Sdk/GameModule.cs`. A plugin maps under `/<game>`: its web client, `/api` (with `GameApi.ForGame(Info)`: version check, antiforgery for cookie requests,
CORS) and `/hub`. It reads the account with `HttpContext.User.ToAccount()` (`GameAccount`: id, name, isAdmin, isGuest) and keeps its own data per account id in
its schema. Its admin pages are Razor pages under `Pages/Admin/<Game>/` that use the host's layout `_Layout` and the admin kit of `MatGame.Sdk`.

## Bridge (app shell ↔ browser part)

Page → shell: `{ id, target, method, args }`; shell → page: `{ id, ok: true, result }` or `{ id, ok: false, error, status, body }`; events `{ event, name, args }`.
Windows: `window.chrome.webview.postMessage(object)` / the `message` event (WebView2). Mobile (MAUI): `window.MatGameBridge.postMessage(json)` / the `matgame-bridge` event.

| target.method | args | result |
|---|---|---|
| `app.info` | – | `{ platform, game, version, defaultServer }` |
| `app.openBrowser` | url | – |
| `app.fullscreen` | on (bool) | – |
| `app.quit` | – | – |
| `storage.get` / `.set` / `.remove` | key[, json text] | json text or null |
| `secure.get` / `.set` / `.remove` | key[, value] | value or null (DPAPI on Windows) |
| `local.start` | game | – (starts the game's `ILocalGame`) |
| `local.request` | method, path, body | `{ status, body }` (the game API offline) |
| `local.invoke` | method, args[] | the result of the hub method |
| event `local` | name, args | a hub message of the offline game (`{ event: 'local', name: 'Turn', args: [ … ] }`) |
