# ReelsConverter

C# WPF app with a Python backend for downloading and re-uploading short-form videos to YouTube Shorts. Supports Instagram, TikTok, and YouTube as sources.

---

## What it does

- Downloads videos from Instagram Reels, TikTok, and YouTube via yt-dlp
- Optionally runs the video through one of four FFmpeg processing passes to alter the file signature before upload
- Uploads directly to YouTube as a Short via the Data API v3


## Processing modes

| Mode | What it does |
|---|---|
| `metadata` | Rewrites metadata only, no re-encode |
| `light` | 1.005x zoom + crop, fast encode |
| `standard` | 1.01x zoom, brightness/saturation tweak, +0.3% speed |
| `strong` | 1.02x zoom, hue shift, +0.8% speed |

---

## Upload

Uploads to YouTube via OAuth2. Pulls title, description, and tags from the source video's metadata automatically. Tags are sanitized to fit YouTube's API limits (30 chars per tag, 500 chars total). Visibility can be set to public, unlisted, or private before uploading.

---

## Getting started

**Requirements**

- .NET 8 SDK
- Python 3.10+
- FFmpeg (downloaded automatically on first run)
- Google Cloud project with YouTube Data API enabled — drop your `client_secret_*.json` into the `backend/` folder and the OAuth flow handles the rest

**Install backend dependencies**
```powershell
cd backend
pip install -r requirements.txt
```
