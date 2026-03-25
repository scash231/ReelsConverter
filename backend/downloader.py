"""yt-dlp download helpers for Instagram, TikTok, and YouTube."""
from __future__ import annotations
import os, re
import yt_dlp

_UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/125.0.0.0 Safari/537.36"
)


def fetch_metadata(url: str) -> dict:
    opts = {
        "quiet": True,
        "no_warnings": True,
        "skip_download": True,
        "http_headers": {"User-Agent": _UA},
    }
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(url, download=False)

    desc = (info.get("description") or "").strip()
    tags = list(dict.fromkeys(t.lstrip("#") for t in re.findall(r"#\w+", desc)))
    return {
        "title":       info.get("title", ""),
        "description": desc,
        "tags":        tags,
        "thumbnail":   info.get("thumbnail", ""),
        "duration":    info.get("duration", 0),
        "uploader":    info.get("uploader", ""),
    }


def download_video(url: str, output_dir: str, progress_hook) -> str | None:
    os.makedirs(output_dir, exist_ok=True)
    before = {f for f in os.listdir(output_dir) if f.endswith(".mp4")}

    opts = {
        "format": "bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best",
        "merge_output_format": "mp4",
        "outtmpl": os.path.join(output_dir, "%(title).80s.%(ext)s"),
        "postprocessors": [{"key": "FFmpegVideoConvertor", "preferedformat": "mp4"}],
        "noplaylist": True,
        "progress_hooks": [progress_hook],
        "http_headers": {"User-Agent": _UA},
    }
    with yt_dlp.YoutubeDL(opts) as ydl:
        ydl.download([url])

    after = {f for f in os.listdir(output_dir) if f.endswith(".mp4")}
    new = [os.path.join(output_dir, f) for f in (after - before)]
    return new[0] if new else None
