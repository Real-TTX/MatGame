# MatGame – Konzept

> Stand: 18.11.2026 · umgesetzt: Host, SDK, Plugins (Abweichungen vom ersten Entwurf sind markiert: **Umsetzung**) · gilt für BunnyWarfare, KingBee und jedes spätere Spiel

## 1. Worum es geht

MatGame ist der **Master der Mat-Spiele**: eine Docker-App wie MatCloud, in der die Spiele als **Plugins** stecken. Man ruft eine Adresse auf, zum Beispiel `games.matthix.de` und später vielleicht `matgame.de`, und findet dort:

- **das Portal:** alle Spiele mit Bild, ein Klick startet den Web-Client im Browser,
- **ein Konto für alle Spiele**, wahlweise als Gast spielen,
- **den Online-Teil jedes Spiels:** Lobbys, Partien, Ranglisten,
- **`/Admin`:** Plattform-Verwaltung plus eine Abteilung pro Spiel (BunnyWarfare: Welten, Waffen …).

Dazu kommen **Apps**: Windows jetzt, iOS und Android später. Sie spielen **offline** als Gast gegen Bots oder zu mehreren an einem Gerät, oder **online** gegen einen Master. Der offizielle Master ist voreingestellt, jede andere MatGame-Instanz kann man im Spiel eintragen.

| Teil | Was es ist | Wo es lebt |
|---|---|---|
| **MatGame** | der Master: Host, Konten, Portal, Admin, Plugin-System, Discovery | Repo `MatGame`, Docker-Image `matgame` |
| **MatGame SDK** | gemeinsamer Code: Plugin-Schnittstelle (C#), Client-Bibliothek (JS), App-Hülle (MAUI) | Repo `MatGame`: die Spiele binden die C#-Projekte aus dem Nachbar-Repo ein, die JS-Bibliothek wird kopiert |
| **Spiele** | Spielregeln, Plugin (Server-Teil + Web-Client + Admin-Seiten), App | eigene Repos: `BunnyWarfare`, `KingBee`, … |

## 2. Grundsätze

1. **Offline zuerst:**
   - Jede App ist ohne Netz voll spielbar, als Gast gegen Bots und mit mehreren Spielern abwechselnd an einem Gerät.
   - Offline gibt es eine eigene Level-Kurve, die nur lokal gilt.
2. **Ein Master, viele Spiele:**
   - ein Konto, eine Adresse, ein Admin, eine Installation.
   - Neue Spiele kommen als Plugin dazu, ohne Änderung am Master.
3. **Selbst hostbar:** MatGame ist ein normales Docker-Image und steht im Mattiverse-Katalog. Wer will, betreibt einen eigenen Master und trägt ihn in der App ein.
4. **Ein Spielcode, viele Hüllen:** Derselbe 3D-Client läuft im Browser vom Master und in der App aus lokalen Dateien. Es gibt keinen Fork pro Plattform.
5. **Nur der Server zählt:**
   - Online vergibt nur der Master Level, Ranglisten und Freischaltungen.
   - Offline-Fortschritt wird nie hochgeladen.
6. **Wie die Mat-Familie:**
   - Plattform-Vertrag 2 mit MatPlatform,
   - MatIdentity optional,
   - MatUi im Admin,
   - Microsoft-Konventionen,
   - Business-Layer als statische Klassen mit `Guard`,
   - gemeinsamer Code wird kopiert und mit Version und SHA-256 festgehalten.

## 3. Warum ein eigener MatGame und keine Plugins in MatCloud

Das Plugin-Muster kommt aus MatCloud: Dort steckt jede App als `ICloudModule` mit eigenen Tabellen, eigener API und eigenen Seiten im Host. MatGame übernimmt das. Trotzdem wird es eine **eigene** App:

| | MatCloud | MatGame |
|---|---|---|
| Publikum | die eigenen Geräte eines Kontos (Thix-Apps) | öffentlich: Registrierung, Gäste, Fremde spielen gegeneinander |
| Last | Sync, kurze Anfragen | Echtzeit über SignalR, Simulation auf dem Server, Bots, Partien im Speicher |
| Neustart | harmlos | bricht laufende Partien ab, also seltener und gezielter |
| Domain, Look | Werkzeug (MatUi) | Spiel: Cartoon-Portal und eigene Marke |

Beide teilen sich Muster und Bausteine: Geräte-Anmeldung, kopierte Verträge, MatPlatform und MatIdentity.

## 4. Aufbau des Masters

```
https://games.matthix.de
├── /                         Portal: Spiele-Karten mit Screenshot, Anmelden / Gast, Profil
├── /bunnywarfare/            Web-Client des Spiels (vom Plugin), /bunnywarfare/Play/{token}
├── /bunnywarfare/api/...     Spiel-API des Plugins
├── /bunnywarfare/hub         SignalR des Plugins
├── /kingbee/ …               dasselbe für KingBee
├── /link                     Geräte-Anmeldung (Code aus der App bestätigen)
├── /Admin                    MatUi: Spieler, Geräte, Spiele, Einstellungen + je Spiel eine Abteilung
├── /.well-known/matgame      Discovery für die Apps
└── /api, /api/health …       Plattform-Vertrag der Mat-Familie
```

Weil Portal und Spiele **unter einer Adresse** liegen, gilt eine Anmeldung überall. Aus dem Portal startet man den Web-Client schon angemeldet.

### 4.1 Das Plugin-Modell

Jedes Spiel liefert **ein Plugin**: eine Razor Class Library mit Server-Code, Web-Client (`wwwroot`) und Admin-Seiten.

```csharp
public interface IGameModule
{
    GameInfo Info { get; }                                   // Id "bunnywarfare", Name, Protokoll, Mindestversion der App, Logo, Screenshot
    void AddServices(IServiceCollection services, GameContext context);
    void MapGame(IEndpointRouteBuilder game);                // unter /{id}: API, Hub, Web-Client
    IReadOnlyList<AdminSection> Admin { get; }               // Menüpunkte im /Admin (Seiten liegen im Plugin)
    Type? Database { get; }                                  // eigener DbContext, eigenes Schema, eigene Migrationen
    Task StartAsync(GameContext context, CancellationToken ct); // Seed, alte Partien abbrechen, Ticker starten
}
```

- **Datenbank:** ein Postgres mit **einem Schema pro Spiel** (`bunnywarfare`, `kingbee`) und eigener Migrations-Historie. Die Plattform-Tabellen liegen im Schema `matgame`. Die Regeln der Familie gelten weiter: PascalCase, `Id bigint`, Audit-Spalten, `UpdateState`.
- **Konten:**
  - Das Konto gehört der Plattform: Name, Avatar, Passwort, MatIdentity-Verknüpfung.
  - Spielbezogenes bleibt im Plugin, verknüpft über die Konto-Id: bei BunnyWarfare XP, Look und Statistik.
  - Das Plugin bekommt den angemeldeten `Actor` vom Host, wie heute von `Identity`.
- **Wie ein Plugin in den Master kommt:**
  - **Umsetzung:** keine NuGet-Pakete. Alle Repos liegen nebeneinander (`Development/MatGame`, `Development/BunnyWarfare`, `Development/KingBee`) – lokal, in der CI und im Docker-Build. `MatGame.csproj` bindet die Plugins als **ProjectReference aus dem Nachbar-Repo** ein, die Plugins `MatGame.Sdk` genauso. Das spart Paket-Feeds und Tokens; die Repos sind öffentlich.
  - Welche Spiele hineinkommen, sagt die MSBuild-Eigenschaft `MatGameGames` (`all`, `none` oder `bunnywarfare;kingbee`), im Docker-Build das Build-Argument `GAMES`.
  - MatGame baut **ein** Image mit allen Spielen. Der Docker-Build bekommt die Spiele als benannte Build-Kontexte (`--build-context bunnywarfare=../BunnyWarfare`).
  - Ein Push im Spiel-Repo stößt den Build von MatGame an (`repository_dispatch`, Typ `game-updated`, `{ branch }`): `dev` erzeugt das Nightly-Image, `main` das Release-Image. Zusätzlich baut MatGame jede Nacht `:nightly` mit dem neuesten `dev` aller Spiele.
  - Plugins zur Laufzeit als DLL in einen Ordner zu legen, ginge auch. Das ist aber wegen Versionen und Migrationen fehleranfällig, deshalb **nicht**.
- **Entwickeln im Spiel-Repo:** **Umsetzung:** der lokale Stack von MatGame (`../MatGame/scripts/redeploy.ps1`, Port 2770) baut den Host mit den Spielen aus den Nachbar-Repos. Man arbeitet also immer in der echten Umgebung.
- **Wenn es später eng wird:** Dasselbe Image kann mit `MATGAME_GAMES=bunnywarfare` nur einen Teil der Spiele laden. So läuft ein Spiel in einem eigenen Container unter derselben Domain, ohne Umbau.

## 5. Was der Master macht

- **Konten und Gäste:**
  - Registrierung, Anmeldung, Passwort-Mail über MatSMTP (optional).
  - **Gäste** spielen online ohne Konto: Ein Gast-Token gilt pro Gerät, der Name ist frei wählbar, es gibt keine Rangliste. Ein Gast kann später ein Konto daraus machen und behält dabei seinen Fortschritt.
  - Optional Anmeldung über **MatIdentity**.
- **Geräte-Anmeldung** wie bei ThixClip und MatCloud:
  1. Die App zeigt einen Code und öffnet `/link`.
  2. Man meldet sich dort an und bestätigt.
  3. Die App bekommt ein Token pro Gerät.
  - Passwort in der App gibt es als zweiten Weg.
  - Geräte kann man im Profil und im Admin entfernen.
- **Portal:**
  - Spiele-Karten mit Screenshot, Spieler online, laufende Partien,
  - ein gemeinsames Profil mit dem Level pro Spiel,
  - später Freunde und Einladungen.
- **Admin:**
  - Spieler, Geräte,
  - Spiele: an/aus, Mindestversion der App, Zustand,
  - Einstellungen: Name des Servers, Registrierung offen, Gäste erlaubt,
  - je Spiel die Abteilung des Plugins.
- **Plattform-Vertrag:**
  - Launcher „MatGame“,
  - Widgets „Spieler online“ und „Laufende Partien“,
  - `consumes: identity.oidc, mail.smtp`,
  - ein Eintrag im Mattiverse-Katalog.

### 5.1 Discovery für die Apps

`GET /.well-known/matgame` antwortet ohne Anmeldung, ohne Geheimnisse und mit begrenzter Rate.

```json
{
  "kind": "matgame",
  "protocol": 1,
  "name": "Matthix Games",
  "version": "1.0.0-20261118",
  "registration": true,
  "guests": true,
  "moved": null,
  "games": [
    { "id": "bunnywarfare", "name": "BunnyWarfare", "path": "/bunnywarfare", "protocol": 7, "minClient": "1.4.0", "status": "ok" },
    { "id": "kingbee", "name": "King Bee", "path": "/kingbee", "protocol": 3, "minClient": "0.6.0", "status": "ok" }
  ]
}
```

- **Jeder Aufruf der App** trägt `X-MatGame-Protocol` und `X-MatGame-Client: bunnywarfare/1.4.0 (windows)`. Ist die App zu alt, antwortet der Server mit `426` und einer Meldung. Der Hub prüft dasselbe beim Verbinden.
- Fehlt das Spiel auf dem Master oder ist es ausgeschaltet, bietet die App nur „Offline spielen“ an.

### 5.2 Domain und Standard-Adresse

- Die Standard-Adresse wird **beim Bauen** festgelegt: `MatGameDefaultServer` in `Directory.Build.props`, vorerst `https://games.matthix.de`. In der App kann man sie ändern und weitere Server eintragen.
- **Domainwechsel**, etwa zu `matgame.de`: Der alte Master meldet in der Discovery `"moved": "https://matgame.de"`. Die Apps übernehmen die neue Adresse dann selbst, Konten und Token bleiben gültig, weil es derselbe Server ist. Die alte Domain leitet weiter.

## 6. Die Spiele als Plugins

| Was | BunnyWarfare | KingBee |
|---|---|---|
| Server-Teil | `BunnyWarfare.Module`: Api, GameHub, RoomRegistry, GameTicker, Business, Daten im Schema `bunnywarfare` | `KingBee.Module`: Rooms, Relay-Hub, FrameLoop (klein, heute ohne Datenbank) |
| Spielregeln | `BunnyWarfare.Core`: Terrain, Simulation, Bot, Crates, MatchRoom, Dtos (ohne ASP.NET und Datenbank, auch in der App) | `sim.js` im Client (läuft in der App ohnehin) |
| Web-Client | heute `wwwroot/js/game`: kennt danach seinen Basis-Pfad `/bunnywarfare/` | Vite-Build mit `base: '/kingbee/'` |
| Admin | Welten, Waffen, Partien als Plugin-Seiten im `/Admin` (MatUi) | Partien, Räume |
| Konten | eigene `Player`-Konten werden Plattform-Konten, XP/Look/Statistik bleiben im Plugin | neu: Gast oder Konto |
| Offline in der App | derselbe `MatchRoom` lokal, Takt durch `LocalTicker` | schon heute |
| Online | der Server entscheidet (wie heute) | Gleichschritt über den Server (wie heute), Prüfsummen-Test Edge ↔ Safari im CI |

**Bestehende BunnyWarfare-Installationen:** Ein einmaliger Import übernimmt die alte Datenbank in MatGame:

- Spieler werden Konten, Passwort-Hashes bleiben erhalten,
- Welten, Waffen und Fortschritt kommen ins Schema `bunnywarfare`.

Danach gibt es BunnyWarfare nur noch als Plugin. Das eigene Image entfällt, der Dev-Host bleibt für die Entwicklung.

## 7. Der Client: MatGame SDK (JS)

Eine ES-Bibliothek ohne Build-Schritt, die auch in Vite funktioniert. Sie wird in jedes Spiel kopiert.

| Modul | Aufgabe |
|---|---|
| `host.js` | Wo laufe ich: `web` (vom Master), `desktop`, `mobile`; Version, Plattform, Basis-Pfad |
| `servers.js` | Standard-Adresse, eigene Server, zuletzt benutzte, „Verbindung testen“, `moved` übernehmen. Im Web fest auf den eigenen Master |
| `session.js` | Offline-Gast (lokal), Online-Gast oder Konto pro Server (Token, Erneuerung, Abmelden), Geräte-Anmeldung |
| `transport.js` | `start() · invoke(method, …args) · on(event, fn) · stop()` mit `SignalRTransport` (online) und `LocalTransport` (offline über die Hülle) |
| `version.js` | Protokollprüfung, „App aktualisieren“ |
| `status.js` | online, offline, verbinde: das Abzeichen im Menü |

Jedes Spiel zeichnet die Oberfläche in seinem eigenen Stil. Pflicht im Menü: **Server**, **Offline spielen**, das Abzeichen. Bei BunnyWarfare spricht `match.js` danach nur noch mit `transport`, nie mehr direkt mit SignalR.

## 8. Die App-Hülle: .NET MAUI (Windows zuerst mit WebView2)

**Umsetzung:** Der MAUI-Workload braucht Administratorrechte zum Installieren. Die Windows-App startet deshalb mit dem vorgesehenen Ausweichweg (eine schlanke Hülle mit WebView2) und **derselben Brücke** (docs/PROTOCOL.md „Bridge“). MAUI kommt für iOS und Android dazu, sobald der Workload installiert ist (`dotnet workload install maui` als Administrator); der Spielkern (`MatGame.Local.ILocalGame`) bleibt derselbe.

| Möglichkeit | C# auf dem Gerät (BunnyWarfare offline) | Windows | iOS / Android | Bewertung |
|---|---|---|---|---|
| **.NET MAUI + HybridWebView** | **ja** | WebView2 | WKWebView / Android WebView | **gewählt:** ein Projekt für alles, der echte Spielkern läuft offline |
| WinForms/WPF + WebView2 | ja | ja | nein | gut, aber nur Windows: für Mobil müsste man noch einmal von vorn anfangen |
| Tauri / Capacitor | nein (nur JS) | ja | ja | klein, aber BunnyWarfare bräuchte offline eine zweite Simulation in JS, die mit der in C# Schritt halten muss |
| Avalonia (MatDesktop) | ja | ja | eingeschränkt | Familien-Toolkit für Werkzeug-Apps, aber kein freier eingebauter Browser für den 3D-Client |

Wie die Hülle arbeitet:

- Der Web-Build des Spiels liegt in der App und wird über einen virtuellen Host geladen.
- Die 3D-Grafik läuft im Browser des Systems: Edge, Safari oder Chrome. WebGL ist dort so schnell wie im Browser.
- Die **Brücke** zwischen JS und C#:
  - `local.invoke(...)` ruft `MatchRoom` auf, die Antworten kommen über `transport.on(...)` zurück,
  - `storage` für Offline-Profil und Einstellungen (`%APPDATA%\MatGame\<spiel>`),
  - `openBrowser` für die Geräte-Anmeldung,
  - `secureStore` für die Token: DPAPI unter Windows, Keychain unter iOS, Keystore unter Android,
  - `appInfo`.
- **Pro Spiel ein App-Projekt** mit Name, Icon und Store-Eintrag. Es setzt sich zusammen aus `MatGame.Shell`, dem Web-Build und, wenn nötig, dem Spielkern (`BunnyWarfare.Core`).
- **Windows:**
  - unverpackte exe, die sich selbst installiert, mit `--update`, `--quiet` und `--uninstall`,
  - Release-Feed von GitHub mit SHA-256 und Kanal Release/Nightly, nach dem ThixClip/ThixLauncher-Muster,
  - je Spiel ein Eintrag im ThixLauncher-Katalog,
  - ein Code-Signing-Zertifikat gegen die SmartScreen-Warnung einplanen.
  - Ausweichweg, falls MAUI unter Windows hakt: dieselbe Brücke in einer WinForms-Hülle mit WebView2.
- **Mobil:** Android über den Play Store (25 $ einmalig), iOS über den App Store (99 $ im Jahr), gebaut auf einem Mac oder einem macOS-Runner. Vorher Pflicht: Touch-Steuerung und eine Grafikstufe „Niedrig“.

## 9. Profile und Fortschritt

| | Offline (Gast) | Online-Gast | Online-Konto |
|---|---|---|---|
| Wo | auf dem Gerät, pro Spiel | auf dem Master, pro Gerät | auf dem Master |
| Level, Freischaltungen | eigene lokale Kurve | ja, vom Server | ja, vom Server |
| Ranglisten | – | – | ja |
| Übertragen | nie | wird beim Registrieren zum Konto | – |

## 10. Versionen, Builds, Kanäle

- **Pro Spiel eine Version** für Plugin, Web-Client und Apps in `Directory.Build.props`, nach dem Schema der Familie: release `<major>.<minor>.<build>-<date>`, nightly `nightly-<build>-<date>`. Dazu **eine Protokollnummer**, die nur bei inkompatiblen Änderungen steigt.
- **CI im Spiel-Repo** (gleichzeitig):

  | Job | Runner | Ergebnis |
  |---|---|---|
  | module | ubuntu | Tests (mit MatGame als Nachbar-Checkout), dann `repository_dispatch` an MatGame |
  | windows | windows | Setup-exe + Feed-Manifest im GitHub Release |
  | android | ubuntu | aab (ab Phase 6) |
  | ios | macos | ipa (ab Phase 6) |

- **CI in MatGame:** Image `matgame:latest` aus `main` mit den Release-Plugins und `matgame:nightly` aus `dev` mit den Nightly-Plugins. Die JS-Bibliothek wird mit `tools/sdk-copy.js` in die Spiele kopiert (`matgame.version` mit Version und SHA-256 jeder Datei).

## 11. Sicherheit

- Online entscheidet der Server. Apps senden nur Absichten, wie der Browser.
- Gleichschritt-Spiele lassen sich nicht ganz gegen Schummeln schützen. Für Gelegenheitsspiele ist das in Ordnung: Der Server prüft Ergebnisse auf Plausibilität, und die Clients melden Prüfsummen.
- Token gelten pro Gerät und Server, lassen sich widerrufen und werden kurzlebig erneuert.
- Browser und `/Admin` nutzen Cookie und Antiforgery, Apps nur Bearer-Token. `/Admin` ist aus der App nicht erreichbar.
- Discovery, Registrierung, Gast-Token und Geräte-Code sind in der Rate begrenzt. Gäste dürfen weniger, zum Beispiel keine öffentlichen Räume erstellen, wenn der Admin das so einstellt.

## 12. Phasen und grober Aufwand

| Phase | Inhalt | Ergebnis | Aufwand |
|---|---|---|---|
| **1 MatGame-Grundgerüst** | Repo, Host, `IGameModule`, Konten + Gäste, Geräte-Anmeldung, Discovery, Portal, Admin-Gerüst, JS-SDK, Pakete, CI | Master läuft (noch ohne Spiele) | ~2 Wochen |
| **2 BunnyWarfare als Plugin** | `BunnyWarfare.Core` + `.Module`, Basis-Pfad, `transport`, Admin-Seiten, Schema, Dev-Host, Import der alten Datenbank | BunnyWarfare auf `games.matthix.de/bunnywarfare` | 1,5–2 Wochen |
| **3 KingBee als Plugin** | `KingBee.Module`, Basis-Pfad, Gast/Konto, Prüfsummen-Test | beide Spiele im Portal | 3–5 Tage |
| **4 Windows-Apps** | MatGame.Shell (MAUI), Brücke, `LocalTransport` + `LocalTicker`, Offline-Profil, Setup/Update, CI, ThixLauncher | beide Spiele als Windows-App, offline + online | 1,5–2 Wochen |
| **5 Familie** | MatPlatform-Vertrag, MatIdentity, MatSMTP, Mattiverse-Katalog | MatGame per Klick in Mattiverse | ~1 Woche |
| **6 Mobil** | Android, iOS, Touch, Grafikstufen, Stores | Apps in den Stores | 2–4 Wochen + Prüfung |
| **7 Extras** | Freunde, Einladungen, Präsenz, macOS | – | nach Bedarf |

## 13. Entscheidungen

**Getroffen:**

- Ein eigener Master **MatGame** mit Spielen als **Plugins** (Muster MatCloud), Portal mit Web-Client.
- Domain vorerst `games.matthix.de`, später vielleicht `matgame.de` (Wechsel über `moved`).
- Apps mit **.NET MAUI**.
- Offline mit eigener lokaler Level-Kurve.
- Gäste immer offline und auf Wunsch des Admins auch online.

**Noch offen:**

1. Name des offiziellen Masters im Portal, etwa „Matthix Games“, und ein Logo für MatGame.
2. ~~Port und Akzentfarbe~~ **Umsetzung:** Port 2770 (pgAdmin 2771, lokales Postgres 2775), Akzent Violett `#6d28d9` (noch in MatDesigns `AppRegistry` eintragen).
3. Code-Signing-Zertifikat für Windows sowie Apple- und Google-Konten: wann besorgen.
