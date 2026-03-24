import datetime
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


def install_yt_dlp() -> None:
    """Installiert yt-dlp falls nicht vorhanden."""
    try:
        import yt_dlp  # noqa: F401
    except ImportError:
        print("Installiere yt-dlp...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "yt-dlp"])
        print("yt-dlp erfolgreich installiert.\n")


def ensure_ffmpeg() -> None:
    """Stellt sicher, dass ffmpeg/ffprobe verfuegbar sind."""
    script_dir = Path(__file__).resolve().parent
    local_ffmpeg_dir = script_dir / "ffmpeg_bin"

    ffmpeg_exe = local_ffmpeg_dir / "ffmpeg.exe"
    ffprobe_exe = local_ffmpeg_dir / "ffprobe.exe"

    if ffmpeg_exe.is_file() and ffprobe_exe.is_file():
        os.environ["PATH"] = str(local_ffmpeg_dir) + os.pathsep + os.environ.get("PATH", "")
        return

    if shutil.which("ffmpeg") and shutil.which("ffprobe"):
        return

    raise FileNotFoundError(
        "ffmpeg/ffprobe wurden nicht gefunden. Lege ffmpeg.exe und ffprobe.exe in ffmpeg_bin ab."
    )


def read_reel_urls(reels_file: Path) -> list[str]:
    """Liest alle Instagram-URLs aus reels.txt (leere Zeilen und Kommentare werden ignoriert)."""
    if not reels_file.is_file():
        raise FileNotFoundError(f"Datei nicht gefunden: {reels_file}")

    urls: list[str] = []
    for line in reels_file.read_text(encoding="utf-8").splitlines():
        entry = line.strip()
        if not entry or entry.startswith("#"):
            continue
        urls.append(entry)

    if not urls:
        raise ValueError("reels.txt enthaelt keine gueltigen Links.")

    return urls


def progress_hook(status: dict) -> None:
    state = status.get("status")
    if state == "downloading":
        downloaded = status.get("downloaded_bytes", 0)
        total = status.get("total_bytes") or status.get("total_bytes_estimate", 0)
        if total:
            percent = downloaded / total * 100
            print(f"\r  Fortschritt: {percent:5.1f}%", end="", flush=True)
    elif state == "finished":
        print("\r  Download fertig.                         ")


def download_reels(urls: list[str], target_dir: Path) -> list[Path]:
    """Laedt alle Reel-URLs herunter und gibt die Dateipfade in Reihenfolge zurueck."""
    import yt_dlp

    target_dir.mkdir(parents=True, exist_ok=True)
    downloaded_files: list[Path] = []

    for index, url in enumerate(urls, start=1):
        print(f"\n[{index}/{len(urls)}] Lade Reel: {url}")
        output_template = str(target_dir / f"{index:03d}.%(ext)s")

        ydl_opts = {
            "format": "bestvideo+bestaudio/best",
            "merge_output_format": "mp4",
            "outtmpl": output_template,
            "noplaylist": True,
            "quiet": True,
            "no_warnings": True,
            "progress_hooks": [progress_hook],
            "http_headers": {
                "User-Agent": (
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                    "AppleWebKit/537.36 (KHTML, like Gecko) "
                    "Chrome/122.0.0.0 Safari/537.36"
                )
            },
        }

        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            ydl.download([url])

        reel_file = target_dir / f"{index:03d}.mp4"
        if not reel_file.is_file():
            raise FileNotFoundError(f"Download fehlgeschlagen fuer URL: {url}")

        downloaded_files.append(reel_file)

    return downloaded_files


def normalize_clip(input_file: Path, output_file: Path) -> None:
    """Bringt Clips auf ein einheitliches Format, damit ffmpeg sie sicher zusammenfuegen kann."""
    cmd = [
        "ffmpeg",
        "-y",
        "-i",
        str(input_file),
        "-vf",
        (
            "scale=1080:1920:force_original_aspect_ratio=decrease,"
            "pad=1080:1920:(ow-iw)/2:(oh-ih)/2:black,"
            "fps=30"
        ),
        "-c:v",
        "libx264",
        "-preset",
        "medium",
        "-crf",
        "20",
        "-pix_fmt",
        "yuv420p",
        "-c:a",
        "aac",
        "-b:a",
        "160k",
        "-ar",
        "48000",
        "-movflags",
        "+faststart",
        str(output_file),
    ]

    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(
            f"Normalisierung fehlgeschlagen fuer {input_file.name}:\n{result.stderr[-1200:]}"
        )


def concat_clips(clips: list[Path], output_file: Path) -> None:
    """Fuegt normalisierte Clips in gegebener Reihenfolge zusammen."""
    if not clips:
        raise ValueError("Keine Clips zum Zusammenfuegen vorhanden.")

    with tempfile.TemporaryDirectory() as temp_dir:
        temp_path = Path(temp_dir)
        normalized_dir = temp_path / "normalized"
        normalized_dir.mkdir(parents=True, exist_ok=True)

        normalized_clips: list[Path] = []
        for idx, clip in enumerate(clips, start=1):
            normalized_clip = normalized_dir / f"n_{idx:03d}.mp4"
            print(f"\nNormalisiere Clip {idx}/{len(clips)}: {clip.name}")
            normalize_clip(clip, normalized_clip)
            normalized_clips.append(normalized_clip)

        concat_file = temp_path / "concat_list.txt"
        with concat_file.open("w", encoding="utf-8") as fh:
            for clip in normalized_clips:
                escaped = str(clip).replace("'", "'\\''")
                fh.write(f"file '{escaped}'\n")

        cmd = [
            "ffmpeg",
            "-y",
            "-f",
            "concat",
            "-safe",
            "0",
            "-i",
            str(concat_file),
            "-c",
            "copy",
            str(output_file),
        ]
        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode != 0:
            raise RuntimeError(f"Zusammenschnitt fehlgeschlagen:\n{result.stderr[-1500:]}")


def build_output_path(done_dir: Path) -> Path:
    """Erzeugt einen eindeutigen Dateinamen fuer die Compilation."""
    done_dir.mkdir(parents=True, exist_ok=True)
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    return done_dir / f"try_not_to_laugh_compilation_{ts}.mp4"


def main() -> None:
    print("=" * 60)
    print("Instagram Reels -> YouTube Try Not To Laugh Compilation")
    print("=" * 60)

    install_yt_dlp()
    ensure_ffmpeg()

    root_dir = Path(__file__).resolve().parent
    reels_file = root_dir / "reels.txt"
    downloads_dir = root_dir / "downloads"
    done_dir = root_dir / "done_compilation"

    try:
        urls = read_reel_urls(reels_file)
        print(f"\nGefundene Reel-Links: {len(urls)}")

        clips = download_reels(urls, downloads_dir)
        output_file = build_output_path(done_dir)

        print("\nStarte Zusammenschnitt...")
        concat_clips(clips, output_file)

        print("\n" + "=" * 60)
        print("FERTIG")
        print(f"Compilation gespeichert unter:\n{output_file}")
        print("=" * 60)
    except Exception as exc:
        print(f"\nFehler: {exc}")
        sys.exit(1)


if __name__ == "__main__":
    main()
