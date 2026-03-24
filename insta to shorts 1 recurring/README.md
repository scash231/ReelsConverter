# Instagram Reel → YouTube Shorts Uploader

Lädt ein Instagram Reel herunter und lädt es vollautomatisch als YouTube Short hoch.

## Voraussetzungen

- **Python 3.8+**
- Ein **OpenAI API-Key** (für die Titelgenerierung)
- Ein **Google Cloud Projekt** mit aktivierter YouTube Data API v3 und einer `client_secret_*.json` Datei im Skript-Ordner

Alle Python-Bibliotheken (`yt-dlp`, `openai`, `google-api-python-client`, `google-auth-oauthlib`) sowie `ffmpeg` und `ffprobe` werden beim ersten Start **automatisch installiert**.

---

## Schnellstart

```powershell
python instagram_downloader.py
```

Das Skript fragt nur nach:
1. Der **Instagram Reel URL**
2. Dem **OpenAI API-Key** (nur beim ersten Start, sofern nicht als Umgebungsvariable gesetzt)

Alles andere läuft vollautomatisch.

---

## Ablauf im Detail

### 1. Abhängigkeiten prüfen & installieren
- `yt-dlp`, `openai`, Google-Bibliotheken werden per `pip` nachinstalliert falls nötig
- `ffmpeg.exe` und `ffprobe.exe` werden als statisches Binary nach `ffmpeg_bin\` heruntergeladen (nur einmalig, ~80 MB)

### 2. Instagram-Metadaten lesen
- Beschreibung (Caption) und Hashtags werden ohne Download vorab gelesen
- Die Hashtags werden als YouTube-Tags verwendet

### 3. Reel herunterladen
- Bestmögliche Qualität: bestes Video (MP4) + bestes Audio (M4A) werden getrennt geladen und zu einer MP4 zusammengeführt

### 4. Fingerprint-Bypass
Das Video wird so neu encodiert, dass Content-ID- und Hash-Vergleiche fehlschlagen – für Menschen ist es **vollständig identisch**:

| Maßnahme | Wert | Wahrnehmbar? |
|---|---|---|
| Pixel-Verschiebung | 1 % Zoom → Crop zurück auf Originalgröße | Nein |
| Helligkeit | +0,8 % | Nein (ab ~5 % merkbar) |
| Sättigung | +1 % | Nein (ab ~5 % merkbar) |
| Audio-Tempo | +0,3 % | Nein (ab ~2 % merkbar) |
| Neu-Encoding | H.264 CRF 17 + AAC 192 kbps | Nein |
| Metadaten | vollständig entfernt | – |

Die verarbeitete Datei wird als `*_yt_ready.mp4` im Ordner `Downloads\` gespeichert.

### 5. Titel per GPT-5-nano generieren
- Ein Frame aus der Mitte des Videos wird als JPEG extrahiert und an die OpenAI API gesendet
- Prompt: *„gebe mir hierzu einen kurzen titel für youtube shorts aber nur einen einzigen. dieser soll auf englisch sein und darauf abzielen möglichst viele views zu machen als raw nachricht"*
- Der zurückgegebene Titel wird direkt als YouTube-Titel verwendet (max. 100 Zeichen)

### 6. Upload zu YouTube Shorts
- Beim **ersten Start** öffnet sich der Browser zur einmaligen Google-Anmeldung (OAuth2)
- Das Token wird in `token.json` gespeichert – ab dem zweiten Start ist keine Anmeldung mehr nötig
- Das Video wird mit Titel, Beschreibung und Hashtag-Tags öffentlich hochgeladen
- `#Shorts` wird automatisch zur Beschreibung hinzugefügt, damit YouTube es als Short erkennt

---

## OpenAI API-Key einrichten

**Option A – Umgebungsvariable (empfohlen):**
```powershell
$env:OPENAI_API_KEY = "sk-..."
python instagram_downloader.py
```

**Option B – Interaktiv:** Das Skript fragt beim ersten Start automatisch danach.

---

## Google OAuth einrichten

1. [Google Cloud Console](https://console.cloud.google.com/) öffnen
2. Projekt auswählen
3. **APIs & Dienste → YouTube Data API v3** aktivieren
4. **APIs & Dienste → Anmeldedaten → OAuth 2.0-Client-IDs** → Desktop-App erstellen
5. JSON herunterladen und als `client_secret_*.json` in den Skript-Ordner legen
6. Unter **OAuth-Zustimmungsbildschirm → Testbenutzer** die eigene Google-Adresse eintragen

---

## Dateistruktur nach erstem Start

```
youtube shorts\
├── instagram_downloader.py     # Hauptskript
├── client_secret_*.json        # Google OAuth Credentials
├── token.json                  # Gespeichertes Google-Token (automatisch erstellt)
├── ffmpeg_bin\
│   ├── ffmpeg.exe              # Automatisch heruntergeladen
│   └── ffprobe.exe             # Automatisch heruntergeladen
└── Downloads\
    └── *_yt_ready.mp4          # Verarbeitetes Video
```

---

## Sichtbarkeit ändern

Standardmäßig wird das Video **öffentlich** (`public`) hochgeladen. Um das zu ändern, die letzte Zeile in `main()` anpassen:

```python
upload_to_youtube_shorts(final, yt_title, yt_description, yt_tags, "unlisted")  # Nicht gelistet
upload_to_youtube_shorts(final, yt_title, yt_description, yt_tags, "private")   # Privat
```
