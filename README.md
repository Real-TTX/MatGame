<div align="center">

<img src="src/MatGame/wwwroot/img/logo-mark.svg" alt="MatGame" width="96">

# MatGame

**Der Server der Mat-Spiele: ein Portal, ein Konto, alle Spiele – im Browser und in den Apps.**

</div>

---

MatGame ist der **Master** für BunnyWarfare, King Bee, TurboToys und alle späteren Mat-Spiele: eine Docker-App, in der die Spiele als **Plugins**
stecken. Unter einer Adresse (z. B. `games.matthix.de`) gibt es:

- 🎮 **das Portal** – alle Spiele als Karten, ein Klick startet das Spiel im Browser,
- 👤 **ein Konto für alle Spiele** – oder einfach **als Gast** losspielen (abschaltbar), später ein Konto daraus machen,
- 📱 **die Geräte-Anmeldung der Apps** – die App zeigt einen Code, man bestätigt ihn im Browser (`/link`),
- 🌐 **die Online-Partien jedes Spiels** – Lobbys, Partien, Ranglisten des Plugins unter `/<spiel>/`,
- 🛠️ **die Administration** (`/Admin`, MatUi) – Konten, Geräte, Spiele an/aus, Mindestversion der Apps, Einstellungen, und je Spiel seine eigene Abteilung (BunnyWarfare: Welten, Waffen …),
- 🔎 **Discovery für die Apps** – `/.well-known/matgame` sagt, welche Spiele es gibt, welche Version die App mindestens braucht und ob der Server umgezogen ist.

Die Apps spielen **offline** immer als Gast gegen Bots und **online** gegen einen Master: ab Werk der offizielle, im Spiel kann man jede andere
MatGame-Instanz eintragen. Das Konzept steht in [docs/KONZEPT.md](docs/KONZEPT.md), das Protokoll in [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Schnellstart

```bash
docker compose up -d          # Release (:latest)  → http://localhost:2770
```

Das **erste Konto**, das sich registriert, ist der Administrator. Die Datenbank (PostgreSQL 17) läuft im selben Stack.

| Datei | Kanal | Image |
|---|---|---|
| `docker-compose.yml` | Release (Branch `main`) | `ghcr.io/real-ttx/matgame:latest` |
| `docker-compose.dev.yml` | Nightly (Branch `dev`) | `ghcr.io/real-ttx/matgame:nightly` |
| `docker-compose.local.yml` | lokal gebaut (mit pgAdmin) | `matgame:local` |

## Einstellungen

Keine Variable ist nötig. Optional (Umgebung des Dienstes `web`):

| Variable | Bedeutung |
|---|---|
| `MATGAME_GAMES` | nur diese Spiele laden, z. B. `bunnywarfare,kingbee` (Standard: alle eingebauten) |
| `MATGAME_PORT` | ein anderer Port des Hosts (Standard 2770; das eigene Image von TurboToys setzt 2773) |
| `MATGAME_DB` | eine andere PostgreSQL-Verbindung (Standard: der `db`-Dienst des Stacks) |
| `MAT_PUBLIC_URL` | die öffentliche Adresse (für die Links der Geräte-Anmeldung); ohne: die Adresse des Aufrufs |
| `MAT_API_KEY` | der Schlüssel der Mat-Plattform-API (`/api/state`, `/api/configure`, `/api/actions`) für Mattiverse |
| `MATIDENTITY_AUTHORITY` | Anmeldung über MatIdentity (Single Sign-on); ohne: nur die eigene Anmeldung |
| `MATIDENTITY_CLIENT_ID` / `MATIDENTITY_CLIENT_SECRET` | der Client von MatGame in MatIdentity (Standard-Id: `matgame`) |
| `MATIDENTITY_INTERNAL_URL` | wo MatGame MatIdentity im Docker-Netz erreicht (optional) |

Name des Servers, Registrierung, Gäste und eine neue Adresse (Domainwechsel) stellt man in **Administration → Einstellungen** ein.

## Spiele

| Spiel | Repo | Adresse | Plugin |
|---|---|---|---|
| BunnyWarfare | [Real-TTX/BunnyWarfare](https://github.com/Real-TTX/BunnyWarfare) | `/bunnywarfare/` | `BunnyWarfare.Module` |
| King Bee | [Real-TTX/KingBee](https://github.com/Real-TTX/KingBee) | `/kingbee/` | `KingBee.Module` |
| TurboToys | [Real-TTX/TurboToys](https://github.com/Real-TTX/TurboToys) | `/turbotoys/` | `TurboToys.Module` |

Die Spiele werden **aus den Nachbar-Repos** eingebaut: Alle Repos liegen nebeneinander (`Development/MatGame`, `Development/BunnyWarfare`,
`Development/KingBee`, `Development/TurboToys`), lokal genauso wie in der CI und im Docker-Build.

## Entwickeln

```powershell
./scripts/redeploy.ps1                 # baut MatGame mit beiden Spielen und startet den lokalen Stack
./scripts/redeploy.ps1 -Games none     # ohne Spiele (nur der Host)
./scripts/redeploy.ps1 -Clean          # mit leerer Datenbank
```

- App http://localhost:2770 · pgAdmin http://localhost:2771 · Postgres `localhost:2775` (Benutzer/Passwort `matgame`)
- `dotnet build MatGame.slnx`, `dotnet test MatGame.slnx` (die Tests des ganzen Hosts laufen mit `MATGAME_TEST_DB`, siehe [CLAUDE.md](CLAUDE.md))
- `-p:MatGameGames=none` baut den Host ohne Spiele.

## Apps

`src/MatGame.Shell` ist die App-Hülle für Windows: ein Fenster mit WebView2, in dem der Browser-Teil eines Spiels aus der exe selbst läuft, mit der Brücke zum
Offline-Spielkern, sicherem Speicher für die Anmeldung, Selbstinstallation (pro Benutzer, ohne Administrator) und Updates aus den GitHub-Releases des Spiels.
Jedes Spiel hat ein eigenes App-Projekt (`BunnyWarfare.App`, `KingBee.App`); dessen CI legt `<Spiel>.exe` und das Update-Manifest in die Releases
`release-channel` (main) und `nightly-channel` (dev). In der App wählt man unter **Server** einen Master (Standard: `MatGameDefaultServer` des Spiels) oder spielt offline.
iOS und Android folgen mit .NET MAUI (braucht den MAUI-Workload) über dieselbe Brücke.

## Lizenz

© Matthias Schmoldt. Alle Rechte vorbehalten.
