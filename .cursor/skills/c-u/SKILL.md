---
name: c-u
description: >-
  Baut den BIS-Client-Installer, aktualisiert SHA-256 und client-feed/version.json
  und committet den Feed. Nutzen, wenn der Nutzer @c-u aufruft oder den
  Client-Feed veröffentlichen will. Niemals pushen.
disable-model-invocation: true
---

# Client-Update `@c-u`

Veröffentliche den Client-Feed. Push liegt beim Nutzer.

## Ablauf

1. Im Repository-Root ausführen. Version und Notes nur übergeben, wenn der Aufruf sie nennt.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File packaging/publish-client-feed.ps1 -Version 1.2.0 -Notes "Kurzbeschreibung"
```

Ohne neue Version oder ohne neue Notes die jeweiligen Parameter weglassen. Das Skript bricht ab, wenn der Branch nicht `main` ist, `BISClient.csproj` und `BISClient.iss` auseinanderlaufen oder die Version nicht neuer als `client-feed/version.json` ist. Bei Abbruch nichts committen.

2. Erfolg ist die Zeile `FEED_READY`. Danach nur diese Pfade committen, auch wenn andere Dateien geändert oder schon gestaged sind:

- `client-feed/version.json`
- `client-feed/BISClientSetup.exe`
- `client-feed/.gitattributes`
- `src/BISClient/BISClient.csproj`
- `installer/BISClient.iss`

```powershell
git commit -m "Client 1.2.0 veröffentlichen" -- client-feed/version.json client-feed/BISClientSetup.exe client-feed/.gitattributes src/BISClient/BISClient.csproj installer/BISClient.iss
```

Die Versionsnummer im Text ist die aus `FEED_READY`. Kein `git push`, kein `--amend`, keine weiteren Dateien.

3. Commit-Hash nennen und darauf hinweisen, dass der Push auf `main` noch aussteht. Raw-Inhalte von GitHub können danach einige Minuten verzögert ankommen.
