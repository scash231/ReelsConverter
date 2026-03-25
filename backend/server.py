#!/usr/bin/env python3
"""
ReelsConverter Backend – FastAPI Server
Supports Instagram, TikTok and YouTube downloads + optional YouTube upload.
"""
from __future__ import annotations
import subprocess, sys, os

_DEPS = {
    "fastapi": "fastapi",
    "uvicorn": "uvicorn[standard]",
    "yt_dlp":  "yt-dlp",
}
for mod, pkg in _DEPS.items():
    try:
        __import__(mod.split(".")[0])
    except ImportError:
        subprocess.check_call([sys.executable, "-m", "pip", "install", pkg, "-q"])

import asyncio, json, tempfile, uuid
from typing import Any, Optional
from fastapi import BackgroundTasks, FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
import uvicorn

from ffmpeg_manager import ensure_ffmpeg
from downloader    import download_video, fetch_metadata
from transformer   import transform_video
from uploader      import upload_to_youtube

app = FastAPI(title="ReelsConverter API", version="2.0.0")
app.add_middleware(
    CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"],
)

_jobs: dict[str, dict[str, Any]] = {}


# ── Models ────────────────────────────────────────────────────────────────────
class MetadataReq(BaseModel):
    url: str

class JobReq(BaseModel):
    url:                str
    mode:               str  # "upload" | "download"
    platform:           str  # "instagram" | "tiktok" | "youtube"
    title:              Optional[str] = None
    description:        Optional[str] = None
    tags:               Optional[list[str]] = []
    output_dir:         Optional[str] = None
    privacy:            Optional[str] = "public"
    fingerprint:        Optional[bool] = True
    fingerprint_method: Optional[str] = "standard"


# ── Lifecycle ─────────────────────────────────────────────────────────────────
@app.on_event("startup")
async def _startup():
    await asyncio.get_event_loop().run_in_executor(None, ensure_ffmpeg)


# ── Endpoints ─────────────────────────────────────────────────────────────────
@app.get("/api/health")
async def health():
    return {"status": "ok", "version": "2.0.0"}

@app.post("/api/metadata")
async def api_metadata(req: MetadataReq):
    try:
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, fetch_metadata, req.url)
    except Exception as exc:
        raise HTTPException(400, str(exc))

@app.post("/api/jobs")
async def create_job(req: JobReq, bg: BackgroundTasks):
    jid = str(uuid.uuid4())
    _jobs[jid] = dict(
        id=jid, status="pending", progress=0,
        message="Warte auf Start…", result=None, error=None,
    )
    bg.add_task(_run_job, jid, req)
    return {"job_id": jid}

@app.get("/api/jobs/{jid}")
async def get_job(jid: str):
    if jid not in _jobs:
        raise HTTPException(404)
    return _jobs[jid]

@app.get("/api/jobs/{jid}/stream")
async def stream_job(jid: str):
    if jid not in _jobs:
        raise HTTPException(404)

    async def _gen():
        while True:
            job = _jobs.get(jid, {})
            yield f"data: {json.dumps(job)}\n\n"
            if job.get("status") in ("completed", "error"):
                break
            await asyncio.sleep(0.25)

    return StreamingResponse(
        _gen(),
        media_type="text/event-stream",
        headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"},
    )


# ── Background job ────────────────────────────────────────────────────────────
async def _run_job(jid: str, req: JobReq):
    loop = asyncio.get_event_loop()

    def up(msg: str, pct: int = -1):
        _jobs[jid]["message"] = msg
        if pct >= 0:
            _jobs[jid]["progress"] = pct

    try:
        _jobs[jid]["status"] = "running"
        up("Lade Video herunter…", 5)

        dl_dir = req.output_dir if (req.mode == "download" and req.output_dir) else tempfile.gettempdir()

        def _hook(d: dict):
            if d["status"] == "downloading":
                dl  = d.get("downloaded_bytes", 0)
                tot = d.get("total_bytes") or d.get("total_bytes_estimate") or 1
                pct = int(dl / tot * 40) + 5
                up(f"Download: {dl / 1_048_576:.1f} / {tot / 1_048_576:.1f} MB", pct)
            elif d["status"] == "finished":
                up("Download fertig, verarbeite…", 50)

        path = await loop.run_in_executor(
            None, lambda: download_video(req.url, dl_dir, _hook)
        )
        if not path:
            raise RuntimeError("Keine MP4-Datei nach Download gefunden.")

        if req.fingerprint:
            up("Fingerprint-Bypass wird angewendet…", 55)
            path = await loop.run_in_executor(
                None, lambda: transform_video(path, req.fingerprint_method or "standard")
            )
            up("Transformation abgeschlossen.", 70)

        if req.mode == "upload":
            up("YouTube-Upload läuft…", 75)

            def _up_cb(p: int):
                up(f"YouTube Upload: {p}%", 75 + p // 4)

            vid = await loop.run_in_executor(
                None,
                lambda: upload_to_youtube(
                    path,
                    req.title or "Video",
                    req.description or "",
                    req.tags or [],
                    req.privacy or "public",
                    _up_cb,
                ),
            )
            if os.path.exists(path):
                os.remove(path)
            _jobs[jid]["result"] = {
                "video_id": vid,
                "url": f"https://youtube.com/shorts/{vid}",
            }
        else:
            _jobs[jid]["result"] = {"file_path": path}

        _jobs[jid].update(status="completed", progress=100,
                          message="Erfolgreich abgeschlossen! 🎉")
    except Exception as exc:
        _jobs[jid].update(status="error", error=str(exc),
                          message=f"Fehler: {exc}")


if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8765, log_level="warning")
