#!/usr/bin/env python3
"""Run a single-session HTTPS invitation until Ctrl+C or invitation expiry."""
from __future__ import annotations

import argparse
import json
import os
import queue
import re
import shutil
import subprocess
import threading
import time
from pathlib import Path

from server import DEFAULT_EXPORTS_DIR, DEFAULT_RESULTS_DIR, REPO_ROOT, iso_utc, utc_now
from share_server import create_server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--session", help="Exact study session ID. Prompted if omitted.")
    parser.add_argument("--port", type=int, default=8768)
    parser.add_argument("--hours", type=float, default=24)
    parser.add_argument("--exports-dir", type=Path, default=DEFAULT_EXPORTS_DIR)
    parser.add_argument("--results-dir", type=Path, default=DEFAULT_RESULTS_DIR)
    parser.add_argument("--snapshots-dir", type=Path, action="append", default=[])
    parser.add_argument("--state-dir", type=Path)
    parser.add_argument("--cloudflared", type=Path)
    parser.add_argument("--confirm-public-sharing", action="store_true",
                        help="Explicitly permit Cloudflare relay for the selected session without an interactive prompt.")
    args = parser.parse_args()
    session = args.session or input("Exact study Session ID: ").strip()
    server = create_server(args.exports_dir, args.results_dir, session, args.port, args.hours, args.snapshots_dir)
    runtime = args.cloudflared or REPO_ROOT / ".utmp/cloudflared/cloudflared.exe"
    if not runtime.is_file():
        found = shutil.which("cloudflared")
        if not found:
            server.server_close()
            raise SystemExit("cloudflared is not installed. Obtain it from the official Cloudflare download page.")
        runtime = Path(found)
    print("Only the selected session will be accessible. Cloudflare will relay its story, images and answers.", flush=True)
    if not args.confirm_public_sharing and input("Allow external sharing for this session? [y/N]: ").strip().lower() != "y":
        server.server_close()
        print("Cancelled. Nothing was shared.")
        return
    state_dir = args.state_dir or REPO_ROOT / ".utmp/delayed-share" / session
    state_dir.mkdir(parents=True, exist_ok=True)
    server_thread = threading.Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    lines = queue.Queue()
    tunnel = None
    reader = None
    try:
        tunnel = subprocess.Popen([str(runtime.resolve()), "tunnel", "--no-autoupdate", "--protocol", "http2",
            "--url", f"http://127.0.0.1:{server.server_port}"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="replace",
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        def read_tunnel():
            with (state_dir / "tunnel.log").open("w", encoding="utf-8", buffering=1) as log:
                for line in tunnel.stdout:
                    log.write(line)
                    lines.put(line)
        reader = threading.Thread(target=read_tunnel, daemon=True)
        reader.start()
        deadline = time.monotonic() + 90
        base_url = None
        while time.monotonic() < deadline and tunnel.poll() is None:
            try:
                line = lines.get(timeout=1)
            except queue.Empty:
                continue
            match = re.search(r"https://[a-z0-9-]+\.trycloudflare\.com", line)
            if match:
                base_url = match[0]
                break
        if base_url is None:
            raise RuntimeError("The HTTPS tunnel did not start. See the local tunnel.log; no invitation was issued.")
        invitation_url = base_url + "/#access=" + server.invitation.token
        state = {"url": invitation_url, "expiresAtUtc": iso_utc(server.invitation.expires_at),
                 "sessionId": session, "participantId": server.invitation.participant_id,
                 "serverPid": os.getpid(), "tunnelPid": tunnel.pid, "port": server.server_port}
        (state_dir / "invitation.json").write_text(json.dumps(state, indent=2), encoding="utf-8")
        print("INVITATION LINK (keep the full #access part):\n" + invitation_url, flush=True)
        print("Expires: " + state["expiresAtUtc"], flush=True)
        print("Keep this computer online. Press Ctrl+C to close access. Results save automatically to the research computer.", flush=True)
        while tunnel.poll() is None and utc_now() < server.invitation.expires_at:
            time.sleep(1)
        if utc_now() >= server.invitation.expires_at:
            print("Invitation expired. Closing access.", flush=True)
        elif tunnel.poll() is not None:
            print("Tunnel stopped. The link is no longer available.", flush=True)
    except KeyboardInterrupt:
        print("Closing access.", flush=True)
    finally:
        server.shutdown()
        server.server_close()
        server_thread.join(timeout=3)
        if tunnel and tunnel.poll() is None:
            tunnel.terminate()
            try:
                tunnel.wait(timeout=10)
            except subprocess.TimeoutExpired:
                tunnel.kill()
                tunnel.wait(timeout=3)
        if reader:
            reader.join(timeout=3)


if __name__ == "__main__":
    main()
