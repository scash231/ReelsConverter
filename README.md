# MediaConverter - VideoConverter

Alpha - https://github.com/scash231/VideoConverter/releases/tag/27H1

Modern C# WPF desktop application with a high-performance Python backend for downloading, converting, and auto-uploading media across YouTube Music, YouTube Shorts, Instagram Reels, TikTok, and universal web sources with built-in anti-detection video fingerprinting.
Check Requirments.txt

# 26H2 Beta

## New

- **YouTube Music support** — download YT Music tracks up to 320 kbps, with artist/album/year/track count pulled from metadata
- **5 new languages** — Spanish, French, Italian, Japanese, Chinese
- **Phased progress window** — download / fingerprint-bypass / upload, each with its own progress and live speed + ETA
- **Success/failure notifications** with optional sound
- **Window resize grip** — every window is resizable and remembers its last size
- **Designer window redesign** — new Layout and Animations tabs, gradient controls, collapsible color categories

## Improved

- Rounded window corners now clip properly at the OS level (no more square edges under the rounded XAML)
- Blur enabled by default on LogViewer, DevConsole, DescEditor
- Sandstone theme preset fixed for proper glassmorphism
- Popup backgrounds cleaner and more opaque
- Animation system reworked (easing, spring dynamics, staggering)

## Misc

- `mutagen` dependency for YT Music metadata
- New endpoint: `POST /api/ytmusic/metadata`
- 18 new theme settings fields
- Console log filters (System / Backend / FFmpeg)
- Compact mode toggle

---
*Note: build artifacts are not the same as release assets — release assets can be modified after the fact.*
#

- YoutubeMusic support - Lossless/Mp3
- Audio/Video support
- 4k/2k/1080p/720p/480p/Audio Only
- Metadata and Video/Audio Changer Option to avoid automaitc Heuristic Detections from Several Plattoforms.

- Fully Automatic Uploud function to Youtube (You need your API Key for that - Requirments)
<img width="764" height="562" alt="image" src="https://github.com/user-attachments/assets/372b9f46-8634-4f2d-a26a-5fa01657ee86" />
