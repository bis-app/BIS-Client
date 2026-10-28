# BIS Client Setup

Inno-Setup-Skript für die Firmeninstallation des **BIS Client**.

Das Setup ist eine **Per-User-Installation** und benötigt **keine Administratorrechte** (kein UAC). Installiert wird nach `%LOCALAPPDATA%\Programs\BIS Client` (typisch `C:\Users\<Benutzer>\AppData\Local\Programs\BIS Client`). Startmenü- und Desktopverknüpfung gelten nur für den angemeldeten Benutzer. WebView2 wird bei Bedarf ebenfalls ohne Elevation (per-user) nachinstalliert.

Falls zuvor eine Maschineninstallation nach `Program Files` existiert, diese zuerst deinstallieren, sonst liegen zwei Kopien parallel.

## Voraussetzungen

- [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`ISCC.exe`)
- .NET 10 SDK (zum Veröffentlichen)
- Internetzugang am Ziel-PC **oder** die WebView2-Runtime bereits vorhanden (Windows 11 meist vorinstalliert)

## Veröffentlichung

Im Repository-Root:

```bat
dotnet publish src\BISClient\BISClient.csproj -c Release -p:PublishProfile=Win64
```

Ergebnis: `publish\win-x64\` (self-contained, win-x64).

## Setup kompilieren

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\BISClient.iss
```

Ausgabe: `installer\output\BISClientSetup.exe`

## WebView2 Runtime

Das Setup prüft die Registry auf die Evergreen-Runtime
`{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`.

- Ist sie vorhanden, wird nichts nachinstalliert.
- Fehlt sie und `installer\redist\MicrosoftEdgeWebView2Setup.exe` liegt bereit, wird diese Datei still installiert.
- Andernfalls lädt das Setup den [Evergreen-Bootstrapper](https://go.microsoft.com/fwlink/p/?LinkId=2124703) herunter und installiert ihn.

Offline-Paket ablegen:

```
installer\redist\MicrosoftEdgeWebView2Setup.exe
```

Der BIS Client prüft die Runtime zusätzlich beim Start und zeigt sonst eine verständliche Fehlermeldung.

## Explorer-Upload („Senden an“ / „Teilen“)

Nach `dotnet publish` liegen `BIS.BISClient.msix` und `BIS.Identity.cer` im Publish-Ordner und werden mitinstalliert. Das Setup legt das Zertifikat in den benutzereigenen Store `TrustedPeople`. Der Client registriert das Sparse-Paket erst, wenn ein Upload-Token gespeichert ist.

Beim Deinstallieren werden die SendTo-Verknüpfung `BIS Import-Ordner.lnk` und das Appx-Paket `BIS.BISClient` entfernt.

## Konfiguration nach der Installation

- `appsettings.json` im Installationsordner wird bei einem Upgrade **nicht** überschrieben, wenn sie bereits existiert.
- Maschinen- oder standortspezifische Werte gehören nach `appsettings.Local.json` (wird vom Setup nicht mitgeliefert).

Vorlage: `src\BISClient\appsettings.Local.json.example`

## Client-Update veröffentlichen

Neue Builds holen das Manifest von

`https://raw.githubusercontent.com/bis-app/BIS-Client/main/client-feed/version.json`

und laden `BISClientSetup.exe` aus demselben Ordner. `downloadUrl` im Manifest ist relativ (`BISClientSetup.exe`), ohne führenden Schrägstrich. Ist `UpdateManifestUrl` gesetzt, gilt diese absolute Adresse statt des GitHub-Feeds.

1. Dieselbe Version in `src\BISClient\BISClient.csproj` (`<Version>`) und `installer\BISClient.iss` (`#define MyAppVersion`) eintragen. Ohne Angabe übernimmt `@c-u` die Version aus dem Aufruf.
2. Im Chat `@c-u` aufrufen. Der Skill prüft die Versionen, baut Publish und Setup, schreibt SHA-256 und `client-feed/version.json`, kopiert `BISClientSetup.exe` nach `client-feed/` und committet nur diesen Feed.
3. Den Commit selbst nach `main` pushen. Erst danach sehen die Clients das Update. Raw-Inhalte von GitHub können einige Minuten verzögert ankommen.

Das Setup muss unter 100 MB bleiben, sonst lehnt GitHub den Push ab. Ab 50 MB weist das Skript darauf hin.

Bereits installierte Clients der Version 1.1.6 prüfen noch `https://<BIS>/client/version.json`. Die erste Version mit dem GitHub-Fallback muss einmal über diesen Server oder eine lokale Installation ankommen. Danach reicht der Push auf `main`.
