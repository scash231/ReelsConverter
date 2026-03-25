"""YouTube Data API v3 upload helper."""
from __future__ import annotations
import os
from typing import Callable
from googleapiclient.http import MediaFileUpload


def _sanitize_tags(tags: list[str]) -> list[str]:
    """YouTube constraints: each tag ≤ 30 chars, total across all tags ≤ 500 chars."""
    result, total = [], 0
    for tag in tags:
        tag = tag.strip().lstrip("#")
        if not tag or len(tag) > 30:
            continue
        if total + len(tag) > 500:
            break
        result.append(tag)
        total += len(tag)
    return result


def upload_to_youtube(
    video_path: str,
    title: str,
    description: str,
    tags: list[str],
    privacy: str,
    progress_cb: Callable[[int], None],
) -> str:
    if "#shorts" not in description.lower():
        description = "#Shorts\n\n" + description

    yt = _get_client()
    body = {
        "snippet": {
            "title": title[:100],
            "description": description,
            "tags": _sanitize_tags(tags),
            "categoryId": "22",
        },
        "status": {"privacyStatus": privacy, "selfDeclaredMadeForKids": False},
    }
    media = MediaFileUpload(
        video_path, mimetype="video/mp4", resumable=True, chunksize=8 << 20
    )
    req = yt.videos().insert(part="snippet,status", body=body, media_body=media)

    response = None
    while response is None:
        status, response = req.next_chunk()
        if status:
            progress_cb(int(status.progress() * 100))
    return response["id"]


def _get_client():
    from google_auth_oauthlib.flow import InstalledAppFlow
    from google.oauth2.credentials import Credentials
    from google.auth.transport.requests import Request
    from googleapiclient.discovery import build

    scopes = ["https://www.googleapis.com/auth/youtube.upload"]
    here = os.path.dirname(os.path.abspath(__file__))
    token = os.path.join(here, "token.json")
    secret = next(
        (os.path.join(here, f)
         for f in os.listdir(here)
         if f.startswith("client_secret") and f.endswith(".json")),
        None,
    )
    if not secret:
        raise FileNotFoundError("client_secret_*.json not found in backend/")

    # Validate that the secret file contains proper JSON
    import json as _json
    try:
        with open(secret) as _f:
            data = _json.load(_f)
        if "installed" not in data and "web" not in data:
            raise ValueError(
                "client_secret.json ist ungültig. Bitte lade die vollständige "
                "OAuth-JSON-Datei von der Google Cloud Console herunter "
                "(APIs & Services → Credentials → OAuth 2.0 Client → Download JSON)."
            )
    except _json.JSONDecodeError:
        raise ValueError(
            "client_secret.json enthält kein gültiges JSON. "
            "Bitte lade die vollständige OAuth-JSON-Datei von der "
            "Google Cloud Console herunter."
        )

    creds = None
    if os.path.isfile(token):
        creds = Credentials.from_authorized_user_file(token, scopes)
    if not creds or not creds.valid:
        if creds and creds.expired and creds.refresh_token:
            creds.refresh(Request())
        else:
            flow = InstalledAppFlow.from_client_secrets_file(secret, scopes)
            creds = flow.run_local_server(port=0, open_browser=True)
        with open(token, "w") as f:
            f.write(creds.to_json())
    return build("youtube", "v3", credentials=creds)
