import subprocess
import sys
import os
import shutil
import urllib.request
import zipfile
import tempfile
import json

def install_yt_dlp():
    """Installiert yt-dlp falls nicht vorhanden."""
    try:
        import yt_dlp
    except ImportError:
        print("Installiere yt-dlp...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "yt-dlp"])
        print("yt-dlp erfolgreich installiert.\n")

def install_google_libs():
    """Installiert Google API Client Libraries falls nicht vorhanden."""
    pkgs = {
        "googleapiclient": "google-api-python-client",
        "google_auth_oauthlib": "google-auth-oauthlib",
    }
    missing = []
    for mod, pkg in pkgs.items():
        try:
            __import__(mod)
        except ImportError:
            missing.append(pkg)
    if missing:
        print(f"Installiere Google-Bibliotheken: {', '.join(missing)}...")
        subprocess.check_call([sys.executable, "-m", "pip", "install"] + missing)
        print("Google-Bibliotheken erfolgreich installiert.\n")

def install_openai():
    """Installiert openai-Bibliothek falls nicht vorhanden."""
    try:
        import openai
    except ImportError:
        print("Installiere openai...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "openai"])
        print("openai erfolgreich installiert.\n")

def install_dotenv():
    """Installiert python-dotenv falls nicht vorhanden."""
    try:
        import dotenv  # noqa: F401
    except ImportError:
        print("Installiere python-dotenv...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "python-dotenv"])
        print("python-dotenv erfolgreich installiert.\n")

def ensure_ffmpeg() -> None:
    """Laedt ffmpeg + ffprobe automatisch herunter falls nicht vorhanden."""
    ffmpeg_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ffmpeg_bin")
    ffmpeg_exe = os.path.join(ffmpeg_dir, "ffmpeg.exe")
    ffprobe_exe = os.path.join(ffmpeg_dir, "ffprobe.exe")

    # Bereits lokal vorhanden?
    if os.path.isfile(ffmpeg_exe) and os.path.isfile(ffprobe_exe):
        os.environ["PATH"] = ffmpeg_dir + os.pathsep + os.environ["PATH"]
        return

    # Im System-PATH vorhanden?
    if shutil.which("ffmpeg") and shutil.which("ffprobe"):
        return

    print("ffmpeg/ffprobe nicht gefunden – lade statisches Windows-Binary herunter (~80 MB)...")
    os.makedirs(ffmpeg_dir, exist_ok=True)

    # Statisches Build von BtbN (GPL essentials, win64)
    url = (
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/"
        "ffmpeg-master-latest-win64-gpl.zip"
    )

    zip_path = os.path.join(ffmpeg_dir, "ffmpeg.zip")
    try:
        with urllib.request.urlopen(url) as resp:
            total = int(resp.headers.get("Content-Length", 0))
            downloaded = 0
            with open(zip_path, "wb") as f:
                while True:
                    chunk = resp.read(65536)
                    if not chunk:
                        break
                    f.write(chunk)
                    downloaded += len(chunk)
                    if total:
                        print(f"\r  Download: {downloaded / total * 100:.1f}%", end="", flush=True)
        print()

        # ffmpeg.exe UND ffprobe.exe aus dem Archiv extrahieren
        targets = {"ffmpeg.exe": ffmpeg_exe, "ffprobe.exe": ffprobe_exe}
        found = {}
        with zipfile.ZipFile(zip_path) as zf:
            for member in zf.namelist():
                name = member.rsplit("/", 1)[-1]
                if name in targets and name not in found:
                    with zf.open(member) as src, open(targets[name], "wb") as dst:
                        dst.write(src.read())
                    found[name] = True
                if len(found) == len(targets):
                    break

        missing = [k for k in targets if k not in found]
        if missing:
            raise RuntimeError(f"Folgende Dateien wurden im Archiv nicht gefunden: {missing}")
    finally:
        if os.path.isfile(zip_path):
            os.remove(zip_path)

    os.environ["PATH"] = ffmpeg_dir + os.pathsep + os.environ["PATH"]
    print(f"ffmpeg + ffprobe erfolgreich installiert in: {ffmpeg_dir}\n")

def fetch_instagram_metadata(url: str) -> dict:
    """Holt Titel, Beschreibung und Hashtags vom Reel ohne Download."""
    import yt_dlp
    ydl_opts = {
        "quiet": True,
        "no_warnings": True,
        "skip_download": True,
        "http_headers": {
            "User-Agent": (
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/122.0.0.0 Safari/537.36"
            )
        },
    }
    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        info = ydl.extract_info(url, download=False)

    description = (info.get("description") or "").strip()

    # Hashtags aus Beschreibung extrahieren
    import re
    tags = list(dict.fromkeys(
        tag.lstrip("#") for tag in re.findall(r"#\w+", description)
    ))

    return {"description": description, "tags": tags}


def generate_title_with_gpt(video_path: str) -> str:
    """Generiert einen View-optimierten englischen Titel via GPT-5-nano.
    Sendet einen Frame aus dem Video als Bild-Anhang mit.
    """
    import base64
    import tempfile
    from openai import OpenAI

    # OpenAI API-Key aus .env Datei
    api_key = os.environ.get("OPENAI_API_KEY", "").strip()
    if not api_key:
        raise ValueError("OPENAI_API_KEY fehlt in der .env Datei.")

    # Mittleren Frame aus dem Video extrahieren (repraesentatives Thumbnail)
    frame_path = tempfile.mktemp(suffix=".jpg")
    try:
        # Videodauer ermitteln
        probe = subprocess.run(
            ["ffprobe", "-v", "quiet", "-print_format", "json",
             "-show_format", video_path],
            capture_output=True, text=True,
        )
        duration = 0.0
        try:
            fmt = json.loads(probe.stdout).get("format", {})
            duration = float(fmt.get("duration", 0))
        except (json.JSONDecodeError, ValueError):
            pass
        seek = max(0.0, duration / 2)

        subprocess.run(
            ["ffmpeg", "-y", "-ss", str(seek), "-i", video_path,
             "-frames:v", "1", "-q:v", "2", frame_path],
            capture_output=True,
        )

        with open(frame_path, "rb") as fh:
            image_b64 = base64.b64encode(fh.read()).decode()
    finally:
        if os.path.isfile(frame_path):
            os.remove(frame_path)

    client = OpenAI(api_key=api_key)

    print("  Generiere Titel mit GPT-5-nano...")
    response = client.chat.completions.create(
        model="gpt-5.4-nano",
        messages=[
            {
                "role": "user",
                "content": [
                    {
                        "type": "image_url",
                        "image_url": {
                            "url": f"data:image/jpeg;base64,{image_b64}",
                            "detail": "low",
                        },
                    },
                    {
                        "type": "text",
                        "text": (
                            "Du siehst ein Einzelbild aus einem Video. "
                            "Erstelle dafuer genau einen kurzen englischen Titel fuer YouTube Shorts. "
                            "aber nur einen einzigen. dieser soll auf englisch sein "
                            "und darauf abzielen moeglichst viele views zu machen "
                            "als raw nachricht"
                        ),
                    },
                ],
            }
        ],
        max_completion_tokens=30,
        temperature=0.9,
    )

    title = response.choices[0].message.content.strip()
    # Anfuehrungszeichen und Zeilenumbrueche entfernen
    title = title.strip('"\' \n').splitlines()[0].strip()
    return title[:100]  # YouTube-Limit


def download_instagram_reel(url: str, output_dir: str = ".") -> str:
    """Laedt das Reel herunter und gibt den Pfad zur gespeicherten Datei zurueck."""
    import yt_dlp

    os.makedirs(output_dir, exist_ok=True)

    # Dateien VOR dem Download merken, um neue Datei zu erkennen
    before = set(f for f in os.listdir(output_dir) if f.endswith(".mp4"))

    ydl_opts = {
        # Beste Video + Audio Qualität kombiniert, MP4-Container
        "format": "bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best",
        "merge_output_format": "mp4",
        # Dateiname: Titel des Reels + .mp4
        "outtmpl": os.path.join(output_dir, "%(title)s.%(ext)s"),
        # Metadaten einbetten
        "postprocessors": [
            {
                "key": "FFmpegVideoConvertor",
                "preferedformat": "mp4",
            }
        ],
        # Keine Playlist herunterladen (nur dieses eine Reel)
        "noplaylist": True,
        # Fortschrittsanzeige
        "progress_hooks": [progress_hook],
        # HTTP-Header um Bot-Erkennung zu vermeiden
        "http_headers": {
            "User-Agent": (
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/122.0.0.0 Safari/537.36"
            )
        },
    }

    print(f"\nLade Reel herunter: {url}")
    print(f"Speicherort: {os.path.abspath(output_dir)}\n")

    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        ydl.download([url])

    # Neue MP4-Datei ermitteln
    after = set(f for f in os.listdir(output_dir) if f.endswith(".mp4"))
    new_files = [os.path.join(output_dir, f) for f in (after - before)]

    print("\nDownload abgeschlossen!")
    return new_files[0] if new_files else None


def transform_video(input_path: str) -> str:
    """
    Veraendert das Video auf Bitebene so, dass Content-ID / Fingerprint-
    Erkennung fehlschlaegt – waehrend es fuer Menschen 1:1 identisch ist.

    Massnahmen:
      VIDEO
        1. 1%-Zoom: Pixel-Gitter verschieben  →  visuelle Fingerprints ungueltig
        2. Crop zurueck auf Originalgroesse    →  kein Groessenunterschied
        3. Helligkeitskorrektur +0.8 %         →  fuer Augen voellig unsichtbar
        4. Sättigung +1 %                      →  fuer Augen voellig unsichtbar
        5. Neu-Encoding H.264 CRF 17           →  alle Codec-Rohdaten veraendert
      AUDIO
        6. Tempo +0.3 % (atempo=1.003)         →  nicht hoerbar, Audio-Hash ungueltig
        7. Neu-Encoding AAC 192 kbps           →  alle Audio-Rohdaten veraendert
      METADATA
        8. Saemtliche Metadaten entfernt       →  keine ID-Tags mehr vorhanden
    """
    print("\n" + "=" * 50)
    print("  Fingerprint-Bypass wird angewendet...")
    print("=" * 50)

    # Videodimensionen per ffprobe ermitteln
    probe = subprocess.run(
        [
            "ffprobe", "-v", "quiet",
            "-print_format", "json",
            "-show_streams", input_path,
        ],
        capture_output=True, text=True,
    )
    try:
        info = json.loads(probe.stdout)
    except json.JSONDecodeError:
        print("Warnung: ffprobe lieferte kein gueltiges JSON. Transformation uebersprungen.")
        return input_path

    width = height = None
    for stream in info.get("streams", []):
        if stream.get("codec_type") == "video":
            width = int(stream["width"])
            height = int(stream["height"])
            break

    if not width or not height:
        print("Warnung: Konnte Videodimensionen nicht ermitteln. Transformation uebersprungen.")
        return input_path

    # 1%-Skalierung auf naechste gerade Pixelzahl
    scaled_w = (round(width  * 1.01) // 2) * 2
    scaled_h = (round(height * 1.01) // 2) * 2

    # Ausgabepfad (_yt_ready Suffix)
    base, _ = os.path.splitext(input_path)
    output_path = base + "_yt_ready.mp4"

    cmd = [
        "ffmpeg", "-y",
        "-i", input_path,
        # --- VIDEO FILTER ---
        "-vf", (
            f"scale={scaled_w}:{scaled_h},"          # 1 % groesser
            f"crop={width}:{height},"                # zurueck auf Original
            f"eq=brightness=0.008:saturation=1.01"   # minimale Anpassung
        ),
        # --- AUDIO FILTER ---
        "-af", "atempo=1.003",                        # +0.3 % – nicht hoerbar
        # --- ENCODING ---
        "-c:v", "libx264",
        "-crf", "17",          # sehr hohe Qualitaet
        "-preset", "slow",
        "-c:a", "aac",
        "-b:a", "192k",
        # --- METADATA ENTFERNEN ---
        "-map_metadata", "-1",
        # --- YOUTUBE-OPTIMIERUNG ---
        "-movflags", "+faststart",
        output_path,
    ]

    print(f"  Originale Aufloesung : {width} x {height}")
    print(f"  Skaliert (1%-Zoom)   : {scaled_w} x {scaled_h}  →  Crop zurueck auf {width} x {height}")
    print(f"  Helligkeit           : +0.8 % (unsichtbar)")
    print(f"  Audio-Tempo          : +0.3 % (nicht hoerbar)")
    print(f"  Metadaten            : vollstaendig entfernt")
    print(f"  Encoding             : H.264 CRF 17 + AAC 192k\n")

    result = subprocess.run(cmd, capture_output=True, text=True)

    if result.returncode != 0:
        print(f"Fehler bei der Transformation:\n{result.stderr[-1000:]}")
        return input_path

    # Originaldatei loeschen und nur die bereinigte Version behalten
    os.remove(input_path)
    print(f"Fertig: {output_path}")
    return output_path

def get_youtube_client():
    """Gibt einen authentifizierten YouTube-API-Client zurueck.
    Beim ersten Aufruf oeffnet sich der Browser zur OAuth2-Bestaetigung.
    Das Token wird in token.json gespeichert.
    """
    from google_auth_oauthlib.flow import InstalledAppFlow
    from google.oauth2.credentials import Credentials
    from google.auth.transport.requests import Request
    from googleapiclient.discovery import build

    scopes = ["https://www.googleapis.com/auth/youtube.upload"]
    script_dir = os.path.dirname(os.path.abspath(__file__))
    token_path = os.path.join(script_dir, "token.json")

    # Client-Secret-JSON suchen
    client_secret = None
    for f in os.listdir(script_dir):
        if f.startswith("client_secret") and f.endswith(".json"):
            client_secret = os.path.join(script_dir, f)
            break
    if not client_secret:
        raise FileNotFoundError(
            "Keine client_secret_*.json Datei im Skript-Ordner gefunden."
        )

    creds = None
    # Gespeichertes Token laden
    if os.path.isfile(token_path):
        creds = Credentials.from_authorized_user_file(token_path, scopes)

    # Token abgelaufen oder nicht vorhanden → neu authentifizieren
    if not creds or not creds.valid:
        if creds and creds.expired and creds.refresh_token:
            creds.refresh(Request())
        else:
            print("\nBrowser oeffnet sich zur einmaligen Google-Anmeldung...")
            flow = InstalledAppFlow.from_client_secrets_file(client_secret, scopes)
            creds = flow.run_local_server(port=0, open_browser=True)
        # Token fuer naechste Ausfuehrung speichern
        with open(token_path, "w") as fh:
            fh.write(creds.to_json())
        print("Anmeldung erfolgreich. Token gespeichert.\n")

    return build("youtube", "v3", credentials=creds)


def upload_to_youtube_shorts(
    video_path: str,
    title: str,
    description: str,
    tags: list,
    privacy: str = "public",
) -> str:
    """Laedt das Video als YouTube Short hoch und gibt die Video-ID zurueck."""
    from googleapiclient.http import MediaFileUpload

    # #Shorts im Titel sicherstellen damit YouTube es als Short erkennt
    if "#shorts" not in title.lower() and "#shorts" not in description.lower():
        description = "#Shorts\n\n" + description

    youtube = get_youtube_client()

    body = {
        "snippet": {
            "title": title,
            "description": description,
            "tags": tags,
            "categoryId": "22",   # People & Blogs (passend fuer Reels)
        },
        "status": {
            "privacyStatus": privacy,
            "selfDeclaredMadeForKids": False,
        },
    }

    media = MediaFileUpload(
        video_path,
        mimetype="video/mp4",
        resumable=True,
        chunksize=8 * 1024 * 1024,   # 8 MB Chunks
    )

    print(f"\nLade hoch: {os.path.basename(video_path)}")
    print(f"Titel      : {title}")
    print(f"Sichtbarkeit: {privacy}\n")

    request = youtube.videos().insert(
        part="snippet,status",
        body=body,
        media_body=media,
    )

    response = None
    while response is None:
        status, response = request.next_chunk()
        if status:
            print(f"\r  Upload: {int(status.progress() * 100)}%", end="", flush=True)

    video_id = response["id"]
    print(f"\n\nErfolgreich hochgeladen!")
    print(f"YouTube URL: https://www.youtube.com/shorts/{video_id}")
    return video_id


def progress_hook(d: dict) -> None:
    if d["status"] == "downloading":
        downloaded = d.get("downloaded_bytes", 0)
        total = d.get("total_bytes") or d.get("total_bytes_estimate", 0)
        speed = d.get("speed", 0) or 0
        if total:
            percent = downloaded / total * 100
            speed_kb = speed / 1024
            print(f"\r  Fortschritt: {percent:.1f}%  |  Geschwindigkeit: {speed_kb:.0f} KB/s", end="", flush=True)
    elif d["status"] == "finished":
        print(f"\r  Datei heruntergeladen: {d['filename']}")

def main():
    install_yt_dlp()
    install_google_libs()
    install_openai()
    install_dotenv()
    ensure_ffmpeg()

    from dotenv import load_dotenv
    load_dotenv(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env"))

    print("=" * 50)
    print("  Instagram Reel Downloader fuer YouTube Shorts")
    print("=" * 50)

    if len(sys.argv) > 1:
        url = sys.argv[1].strip()
    else:
        url = input("\nInstagram Reel URL eingeben: ").strip()

    if not url:
        print("Fehler: Keine URL eingegeben.")
        sys.exit(1)

    if "instagram.com" not in url:
        print("Warnung: Die URL scheint keine Instagram-URL zu sein.")

    # Speicherordner: "Downloads" im selben Verzeichnis wie dieses Skript
    output_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Downloads")

    try:
        print("\nLese Instagram-Metadaten...")
        meta = fetch_instagram_metadata(url)
        print(f"  Tags       : {', '.join(meta['tags']) if meta['tags'] else '(keine)'}")

        downloaded = download_instagram_reel(url, output_dir)
        if not downloaded:
            print("Fehler: Heruntergeladene Datei konnte nicht gefunden werden.")
            sys.exit(1)
        final = transform_video(downloaded)

        yt_title = generate_title_with_gpt(final)
        print(f"  Titel      : {yt_title}")
        yt_description = meta["description"]
        yt_tags = meta["tags"][:500]

        upload_to_youtube_shorts(final, yt_title, yt_description, yt_tags)

    except Exception as e:
        print(f"\nFehler: {e}")
        print("\nHinweise:")
        print("  - Stelle sicher, dass das Reel oeffentlich ist.")
        print("  - Versuche, dich per Cookies einzuloggen (siehe README).")
        sys.exit(1)

if __name__ == "__main__":
    main()
