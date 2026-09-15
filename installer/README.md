# BIS Client Setup

Inno-Setup-Skript für die Firmeninstallation des **BIS Client**.

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

## Konfiguration nach der Installation

- `appsettings.json` im Installationsordner wird bei einem Upgrade **nicht** überschrieben, wenn sie bereits existiert.
- Maschinen- oder standortspezifische Werte gehören nach `appsettings.Local.json` (wird vom Setup nicht mitgeliefert).

Vorlage: `src\BISClient\appsettings.Local.json.example`
