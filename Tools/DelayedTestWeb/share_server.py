#!/usr/bin/env python3
"""Single-session invitation gate for a loopback-only delayed-test server."""
from __future__ import annotations

import argparse
import json
import secrets
from datetime import timedelta
from functools import partial
from http import HTTPStatus
from http.cookies import SimpleCookie
from http.server import ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

from server import (DEFAULT_EXPORTS_DIR, DEFAULT_RESULTS_DIR, DEFAULT_WEB_DIR, REPO_ROOT, ApiError,
                    DelayedTestRequestHandler, DelayedTestService, iso_utc, resolve_session, utc_now)

COOKIE_NAME = "memorypalace_invitation"
STATIC_PATHS = {"/", "/index.html", "/app.js", "/styles.css"}


class Invitation:
    def __init__(self, session_id, participant_id, hours=24):
        if not 0 < hours <= 168:
            raise ValueError("Invitation duration must be between 0 and 168 hours.")
        self.session_id = session_id
        self.participant_id = participant_id
        self.token = secrets.token_urlsafe(32)
        self.cookie = secrets.token_urlsafe(32)
        self.expires_at = utc_now() + timedelta(hours=hours)

    def check_active(self):
        if utc_now() >= self.expires_at:
            raise ApiError(HTTPStatus.GONE, "This invitation has expired. Contact the researcher.", "invitation_expired")

    @staticmethod
    def equal(provided, expected):
        return isinstance(provided, str) and provided.isascii() and secrets.compare_digest(provided, expected)

    def check_token(self, provided):
        self.check_active()
        if not self.equal(provided, self.token):
            raise ApiError(HTTPStatus.FORBIDDEN, "Open the complete invitation link provided by the researcher.", "access_denied")


class SingleSessionService(DelayedTestService):
    def __init__(self, exports_dir, results_dir, record, snapshot_dirs=None):
        super().__init__(exports_dir, results_dir, snapshot_dirs)
        self.record = record

    def start(self, lookup_id):
        query = str(lookup_id or "").strip().casefold()
        if query not in {self.record.session_id.casefold(), self.record.participant_id.casefold()}:
            raise ApiError(HTTPStatus.FORBIDDEN, "This invitation does not allow that study session.", "session_not_allowed")
        # Exact session lookup prevents switching to a participant's newer export.
        return super().start(self.record.session_id)


class InvitationHandler(DelayedTestRequestHandler):
    @property
    def invitation(self):
        return self.server.invitation

    def end_headers(self):
        self.send_header("X-Frame-Options", "DENY")
        super().end_headers()

    def require_access(self):
        self.invitation.check_active()
        cookie = SimpleCookie()
        try:
            cookie.load(self.headers.get("Cookie", ""))
            supplied = cookie[COOKIE_NAME].value if COOKIE_NAME in cookie else ""
        except Exception:
            supplied = ""
        if not self.invitation.equal(supplied, self.invitation.cookie):
            raise ApiError(HTTPStatus.FORBIDDEN, "Open the complete invitation link provided by the researcher.", "access_denied")

    def require_same_origin(self):
        origin = self.headers.get("Origin")
        # Python clients may omit Origin; browser cross-origin submissions may not.
        if origin and (urlparse(origin).netloc.casefold() != self.headers.get("Host", "").casefold()
                       or urlparse(origin).scheme not in {"http", "https"}):
            raise ApiError(HTTPStatus.FORBIDDEN, "Cross-origin requests are not allowed.", "origin_denied")

    def do_GET(self):
        path = urlparse(self.path).path
        if path in STATIC_PATHS:
            return super().do_GET()
        try:
            self.require_access()
        except ApiError as error:
            self._send_error(error)
            return
        if path.startswith("/api/"):
            return super().do_GET()
        self._send_error(ApiError(HTTPStatus.NOT_FOUND, "Not found.", "route_not_found"))

    def do_HEAD(self):
        if urlparse(self.path).path not in STATIC_PATHS:
            self._send_error(ApiError(HTTPStatus.NOT_FOUND, "Not found.", "route_not_found"))
            return
        if urlparse(self.path).path == "/":
            self.path = "/index.html"
        super().do_HEAD()

    def do_POST(self):
        try:
            self.require_same_origin()
            if urlparse(self.path).path == "/api/access":
                self._read_json()
                authorization = self.headers.get("Authorization", "")
                token = authorization[7:] if authorization.startswith("Bearer ") else ""
                self.invitation.check_token(token)
                payload = {"ok": True, "participantId": self.invitation.participant_id,
                           "sessionId": self.invitation.session_id, "expiresAtUtc": iso_utc(self.invitation.expires_at)}
                body = json.dumps(payload).encode("utf-8")
                seconds = max(1, int((self.invitation.expires_at - utc_now()).total_seconds()))
                self.send_response(HTTPStatus.OK)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Set-Cookie", f"{COOKIE_NAME}={self.invitation.cookie}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age={seconds}")
                self.end_headers()
                self.wfile.write(body)
                return
            self.require_access()
        except ApiError as error:
            self._send_error(error)
            return
        super().do_POST()

    def log_message(self, format_string, *args):
        # Never log query strings, cookies, authorization headers or response bodies.
        print(f"[{self.log_date_time_string()}] {self.command} {urlparse(self.path).path}", flush=True)


def create_server(exports_dir, results_dir, session_id, port=8768, hours=24, snapshot_dirs=None):
    record, _, _ = resolve_session(session_id, Path(exports_dir))
    if record.session_id != session_id:
        raise ValueError("Use an exact session ID, not a participant ID.")
    service = SingleSessionService(Path(exports_dir), Path(results_dir), record, snapshot_dirs)
    service.check_materials(record)
    server = ThreadingHTTPServer(("127.0.0.1", port), partial(InvitationHandler, directory=str(DEFAULT_WEB_DIR)))
    server.service = service
    server.invitation = Invitation(record.session_id, record.participant_id, hours)
    return server


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--session", required=True, help="Exact study session ID permitted by this invitation.")
    parser.add_argument("--port", type=int, default=8768)
    parser.add_argument("--hours", type=float, default=24)
    parser.add_argument("--exports-dir", type=Path, default=DEFAULT_EXPORTS_DIR)
    parser.add_argument("--results-dir", type=Path, default=DEFAULT_RESULTS_DIR)
    parser.add_argument("--snapshots-dir", type=Path, action="append", default=[])
    parser.add_argument("--state-file", type=Path, default=REPO_ROOT / ".utmp/delayed-share/invitation.json")
    args = parser.parse_args()
    server = create_server(args.exports_dir, args.results_dir, args.session, args.port, args.hours, args.snapshots_dir)
    state = {"port": server.server_port, "sessionId": server.invitation.session_id,
             "participantId": server.invitation.participant_id, "token": server.invitation.token,
             "expiresAtUtc": iso_utc(server.invitation.expires_at)}
    args.state_file.parent.mkdir(parents=True, exist_ok=True)
    args.state_file.write_text(json.dumps(state, indent=2), encoding="utf-8")
    print(f"Protected single-session server: 127.0.0.1:{server.server_port}", flush=True)
    print(f"Invitation expires: {state['expiresAtUtc']}", flush=True)
    print("Invitation secret saved locally; it is not printed in logs.", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
