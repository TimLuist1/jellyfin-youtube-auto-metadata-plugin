# Jellyfin YouTube Auto Metadata Plugin

Jellyfin plugin fuer automatische YouTube-Metadaten:
- funktioniert auch ohne YouTube-ID im Dateinamen
- sucht per Titel nach passenden Videos
- uebernimmt Titel, Beschreibung, Thumbnail, Upload-Datum
- kann Episoden-Nummern aus Titeln wie `Episode 1` erkennen
- optional: KI-Bereinigung von Titel/Beschreibung (OpenAI-kompatible API)

## Beispiel

Datei:

`I'm Doing Something Crazy - Tech House Episode 1.mp4`

Das Plugin sucht den Titel auf YouTube und setzt automatisch Metadaten + Bild.

## Anforderungen

- Jellyfin `10.11.x`
- `.NET SDK 9.0` zum Bauen
- Fuer Remote-Metadaten: `yt-dlp` muss im Jellyfin-System verfuegbar sein

## Build lokal

```powershell
dotnet restore
dotnet publish .\Jellyfin.Plugin.YoutubeMetadata\Jellyfin.Plugin.YoutubeMetadata.csproj -c Release
```

## Plugin als ZIP bauen

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Version 0.1.0.0
```

Output:
- `artifacts\youtube-auto-metadata_0.1.0.0.zip`
- MD5-Checksum fuer `manifest.json`

## Repository-Installation in Jellyfin

1. `Admin Dashboard` -> `Plugins` -> `Repositories`
2. `+` klicken
3. URL auf deine `manifest.json` setzen, z. B.  
   `https://raw.githubusercontent.com/TimLuist1/jellyfin-youtube-auto-metadata-plugin/main/manifest.json`
4. `Catalog` oeffnen
5. Unter `Metadata` -> `YouTube Auto Metadata` installieren
6. Jellyfin neu starten

## Wichtige Veroeffentlichungsschritte

1. ZIP-Release in GitHub veroeffentlichen (`v0.1.0.0`)
2. In `manifest.json`:
   - `sourceUrl` auf die echte Release-ZIP setzen
   - `checksum` mit echter MD5 ersetzen
3. Aenderungen pushen

## Konfiguration (Jellyfin Plugin-Seite)

- `Automatisch per Titel suchen (ohne YouTube-ID)`
- `Max Suchtreffer`
- `Automatische Episoden-Nummern aus Titel`
- `Serie nach YouTube-Kanal gruppieren`
- `KI-Metadatenbereinigung` + API-Key/Base-URL/Modell
