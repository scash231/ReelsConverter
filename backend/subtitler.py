"""Auto-generate subtitles from video using faster-whisper."""
from __future__ import annotations

import os, subprocess, sys, tempfile
from typing import Optional

_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)


def generate_subtitles(
    video_path: str,
    model_size: str = "base",
    language: Optional[str] = None,
) -> str:
    """Extract audio, run speech-to-text, return SRT string."""

    # Lazy-install faster-whisper on first use (avoids slowing backend startup)
    try:
        from faster_whisper import WhisperModel
    except ImportError:
        print("[subtitler] Installing faster-whisper …", flush=True)
        subprocess.check_call(
            [sys.executable, "-m", "pip", "install", "faster-whisper", "-q"]
        )
        from faster_whisper import WhisperModel

    if not os.path.isfile(video_path):
        raise FileNotFoundError(f"Video not found: {video_path}")

    tmp_wav = tempfile.mktemp(suffix=".wav")
    try:
        print(f"[subtitler] Extracting audio from {video_path!r}", flush=True)
        _extract_audio(video_path, tmp_wav)

        print(f"[subtitler] Loading model '{model_size}' …", flush=True)
        model = WhisperModel(model_size, device="cpu", compute_type="int8")

        lang_arg = language if language and language != "auto" else None
        print(f"[subtitler] Transcribing (language={lang_arg or 'auto-detect'}) …", flush=True)
        segments, info = model.transcribe(tmp_wav, language=lang_arg, beam_size=5)

        srt_lines: list[str] = []
        for i, seg in enumerate(segments, start=1):
            srt_lines.append(str(i))
            srt_lines.append(f"{_fmt_ts(seg.start)} --> {_fmt_ts(seg.end)}")
            srt_lines.append(seg.text.strip())
            srt_lines.append("")

        detected = info.language if hasattr(info, "language") else "?"
        print(f"[subtitler] Done – {i if srt_lines else 0} segments, detected language: {detected}", flush=True)
        return "\n".join(srt_lines)
    finally:
        if os.path.exists(tmp_wav):
            os.remove(tmp_wav)


def _extract_audio(video_path: str, wav_path: str) -> None:
    """Extract audio from video to 16 kHz mono WAV using ffmpeg."""
    cmd = [
        "ffmpeg", "-y",
        "-i", video_path,
        "-vn",
        "-acodec", "pcm_s16le",
        "-ar", "16000",
        "-ac", "1",
        wav_path,
    ]
    r = subprocess.run(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        encoding="utf-8",
        errors="replace",
        creationflags=_NO_WINDOW,
    )
    if r.returncode != 0:
        raise RuntimeError(f"ffmpeg audio extraction failed: {r.stderr[:300]}")


def _fmt_ts(seconds: float) -> str:
    """Format seconds to SRT timestamp: HH:MM:SS,mmm"""
    h = int(seconds // 3600)
    m = int((seconds % 3600) // 60)
    s = int(seconds % 60)
    ms = int((seconds % 1) * 1000)
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"
