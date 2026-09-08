#!/usr/bin/env python3
"""Small dependency-free server for the one-week delayed vocabulary test."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import random
import re
import secrets
import threading
import unicodedata
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from functools import partial
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import urlparse


APP_DIR = Path(__file__).resolve().parent
REPO_ROOT = APP_DIR.parents[1]
DEFAULT_EXPORTS_DIR = REPO_ROOT / "ExperimentExports"
DEFAULT_RESULTS_DIR = DEFAULT_EXPORTS_DIR / "DelayedTests"
DEFAULT_WEB_DIR = APP_DIR / "web"
ATTEMPT_TTL = timedelta(hours=8)
MAX_BODY_BYTES = 1_000_000


class ApiError(Exception):
    def __init__(self, status: int, message: str, code: str = "request_error") -> None:
        super().__init__(message)
        self.status = status
        self.message = message
        self.code = code


@dataclass(frozen=True)
class StudyItem:
    word: str
    meaning: str


@dataclass(frozen=True)
class SessionRecord:
    participant_id: str
    session_id: str
    created_at_utc: datetime
    word_set_name: str
    source_path: Path
    items: tuple[StudyItem, ...]


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def iso_utc(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def parse_utc(value: Any, fallback_session_id: str = "") -> datetime:
    text = str(value or "").strip()
    if text:
        try:
            return datetime.fromisoformat(text.replace("Z", "+00:00")).astimezone(timezone.utc)
        except ValueError:
            pass

    match = re.search(r"(\d{8})[_-]?(\d{6})", fallback_session_id)
    if match:
        try:
            parsed = datetime.strptime("".join(match.groups()), "%Y%m%d%H%M%S")
            return parsed.replace(tzinfo=timezone.utc)
        except ValueError:
            pass
    return datetime.fromtimestamp(0, tz=timezone.utc)


def _clean_string(value: Any) -> str:
    return str(value or "").strip()


def load_session_file(path: Path) -> SessionRecord | None:
    try:
        payload = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError, UnicodeDecodeError):
        return None

    participant_id = _clean_string(payload.get("participantId"))
    session_id = _clean_string(payload.get("sessionId"))
    raw_items = payload.get("items")
    if not participant_id or not session_id or not isinstance(raw_items, list):
        return None

    items: list[StudyItem] = []
    seen_words: set[str] = set()
    for raw in raw_items:
        if not isinstance(raw, dict):
            continue
        word = _clean_string(raw.get("word"))
        meaning = _clean_string(raw.get("meaning"))
        key = unicodedata.normalize("NFC", word).casefold()
        if not word or not meaning or key in seen_words:
            continue
        seen_words.add(key)
        items.append(StudyItem(word=word, meaning=meaning))

    if len(items) < 3:
        return None

    return SessionRecord(
        participant_id=participant_id,
        session_id=session_id,
        created_at_utc=parse_utc(payload.get("createdAtUtc"), session_id),
        word_set_name=_clean_string(payload.get("wordSetName")),
        source_path=path,
        items=tuple(items),
    )


def list_sessions(exports_dir: Path) -> list[SessionRecord]:
    records: list[SessionRecord] = []
    if not exports_dir.exists():
        return records
    for path in exports_dir.glob("session_*.json"):
        record = load_session_file(path)
        if record is not None:
            records.append(record)
    return records


def resolve_session(lookup_id: str, exports_dir: Path) -> tuple[SessionRecord, str, int]:
    query = _clean_string(lookup_id)
    if not query:
        raise ApiError(HTTPStatus.BAD_REQUEST, "Enter a Participant ID or Session ID.", "missing_id")

    records = list_sessions(exports_dir)
    query_key = query.casefold()
    session_matches = [record for record in records if record.session_id.casefold() == query_key]
    participant_matches = [record for record in records if record.participant_id.casefold() == query_key]
    matches = session_matches or participant_matches
    matched_by = "sessionId" if session_matches else "participantId"
    if not matches:
        raise ApiError(
            HTTPStatus.NOT_FOUND,
            "No completed experiment export matches that ID. Check the ID with the researcher.",
            "session_not_found",
        )

    matches.sort(
        key=lambda record: (record.created_at_utc, record.source_path.stat().st_mtime),
        reverse=True,
    )
    return matches[0], matched_by, len(matches)


_FUNCTION_WORDS = {"a", "an", "the", "to"}
_IRREGULAR_LEMMAS = {
    "children": "child",
    "feet": "foot",
    "geese": "goose",
    "men": "man",
    "mice": "mouse",
    "people": "person",
    "teeth": "tooth",
    "women": "woman",
    "went": "go",
    "gone": "go",
    "ran": "run",
    "running": "run",
    "swam": "swim",
    "swum": "swim",
    "bought": "buy",
    "brought": "bring",
    "thought": "think",
    "felt": "feel",
}


def normalize_english(value: Any) -> str:
    text = unicodedata.normalize("NFKD", _clean_string(value)).casefold()
    text = "".join(char for char in text if not unicodedata.combining(char))
    text = text.replace("’", "'").replace("&", " and ")
    return " ".join(re.findall(r"[a-z0-9]+(?:'[a-z0-9]+)?", text))


def expected_alternatives(meaning: str) -> list[str]:
    normalized = meaning.replace("（", "(").replace("）", ")")
    pieces = re.split(r"\s*(?:/|;|\||\bor\b)\s*", normalized, flags=re.IGNORECASE)
    alternatives: list[str] = []
    for piece in pieces:
        piece = piece.strip()
        if not piece:
            continue
        alternatives.append(piece)
        without_note = re.sub(r"\s*\([^)]*\)\s*", " ", piece).strip()
        if without_note and without_note != piece:
            alternatives.append(without_note)
    return alternatives or [meaning]


def _add_suffix_roots(forms: set[str], token: str) -> None:
    if len(token) > 4 and token.endswith("ies"):
        forms.add(token[:-3] + "y")
    if len(token) > 4 and token.endswith("ves"):
        forms.update({token[:-3] + "f", token[:-3] + "fe"})
    if len(token) > 4 and token.endswith("ing"):
        base = token[:-3]
        forms.update({base, base + "e"})
        if len(base) > 2 and base[-1] == base[-2]:
            forms.add(base[:-1])
    if len(token) > 3 and token.endswith("ied"):
        forms.add(token[:-3] + "y")
    if len(token) > 3 and token.endswith("ed"):
        base = token[:-2]
        forms.update({base, base + "e"})
        if len(base) > 2 and base[-1] == base[-2]:
            forms.add(base[:-1])
    if len(token) > 4 and token.endswith("es"):
        forms.update({token[:-2], token[:-1]})
    elif len(token) > 3 and token.endswith("s") and not token.endswith("ss"):
        forms.add(token[:-1])
    for suffix in ("ness", "ment", "ful", "less", "ly"):
        if len(token) > len(suffix) + 2 and token.endswith(suffix):
            base = token[: -len(suffix)]
            forms.update({base, base + "e", base + "y"})


def token_roots(token: str) -> set[str]:
    token = token.casefold()
    forms = {token, _IRREGULAR_LEMMAS.get(token, token)}
    _add_suffix_roots(forms, token)
    expanded = set(forms)
    for form in forms:
        _add_suffix_roots(expanded, form)
    return {form for form in expanded if len(form) >= 2}


def _content_tokens(value: str) -> list[str]:
    return [token for token in normalize_english(value).split() if token not in _FUNCTION_WORDS]


def answers_match(answer: str, meaning: str) -> tuple[bool, str, str]:
    answer_normalized = normalize_english(answer)
    if not answer_normalized:
        return False, "blank", ""

    for expected in expected_alternatives(meaning):
        expected_normalized = normalize_english(expected)
        if answer_normalized == expected_normalized:
            return True, "exact", expected

        answer_tokens = _content_tokens(answer)
        expected_tokens = _content_tokens(expected)
        if len(answer_tokens) != len(expected_tokens) or not answer_tokens:
            continue
        if all(token_roots(left) & token_roots(right) for left, right in zip(answer_tokens, expected_tokens)):
            return True, "morphology", expected
    return False, "no_match", ""


def _safe_filename_part(value: str) -> str:
    cleaned = re.sub(r"[^A-Za-z0-9_-]+", "_", value).strip("_")
    return cleaned[:80] or "unknown"


def _primary_meaning(meaning: str) -> str:
    return expected_alternatives(meaning)[0].strip()


class DelayedTestService:
    def __init__(self, exports_dir: Path, results_dir: Path) -> None:
        self.exports_dir = exports_dir.resolve()
        self.results_dir = results_dir.resolve()
        self._attempts: dict[str, dict[str, Any]] = {}
        self._lock = threading.Lock()

    def _discard_expired_attempts(self) -> None:
        cutoff = utc_now() - ATTEMPT_TTL
        expired = [key for key, attempt in self._attempts.items() if attempt["createdAt"] < cutoff]
        for key in expired:
            del self._attempts[key]

    def _get_attempt(self, attempt_id: str, expected_phase: str) -> dict[str, Any]:
        with self._lock:
            self._discard_expired_attempts()
            attempt = self._attempts.get(_clean_string(attempt_id))
        if attempt is None:
            raise ApiError(HTTPStatus.NOT_FOUND, "This test session expired. Enter the ID again.", "attempt_not_found")
        if attempt["phase"] != expected_phase:
            raise ApiError(HTTPStatus.CONFLICT, "This test phase was already submitted.", "wrong_phase")
        return attempt

    def _prior_attempt_count(self, session_id: str) -> int:
        if not self.results_dir.exists():
            return 0
        count = 0
        for path in self.results_dir.glob("delayed_*.json"):
            try:
                payload = json.loads(path.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                continue
            if _clean_string(payload.get("sessionId")).casefold() == session_id.casefold():
                count += 1
        return count

    def start(self, lookup_id: str) -> dict[str, Any]:
        record, matched_by, match_count = resolve_session(lookup_id, self.exports_dir)
        attempt_id = secrets.token_urlsafe(20)
        question_ids = [f"m-{index + 1}-{secrets.token_hex(4)}" for index in range(len(record.items))]
        order = list(range(len(record.items)))
        random.SystemRandom().shuffle(order)
        manual_questions = [
            {"questionId": question_ids[position], "word": record.items[item_index].word}
            for position, item_index in enumerate(order)
        ]
        prior_attempts = self._prior_attempt_count(record.session_id)
        scheduled_for = record.created_at_utc + timedelta(days=7)
        now = utc_now()
        attempt = {
            "id": attempt_id,
            "createdAt": now,
            "startedAtUtc": iso_utc(now),
            "phase": "manual",
            "record": record,
            "manualOrder": order,
            "manualQuestionIds": question_ids,
            "manualResults": [],
            "choiceQuestions": [],
            "choiceResults": [],
            "priorAttemptCount": prior_attempts,
        }
        with self._lock:
            self._discard_expired_attempts()
            self._attempts[attempt_id] = attempt

        return {
            "attemptId": attempt_id,
            "participantId": record.participant_id,
            "sessionId": record.session_id,
            "wordSetName": record.word_set_name,
            "wordCount": len(record.items),
            "studyCreatedAtUtc": iso_utc(record.created_at_utc),
            "scheduledForUtc": iso_utc(scheduled_for),
            "daysSinceStudy": round((now - record.created_at_utc).total_seconds() / 86400.0, 2),
            "matchedBy": matched_by,
            "matchingSessionCount": match_count,
            "priorDelayedAttemptCount": prior_attempts,
            "manualQuestions": manual_questions,
        }

    def submit_manual(self, attempt_id: str, responses: Any) -> dict[str, Any]:
        attempt = self._get_attempt(attempt_id, "manual")
        if not isinstance(responses, list):
            raise ApiError(HTTPStatus.BAD_REQUEST, "Manual responses must be a list.", "invalid_responses")

        record: SessionRecord = attempt["record"]
        expected_ids = set(attempt["manualQuestionIds"])
        received: dict[str, dict[str, Any]] = {}
        for raw in responses:
            if not isinstance(raw, dict):
                continue
            question_id = _clean_string(raw.get("questionId"))
            if question_id in expected_ids and question_id not in received:
                received[question_id] = raw
        if set(received) != expected_ids:
            raise ApiError(HTTPStatus.BAD_REQUEST, "Submit one response for every Round 1 question.", "incomplete_manual")

        manual_results: list[dict[str, Any]] = []
        for position, item_index in enumerate(attempt["manualOrder"]):
            item = record.items[item_index]
            question_id = attempt["manualQuestionIds"][position]
            raw = received[question_id]
            answer = _clean_string(raw.get("answer"))[:300]
            correct, match_mode, matched_expected = answers_match(answer, item.meaning)
            manual_results.append(
                {
                    "questionId": question_id,
                    "word": item.word,
                    "expectedMeaning": item.meaning,
                    "answer": answer,
                    "normalizedAnswer": normalize_english(answer),
                    "isCorrect": correct,
                    "matchMode": match_mode,
                    "matchedExpectedVariant": matched_expected,
                    "responseTimeMs": _safe_time_ms(raw.get("responseTimeMs")),
                }
            )

        rng_seed = hashlib.sha256((attempt_id + record.session_id).encode("utf-8")).digest()
        rng = random.Random(rng_seed)
        choice_order = list(range(len(record.items)))
        rng.shuffle(choice_order)
        choice_questions: list[dict[str, Any]] = []
        private_questions: list[dict[str, Any]] = []
        for item_index in choice_order:
            item = record.items[item_index]
            correct_label = _primary_meaning(item.meaning)
            distractor_labels: list[str] = []
            candidate_indexes = [index for index in range(len(record.items)) if index != item_index]
            rng.shuffle(candidate_indexes)
            for other_index in candidate_indexes:
                label = _primary_meaning(record.items[other_index].meaning)
                if normalize_english(label) != normalize_english(correct_label) and all(
                    normalize_english(label) != normalize_english(existing) for existing in distractor_labels
                ):
                    distractor_labels.append(label)
                if len(distractor_labels) == 2:
                    break
            if len(distractor_labels) < 2:
                raise ApiError(
                    HTTPStatus.UNPROCESSABLE_ENTITY,
                    "The participant session does not contain three distinct English meanings.",
                    "insufficient_distractors",
                )

            labels = [correct_label, *distractor_labels]
            rng.shuffle(labels)
            question_id = f"c-{len(choice_questions) + 1}-{secrets.token_hex(4)}"
            options = [
                {"optionId": f"{question_id}-o{index + 1}", "label": label}
                for index, label in enumerate(labels)
            ]
            correct_option_id = next(
                option["optionId"] for option in options if normalize_english(option["label"]) == normalize_english(correct_label)
            )
            choice_questions.append({"questionId": question_id, "word": item.word, "options": options})
            private_questions.append(
                {
                    "questionId": question_id,
                    "word": item.word,
                    "expectedMeaning": item.meaning,
                    "options": options,
                    "correctOptionId": correct_option_id,
                }
            )

        attempt["manualResults"] = manual_results
        attempt["manualSubmittedAtUtc"] = iso_utc(utc_now())
        attempt["choiceQuestions"] = private_questions
        attempt["phase"] = "choice"
        return {"choiceQuestions": choice_questions, "wordCount": len(choice_questions)}

    def submit_choice(self, attempt_id: str, responses: Any) -> dict[str, Any]:
        attempt = self._get_attempt(attempt_id, "choice")
        if not isinstance(responses, list):
            raise ApiError(HTTPStatus.BAD_REQUEST, "Choice responses must be a list.", "invalid_responses")

        private_questions = {question["questionId"]: question for question in attempt["choiceQuestions"]}
        received: dict[str, dict[str, Any]] = {}
        for raw in responses:
            if not isinstance(raw, dict):
                continue
            question_id = _clean_string(raw.get("questionId"))
            if question_id in private_questions and question_id not in received:
                received[question_id] = raw
        if set(received) != set(private_questions):
            raise ApiError(HTTPStatus.BAD_REQUEST, "Choose one answer for every Round 2 question.", "incomplete_choice")

        choice_results: list[dict[str, Any]] = []
        for question in attempt["choiceQuestions"]:
            raw = received[question["questionId"]]
            chosen_option_id = _clean_string(raw.get("optionId"))
            option_by_id = {option["optionId"]: option for option in question["options"]}
            if chosen_option_id not in option_by_id:
                raise ApiError(HTTPStatus.BAD_REQUEST, "A selected option is not valid.", "invalid_option")
            choice_results.append(
                {
                    "questionId": question["questionId"],
                    "word": question["word"],
                    "expectedMeaning": question["expectedMeaning"],
                    "options": [option["label"] for option in question["options"]],
                    "chosenOptionId": chosen_option_id,
                    "chosenMeaning": option_by_id[chosen_option_id]["label"],
                    "isCorrect": chosen_option_id == question["correctOptionId"],
                    "responseTimeMs": _safe_time_ms(raw.get("responseTimeMs")),
                }
            )

        attempt["choiceResults"] = choice_results
        attempt["choiceSubmittedAtUtc"] = iso_utc(utc_now())
        attempt["phase"] = "complete"
        result_id = self._save_result(attempt)
        manual_score = sum(1 for result in attempt["manualResults"] if result["isCorrect"])
        choice_score = sum(1 for result in choice_results if result["isCorrect"])
        summary = {
            "resultId": result_id,
            "participantId": attempt["record"].participant_id,
            "sessionId": attempt["record"].session_id,
            "wordCount": len(attempt["record"].items),
            "manualScore": manual_score,
            "choiceScore": choice_score,
            "completedAtUtc": attempt["completedAtUtc"],
        }
        attempt["summary"] = summary
        return summary

    def _save_result(self, attempt: dict[str, Any]) -> str:
        record: SessionRecord = attempt["record"]
        completed_at = utc_now()
        attempt_number = attempt["priorAttemptCount"] + 1
        manual_score = sum(1 for result in attempt["manualResults"] if result["isCorrect"])
        choice_score = sum(1 for result in attempt["choiceResults"] if result["isCorrect"])
        payload = {
            "schemaVersion": 1,
            "testType": "one_week_delayed_vocabulary",
            "participantId": record.participant_id,
            "sessionId": record.session_id,
            "sourceExperimentExport": record.source_path.name,
            "studyCreatedAtUtc": iso_utc(record.created_at_utc),
            "scheduledForUtc": iso_utc(record.created_at_utc + timedelta(days=7)),
            "startedAtUtc": attempt["startedAtUtc"],
            "manualSubmittedAtUtc": attempt["manualSubmittedAtUtc"],
            "choiceSubmittedAtUtc": attempt["choiceSubmittedAtUtc"],
            "completedAtUtc": iso_utc(completed_at),
            "daysSinceStudy": round((completed_at - record.created_at_utc).total_seconds() / 86400.0, 4),
            "attemptNumber": attempt_number,
            "wordCount": len(record.items),
            "manualScore": manual_score,
            "manualTotal": len(attempt["manualResults"]),
            "choiceScore": choice_score,
            "choiceTotal": len(attempt["choiceResults"]),
            "manualResponses": attempt["manualResults"],
            "choiceResponses": attempt["choiceResults"],
        }
        self.results_dir.mkdir(parents=True, exist_ok=True)
        timestamp = completed_at.strftime("%Y%m%d_%H%M%S_%f")
        stem = (
            f"delayed_{_safe_filename_part(record.participant_id)}_"
            f"{_safe_filename_part(record.session_id)}_{timestamp}"
        )
        json_path = self.results_dir / f"{stem}.json"
        temp_path = self.results_dir / f".{stem}.tmp"
        temp_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        temp_path.replace(json_path)
        self._write_csv(self.results_dir / f"{stem}.csv", payload)
        attempt["completedAtUtc"] = payload["completedAtUtc"]
        return stem

    @staticmethod
    def _write_csv(path: Path, payload: dict[str, Any]) -> None:
        manual_by_word = {result["word"]: result for result in payload["manualResponses"]}
        choice_by_word = {result["word"]: result for result in payload["choiceResponses"]}
        with path.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.writer(stream)
            writer.writerow(
                [
                    "participant_id",
                    "session_id",
                    "attempt_number",
                    "completed_at_utc",
                    "target_word",
                    "expected_meaning",
                    "manual_answer",
                    "manual_match_mode",
                    "manual_response_time_ms",
                    "manual_correct",
                    "choice_options",
                    "choice_answer",
                    "choice_response_time_ms",
                    "choice_correct",
                ]
            )
            for word, manual in manual_by_word.items():
                choice = choice_by_word[word]
                writer.writerow(
                    [
                        payload["participantId"],
                        payload["sessionId"],
                        payload["attemptNumber"],
                        payload["completedAtUtc"],
                        word,
                        manual["expectedMeaning"],
                        manual["answer"],
                        manual["matchMode"],
                        manual["responseTimeMs"],
                        manual["isCorrect"],
                        " | ".join(choice["options"]),
                        choice["chosenMeaning"],
                        choice["responseTimeMs"],
                        choice["isCorrect"],
                    ]
                )


def _safe_time_ms(value: Any) -> int:
    try:
        return max(0, min(int(float(value)), 3_600_000))
    except (TypeError, ValueError, OverflowError):
        return 0


class DelayedTestRequestHandler(SimpleHTTPRequestHandler):
    server_version = "MemoryPalaceDelayedTest/1.0"

    def end_headers(self) -> None:
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'")
        super().end_headers()

    @property
    def service(self) -> DelayedTestService:
        return self.server.service  # type: ignore[attr-defined]

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/api/health":
            self._send_json(HTTPStatus.OK, {"ok": True, "service": "one-week-delayed-test"})
            return
        if parsed.path.startswith("/api/"):
            self._send_error(ApiError(HTTPStatus.NOT_FOUND, "Unknown API route.", "route_not_found"))
            return
        if parsed.path == "/":
            self.path = "/index.html"
        super().do_GET()

    def do_POST(self) -> None:
        try:
            payload = self._read_json()
            parsed = urlparse(self.path)
            if parsed.path == "/api/start":
                result = self.service.start(payload.get("lookupId", ""))
            elif parsed.path == "/api/manual":
                result = self.service.submit_manual(payload.get("attemptId", ""), payload.get("responses"))
            elif parsed.path == "/api/choice":
                result = self.service.submit_choice(payload.get("attemptId", ""), payload.get("responses"))
            else:
                raise ApiError(HTTPStatus.NOT_FOUND, "Unknown API route.", "route_not_found")
            self._send_json(HTTPStatus.OK, result)
        except ApiError as error:
            self._send_error(error)
        except Exception as error:  # pragma: no cover - last-resort HTTP boundary
            print(f"Unexpected request failure: {error}")
            self._send_error(ApiError(HTTPStatus.INTERNAL_SERVER_ERROR, "The server could not complete the request.", "server_error"))

    def _read_json(self) -> dict[str, Any]:
        try:
            content_length = int(self.headers.get("Content-Length", "0"))
        except ValueError as error:
            raise ApiError(HTTPStatus.BAD_REQUEST, "Invalid request length.") from error
        if content_length <= 0 or content_length > MAX_BODY_BYTES:
            raise ApiError(HTTPStatus.BAD_REQUEST, "Invalid request body size.")
        try:
            payload = json.loads(self.rfile.read(content_length).decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ApiError(HTTPStatus.BAD_REQUEST, "Request body must be valid JSON.") from error
        if not isinstance(payload, dict):
            raise ApiError(HTTPStatus.BAD_REQUEST, "Request body must be a JSON object.")
        return payload

    def _send_error(self, error: ApiError) -> None:
        self._send_json(error.status, {"error": error.code, "message": error.message})

    def _send_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format_string: str, *args: Any) -> None:
        print(f"[{self.log_date_time_string()}] {format_string % args}")


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Serve the one-week delayed vocabulary test.")
    parser.add_argument("--host", default="127.0.0.1", help="Bind address (use 0.0.0.0 for LAN access).")
    parser.add_argument("--port", type=int, default=8765, help="HTTP port (default: 8765).")
    parser.add_argument("--exports-dir", type=Path, default=DEFAULT_EXPORTS_DIR, help="Unity ExperimentExports directory.")
    parser.add_argument("--results-dir", type=Path, default=DEFAULT_RESULTS_DIR, help="Directory for delayed-test JSON/CSV results.")
    parser.add_argument("--web-dir", type=Path, default=DEFAULT_WEB_DIR, help="Static web asset directory.")
    return parser


def main() -> None:
    args = build_argument_parser().parse_args()
    exports_dir = args.exports_dir.resolve()
    results_dir = args.results_dir.resolve()
    web_dir = args.web_dir.resolve()
    if not web_dir.joinpath("index.html").is_file():
        raise SystemExit(f"Web assets were not found: {web_dir}")

    service = DelayedTestService(exports_dir, results_dir)
    handler = partial(DelayedTestRequestHandler, directory=str(web_dir))
    server = ThreadingHTTPServer((args.host, args.port), handler)
    server.service = service  # type: ignore[attr-defined]
    print(f"One-week delayed test: http://{args.host}:{args.port}")
    print(f"Experiment exports: {exports_dir}")
    print(f"Delayed-test results: {results_dir}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopping delayed-test server.")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
