# 🎬 ReelsConverter v2.0

**Instagram · TikTok · YouTube → Shorts Converter**

C# WPF Frontend + Python FastAPI Backend — Dark Theme, Two-Column Layout, Fingerprint-Bypass.

---

## Architektur

```
reelsconverter/
├── backend/                        ← Python FastAPI Backend
│   ├── server.py                   ← REST API (Port 8765)
│   ├── downloader.py               ← yt-dlp Download-Logik
│   ├── transformer.py              ← FFmpeg Fingerprint-Bypass (4 Methoden)
│   ├── uploader.py                 ← YouTube Data API v3 Upload
│   ├── ffmpeg_manager.py           ← Auto-FFmpeg-Installer
│   └── requirements.txt
└── ReelsConverterUI/               ← C# WPF Frontend (.NET 8)
    ├── MainWindow.xaml             ← Zwei-Spalten-Layout (760×560)
    ├── MainWindow.xaml.cs          ← Event-Handler + Job-Orchestrierung
    ├── ProgressWindow.xaml/.cs     ← Floating Progress-Popup
    ├── DescriptionEditorWindow.xaml/.cs  ← Beschreibungs-Editor-Popup
    ├── App.xaml                    ← Dark Theme + Custom Styles
    ├── Models/Models.cs            ← Datenmodelle
    └── Services/
        ├── BackendService.cs       ← HTTP + SSE Client
        └── BackendLauncher.cs      ← Python-Prozess Manager
```

---

## Features

### 🎨 Modernes UI
- **Dark Theme** mit blauem Akzent (`#7A9EC0`)
- **Zwei-Spalten-Layout** — alle Steuerelemente immer sichtbar, kein Scrollen nötig
- **Tab-System** — Instagram / TikTok / YouTube mit animiertem Farbindikator
- **Custom Window Chrome** — randlos, runde Ecken, eigene Titelleiste
- **Floating Progress-Popup** — öffnet sich beim Start, schließt sich automatisch bei Erfolg
- **Beschreibungs-Editor** — separates Popup-Fenster für langen Text

### 📥 Multi-Plattform Download
- **Instagram Reels** — Direkt-Download in bester Qualität
- **TikTok Videos** — Ohne Wasserzeichen (yt-dlp)
- **YouTube Videos** — Shorts und reguläre Videos

### 🔄 Fingerprint-Bypass (4 Methoden)

| Methode | Beschreibung |
|---|---|
| `metadata` | Nur Metadaten neu schreiben, kein Re-Encode |
| `light` | 1.005× Zoom + Crop, schnelle Enkodierung |
| `standard` | 1.01× Zoom, Helligkeit/Sättigung, +0.3% Tempo |
| `strong` | 1.02× Zoom, Farbton-Shift, +0.8% Tempo |

### ☁️ YouTube Upload
- Direkt als Short hochladen
- Titel, Beschreibung, Tags automatisch aus Metadaten übernommen
- Sichtbarkeit wählbar (Öffentlich / Nicht gelistet / Privat)
- Tags werden automatisch bereinigt (YouTube API Limits: max. 30 Zeichen/Tag, 500 Zeichen gesamt)

### 📊 Extras
- **Echtzeit-Progress** via Server-Sent Events (SSE)
- **Metadaten-Vorschau** mit Titel, Tags und Uploader-Info
- **Zwischenablage-Erkennung** — URL einfügen per Klick

---

## Schnellstart

### 1. Python Backend vorbereiten
```powershell
cd backend
pip install -r requirements.txt
```

### 2. C# Frontend starten
```powershell
dotnet run --project ReelsConverterUI
```

Das WPF-Frontend startet den Python-Backend-Server automatisch beim Launch.

### Voraussetzungen

- **.NET 8 SDK**
- **Python 3.10+**
- **FFmpeg** — wird beim ersten Start automatisch heruntergeladen
- **Google Cloud Projekt** — nur für YouTube-Upload nötig  
  → `client_secret_*.json` in den `backend/`-Ordner legen  
  → Beim ersten Upload öffnet sich der OAuth2-Browser-Flow

---

## Technologie-Stack

| Komponente | Technologie |
|---|---|
| Frontend | C# / WPF / .NET 8 |
| Backend | Python / FastAPI / uvicorn |
| Download | yt-dlp |
| Video-Processing | FFmpeg |
| Upload | YouTube Data API v3 |
| Kommunikation | REST + SSE (Server-Sent Events) |

---

## Hinweise

- `client_secret_*.json` und `token.json` werden **nicht** ins Repository committed (`.gitignore`)
- `ffmpeg_bin/`, `downloads/` und `done_compilation/` sind ebenfalls ausgeschlossen

