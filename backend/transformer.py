"""FFmpeg fingerprint-bypass transformation."""
from __future__ import annotations
import json, os, subprocess

# (scale_factor, vf_extra, af_filter, crf, preset)
_METHODS: dict[str, tuple] = {
    "light":    (1.005, None,
                 None,           20, "fast"),
    "standard": (1.010, "eq=brightness=0.008:saturation=1.01",
                 "atempo=1.003", 17, "slow"),
    "strong":   (1.020, "eq=brightness=0.015:saturation=1.03:contrast=1.01,hue=h=2",
                 "atempo=1.008", 16, "slow"),
}


def transform_video(input_path: str, method: str = "standard") -> str:
    out = os.path.splitext(input_path)[0] + "_yt.mp4"

    # ── Metadata-only: no re-encode, just strip metadata ─────────────────────
    if method == "metadata":
        res = subprocess.run([
            "ffmpeg", "-y", "-i", input_path,
            "-c", "copy", "-map_metadata", "-1", "-movflags", "+faststart", out,
        ], capture_output=True, text=True)
        if res.returncode != 0:
            return input_path
        os.remove(input_path)
        return out

    # ── Video-filter methods ──────────────────────────────────────────────────
    if method not in _METHODS:
        method = "standard"

    probe = subprocess.run(
        ["ffprobe", "-v", "quiet", "-print_format", "json",
         "-show_streams", input_path],
        capture_output=True, text=True,
    )
    try:
        streams = json.loads(probe.stdout).get("streams", [])
    except json.JSONDecodeError:
        return input_path

    w = h = None
    for s in streams:
        if s.get("codec_type") == "video":
            w, h = int(s["width"]), int(s["height"])
            break
    if not (w and h):
        return input_path

    scale_factor, vf_extra, af_filter, crf, preset = _METHODS[method]
    sw = (round(w * scale_factor) // 2) * 2
    sh = (round(h * scale_factor) // 2) * 2

    vf = f"scale={sw}:{sh},crop={w}:{h}"
    if vf_extra:
        vf += f",{vf_extra}"

    cmd = ["ffmpeg", "-y", "-i", input_path, "-vf", vf]
    if af_filter:
        cmd += ["-af", af_filter]
    cmd += [
        "-c:v", "libx264", "-crf", str(crf), "-preset", preset,
        "-c:a", "aac", "-b:a", "192k",
        "-map_metadata", "-1", "-movflags", "+faststart", out,
    ]

    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0:
        return input_path
    os.remove(input_path)
    return out
