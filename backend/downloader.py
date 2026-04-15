"""yt-dlp download helpers - speed-optimized.

Improvements:
 - 8 concurrent fragment downloads (was 4)
 - Automatic aria2c external downloader when available (~5-10x faster)
 - Retry logic for fragments and connections
 - Larger buffer size for faster I/O
 - Updated User-Agent
 - URL sanitisation (invisible Unicode chars from social-media copy-paste)
 - Automatic fallback retry without aria2c on OSError
"""
from __future__ import annotations
import os, re, shutil, sys, uuid
import yt_dlp


class DownloadCancelled(Exception):
    """Raised when a download is cancelled by the user."""


_UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/130.0.0.0 Safari/537.36"
)

# Invisible / zero-width Unicode codepoints that browsers and social-media
# apps love to inject when the user copies a URL.
_INVISIBLE_RE = re.compile(
    "[\u200b\u200c\u200d\u200e\u200f"
    "\u202a\u202b\u202c\u202d\u202e"
    "\u2060\u2066\u2067\u2068\u2069"
    "\ufeff\u00ad\u034f\u061c\u115f\u1160"
    "\u17b4\u17b5\u180e\ufff0-\ufff8\ufffa-\ufffd]"
)


def _clean_url(raw: str) -> str:
    """Strip invisible Unicode chars and whitespace from a pasted URL."""
    return _INVISIBLE_RE.sub("", raw).strip()


class _YtdlpLogger:
    """Forward yt-dlp log messages to stdout so they appear in the backend console."""
    def debug(self, msg: str) -> None:
        if msg.startswith('[debug] '):
            return
        print(f"[yt-dlp] {msg}", flush=True)
    def info(self, msg: str) -> None:
        print(f"[yt-dlp] {msg}", flush=True)
    def warning(self, msg: str) -> None:
        print(f"[yt-dlp] WARNING: {msg}", flush=True)
    def error(self, msg: str) -> None:
        print(f"[yt-dlp] ERROR: {msg}", flush=True)

_logger = _YtdlpLogger()


def fetch_metadata(url: str) -> dict:
    url = _clean_url(url)
    opts = {
        "quiet": False,
        "no_warnings": False,
        "skip_download": True,
        "logger": _logger,
        "http_headers": {"User-Agent": _UA},
        "socket_timeout": 15,
        "extractor_retries": 3,
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


def _safe_title(raw: str) -> str:
    """Sanitise a video title into a Windows-safe filename stem."""
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', raw).strip('. ')
    if re.match(r'^(CON|PRN|AUX|NUL|COM\d|LPT\d)$', name, re.IGNORECASE):
        name = f"_{name}"
    return name[:80] or "video"


def _has_aria2c() -> bool:
    return shutil.which("aria2c") is not None


def download_video(url: str, output_dir: str, progress_hook,
                   cancel_check=None, quality: str = "best") -> str | None:
    url = _clean_url(url)
    output_dir = os.path.abspath(os.path.normpath(output_dir))
    os.makedirs(output_dir, exist_ok=True)
    stem = uuid.uuid4().hex[:12]
    print(f"[downloader] yt-dlp {yt_dlp.version.__version__}", flush=True)
    print(f"[downloader] URL: {url[:120]}", flush=True)
    print(f"[downloader] output_dir: {output_dir}", flush=True)
    print(f"[downloader] quality: {quality}", flush=True)
    is_audio = (quality == "audio")

    def _hook(d: dict):
        if cancel_check and cancel_check():
            raise DownloadCancelled("Download abgebrochen")
        progress_hook(d)

    _was_cancelled = [False]

    def _hook_wrap(d: dict):
        try:
            _hook(d)
        except DownloadCancelled:
            _was_cancelled[0] = True
            raise

    def _build_opts(*, use_aria: bool = True, fast_fragments: bool = True,
                    safe_format: bool = False) -> dict:
        if is_audio:
            fmt = "bestaudio[ext=m4a]/bestaudio/best"
        elif safe_format:
            fmt = "best[ext=mp4]/best"
        elif quality in ("1080", "720", "480", "360"):
            fmt = (f"bestvideo[height<={quality}][ext=mp4]+bestaudio[ext=m4a]/"
                   f"bestvideo[height<={quality}]+bestaudio/best")
        else:
            fmt = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best"
        o = {
            "format": fmt,
            "outtmpl": os.path.join(output_dir, f"{stem}.%(ext)s"),
            "noplaylist": True,
            "updatetime": False,
            "windowsfilenames": True,
            "noprogress": False,
            "progress_hooks": [_hook_wrap],
            "logger": _logger,
            "http_headers": {"User-Agent": _UA},
            "retries": 5,
            "fragment_retries": 10,
            "extractor_retries": 3,
            "socket_timeout": 15,
            "buffersize": 1024 * 64,
        }
        if is_audio:
            o["postprocessors"] = [{
                "key": "FFmpegExtractAudio",
                "preferredcodec": "mp3",
                "preferredquality": "192",
            }]
        else:
            o["merge_output_format"] = "mp4"
            if not safe_format:
                o["postprocessors"] = [{"key": "FFmpegVideoConvertor", "preferedformat": "mp4"}]
        if fast_fragments:
            o["concurrent_fragment_downloads"] = 8
        if use_aria and _has_aria2c():
            o["external_downloader"] = "aria2c"
            o["external_downloader_args"] = {
                "default": [
                    "--min-split-size=1M",
                    "--max-connection-per-server=16",
                    "--split=16",
                    "--max-concurrent-downloads=16",
                ]
            }
        return o

    # Try with full speed opts first; on failure fall back to safe mode.
    info = None
    last_exc = None
    for attempt, (aria, frag, safe) in enumerate([
        (True, True, False),
        (False, False, True),
    ]):
        _was_cancelled[0] = False
        opts = _build_opts(use_aria=aria, fast_fragments=frag, safe_format=safe)
        try:
            with yt_dlp.YoutubeDL(opts) as ydl:
                info = ydl.extract_info(url, download=True)
            break  # success
        except DownloadCancelled:
            return None
        except Exception as exc:
            if _was_cancelled[0] or (cancel_check and cancel_check()):
                return None
            last_exc = exc
            if attempt == 0:
                import traceback as _tb
                print(f"[downloader] attempt 1 failed ({type(exc).__name__}), retrying safe mode…",
                      flush=True)
                print(_tb.format_exc(), flush=True)
                # Clean up partial files before retry
                for f in os.listdir(output_dir):
                    if f.startswith(stem):
                        try:
                            os.remove(os.path.join(output_dir, f))
                        except OSError:
                            pass
                continue  # retry with safe opts
            raise

    downloaded = None
    for f in os.listdir(output_dir):
        if f.startswith(stem) and os.path.isfile(os.path.join(output_dir, f)):
            if not f.endswith(('.part', '.ytdl', '.temp')):
                downloaded = os.path.join(output_dir, f)
                break
    if not downloaded:
        return None

    # Rename to human-readable title
    title = (info or {}).get("title", "")
    safe = _safe_title(title) if title else stem
    ext = os.path.splitext(downloaded)[1]
    target = os.path.join(output_dir, f"{safe}{ext}")
    n = 1
    while os.path.exists(target):
        target = os.path.join(output_dir, f"{safe} ({n}){ext}")
        n += 1
    try:
        os.rename(downloaded, target)
        return target
    except OSError:
        return downloaded
