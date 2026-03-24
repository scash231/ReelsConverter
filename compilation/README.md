# Instagram Reels -> Try Not To Laugh Compilation

Dieses Skript liest alle Instagram-Reel-Links aus reels.txt, laedt die Videos herunter und erstellt daraus automatisch ein grosses Compilation-Video.

Das Ergebnis wird im Ordner done_compilation gespeichert.

## Voraussetzungen

- Python 3.9+
- ffmpeg und ffprobe
  - Entweder im System-PATH
  - oder als ffmpeg.exe und ffprobe.exe im Ordner ffmpeg_bin

Hinweis: yt-dlp wird beim Start automatisch installiert, falls es fehlt.

## reels.txt vorbereiten

Trage in reels.txt pro Zeile genau einen Reel-Link ein, zum Beispiel:

https://www.instagram.com/reel/ABC123/
https://www.instagram.com/reel/DEF456/
https://www.instagram.com/reel/GHI789/

Optional:
- Leere Zeilen sind erlaubt
- Zeilen mit # am Anfang werden als Kommentar ignoriert

## Start

Im Projektordner ausfuehren:

python instagram_downloader.py

## Was das Skript macht

1. Liest alle Links aus reels.txt
2. Laedt alle Reels in den Ordner downloads
3. Normalisiert jeden Clip auf einheitliches Format (1080x1920, 30 FPS, H.264/AAC)
4. Fuegt alle Clips in der Reihenfolge aus reels.txt zu einem Video zusammen
5. Speichert die fertige Datei in done_compilation unter einem Zeitstempel-Namen

Beispiel-Ausgabe:
- done_compilation/try_not_to_laugh_compilation_20260324_153012.mp4

## Projektstruktur

.
├── instagram_downloader.py
├── reels.txt
├── ffmpeg_bin/
├── downloads/
└── done_compilation/
