#!/usr/bin/env python3
"""Small dependency-free server for the one-week delayed vocabulary test."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
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
from urllib.parse import parse_qs, urlparse


APP_DIR = Path(__file__).resolve().parent
REPO_ROOT = APP_DIR.parents[1]
DEFAULT_EXPORTS_DIR = REPO_ROOT / "ExperimentExports"
DEFAULT_RESULTS_DIR = DEFAULT_EXPORTS_DIR / "DelayedTests"
DEFAULT_WEB_DIR = APP_DIR / "web"
ATTEMPT_TTL = timedelta(hours=8)
MAX_BODY_BYTES = 1_000_000
PROTOCOL_VERSION = "delayed-story-spatial-typed-retest-v3"
MAX_SNAPSHOT_BYTES = 20_000_000


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
    anchor_id: str = ""
    anchor_label: str = ""
    snapshot_ref: str = ""


@dataclass(frozen=True)
class SessionRecord:
    participant_id: str
    session_id: str
    created_at_utc: datetime
    word_set_name: str
    source_path: Path
    items: tuple[StudyItem, ...]
    full_story: str = ""


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
        items.append(StudyItem(
            word=word, meaning=meaning,
            anchor_id=_clean_string(raw.get("anchorId")),
            anchor_label=_clean_string(raw.get("anchorLabel")),
            snapshot_ref=_clean_string(raw.get("sceneSnapshotPath")),
        ))

    if len(items) < 3:
        return None

    story = payload.get("storySession")
    raw_story = story.get("fullStory") if isinstance(story, dict) else None
    full_story = raw_story if isinstance(raw_story, str) else ""
    return SessionRecord(
        participant_id=participant_id,
        session_id=session_id,
        created_at_utc=parse_utc(payload.get("createdAtUtc"), session_id),
        word_set_name=_clean_string(payload.get("wordSetName")),
        source_path=path,
        items=tuple(items),
        full_story=full_story,
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


class DelayedTestService:
    """Server-gated baseline -> story -> spatial matching -> vocabulary retest."""

    def __init__(self, exports_dir: Path, results_dir: Path, snapshot_dirs: list[Path] | None = None) -> None:
        self.exports_dir = exports_dir.resolve()
        self.results_dir = results_dir.resolve()
        roots = [self.exports_dir, self.exports_dir.parent, REPO_ROOT / "QuestResultImports"]
        if os.name == "nt":
            settings = REPO_ROOT / "ProjectSettings" / "ProjectSettings.asset"
            text = settings.read_text(encoding="utf-8-sig") if settings.exists() else ""
            company = re.search(r"^  companyName: (.+)$", text, re.MULTILINE)
            product = re.search(r"^  productName: (.+)$", text, re.MULTILINE)
            if company and product:
                roots.append(Path.home() / "AppData/LocalLow" / company[1].strip() /
                             product[1].strip() / "MemoryPalaceSessionData")
        self.snapshot_roots = tuple(dict.fromkeys(path.resolve() for path in [*roots, *(snapshot_dirs or [])]))
        self._attempts: dict[str, dict[str, Any]] = {}
        self._lock = threading.RLock()

    def _get_attempt(self, attempt_id: str, expected_phase: str | None = None) -> dict[str, Any]:
        cutoff = utc_now() - ATTEMPT_TTL
        for key in list(self._attempts):
            if self._attempts[key]["createdAt"] < cutoff:
                del self._attempts[key]
        attempt = self._attempts.get(_clean_string(attempt_id))
        if attempt is None:
            raise ApiError(HTTPStatus.NOT_FOUND, "This test session expired. Enter the ID again.", "attempt_not_found")
        if expected_phase is not None and attempt["phase"] != expected_phase:
            raise ApiError(HTTPStatus.CONFLICT, "This stage is not available. Continue in the displayed order.", "wrong_phase")
        return attempt

    def _prior_attempt_count(self, session_id: str) -> int:
        count = 0
        for path in self.results_dir.glob("delayed_*.json") if self.results_dir.exists() else []:
            try:
                payload = json.loads(path.read_text(encoding="utf-8"))
                count += int(_clean_string(payload.get("sessionId")).casefold() == session_id.casefold())
            except (OSError, ValueError, TypeError):
                continue
        return count

    def _valid_snapshot(self, path: Path, record: SessionRecord) -> bool:
        try:
            resolved = path.resolve()
            if not any(resolved.is_relative_to(root) for root in self.snapshot_roots):
                return False
            session_folder = resolved.parent.parent.name
            if (resolved.parent.name != "SpatialSnapshots"
                    or not (session_folder == record.session_id or session_folder.endswith("_" + record.session_id))):
                return False
            if not resolved.is_file() or not 8 <= resolved.stat().st_size <= MAX_SNAPSHOT_BYTES:
                return False
            with resolved.open("rb") as stream:
                header = stream.read(8)
            return header == b"\x89PNG\r\n\x1a\n" or header.startswith(b"\xff\xd8\xff")
        except (OSError, ValueError):
            return False

    def _resolve_snapshot(self, record: SessionRecord, item: StudyItem) -> Path | None:
        ref = item.snapshot_ref.replace("\\", "/")
        if not ref or ref.startswith("//") or ".." in ref.split("/"):
            return None
        if not re.fullmatch(r"[A-Za-z0-9_.-]{1,100}", record.session_id) or ".." in record.session_id:
            return None
        name = Path(ref).name
        if Path(name).suffix.lower() not in {".png", ".jpg", ".jpeg"}:
            return None
        direct = Path(item.snapshot_ref)
        candidates = [direct] if direct.is_absolute() else [record.source_path.parent / direct]
        for root in self.snapshot_roots:
            if root.name == "SpatialSnapshots":
                candidates.append(root / name)
            candidates.append(root / "SpatialSnapshots" / name)
            bases = [root, root / "MemoryPalaceSessionData"]
            if root.name == "QuestResultImports" and root.exists():
                bases.extend(root.glob("*/MemoryPalaceSessionData"))
            for base in bases:
                if base.is_dir():
                    candidates.extend(base.glob(f"*{record.session_id}*/SpatialSnapshots/{name}"))
        for candidate in candidates:
            if self._valid_snapshot(candidate, record):
                return candidate.resolve()
        return None

    def check_materials(self, record: SessionRecord) -> tuple[Path, ...]:
        if not record.full_story.strip():
            raise ApiError(HTTPStatus.UNPROCESSABLE_ENTITY,
                           "The original study story is missing. Ask the researcher to restore the complete export.",
                           "missing_story")
        anchors = [item.anchor_id for item in record.items]
        if any(not item.anchor_id or not item.anchor_label for item in record.items) or len(set(anchors)) != len(anchors):
            raise ApiError(HTTPStatus.UNPROCESSABLE_ENTITY,
                           "Original, unique furniture assignments are missing from this export.", "missing_furniture")
        paths = tuple(self._resolve_snapshot(record, item) for item in record.items)
        if any(path is None for path in paths):
            missing = sum(path is None for path in paths)
            raise ApiError(HTTPStatus.UNPROCESSABLE_ENTITY,
                           f"{missing} original furniture snapshot(s) could not be loaded. Ask the researcher to restore "
                           "the session's SpatialSnapshots folder or configure --snapshots-dir.", "missing_snapshots")
        return paths

    @staticmethod
    def _new_manual(attempt: dict[str, Any], stage: str) -> list[dict[str, str]]:
        order = list(range(len(attempt["record"].items)))
        random.SystemRandom().shuffle(order)
        questions = [{"questionId": f"{stage}-m-{secrets.token_hex(8)}",
                      "word": attempt["record"].items[index].word} for index in order]
        attempt[stage] = {"manualOrder": order, "manualQuestions": questions, "manualResponses": []}
        return questions

    def start(self, lookup_id: str) -> dict[str, Any]:
        with self._lock:
            record, matched_by, match_count = resolve_session(lookup_id, self.exports_dir)
            snapshots = self.check_materials(record)
            now = utc_now()
            attempt_id = secrets.token_urlsafe(24)
            attempt = {
                "id": attempt_id, "record": record, "snapshots": snapshots, "createdAt": now,
                "startedAtUtc": iso_utc(now), "phase": "baseline_manual", "cache": {},
                "priorAttemptCount": self._prior_attempt_count(record.session_id),
                "matchingResponses": [], "completedAtUtc": None,
            }
            questions = self._new_manual(attempt, "baseline")
            self._attempts[attempt_id] = attempt
            return {
                "protocolVersion": PROTOCOL_VERSION, "attemptId": attempt_id,
                "participantId": record.participant_id, "sessionId": record.session_id,
                "wordSetName": record.word_set_name, "wordCount": len(record.items),
                "studyCreatedAtUtc": iso_utc(record.created_at_utc),
                "scheduledForUtc": iso_utc(record.created_at_utc + timedelta(days=7)),
                "daysSinceStudy": round((now - record.created_at_utc).total_seconds() / 86400.0, 2),
                "matchedBy": matched_by, "matchingSessionCount": match_count,
                "priorDelayedAttemptCount": attempt["priorAttemptCount"],
                "manualQuestions": questions,
            }

    @staticmethod
    def _received(responses: Any, questions: list[dict[str, Any]]) -> dict[str, dict[str, Any]]:
        if not isinstance(responses, list):
            raise ApiError(HTTPStatus.BAD_REQUEST, "Responses must be a list.", "invalid_responses")
        expected = {question["questionId"] for question in questions}
        received: dict[str, dict[str, Any]] = {}
        for raw in responses:
            if (not isinstance(raw, dict) or not isinstance(raw.get("questionId"), str)
                    or raw["questionId"] not in expected or raw["questionId"] in received):
                raise ApiError(HTTPStatus.BAD_REQUEST, "Invalid or duplicated question.", "invalid_question")
            received[raw["questionId"]] = raw
        if set(received) != expected:
            raise ApiError(HTTPStatus.BAD_REQUEST, "Answer every question before continuing.", "incomplete_responses")
        return received

    @staticmethod
    def _cached(attempt: dict[str, Any], key: str, responses: Any) -> tuple[Any, str]:
        fingerprint = hashlib.sha256(json.dumps(responses, sort_keys=True, ensure_ascii=False).encode()).hexdigest()
        cached = attempt["cache"].get(key)
        if cached is not None:
            if fingerprint != cached[0]:
                raise ApiError(HTTPStatus.CONFLICT, "This stage was already saved and cannot be changed.", "already_saved")
            return cached[1], fingerprint
        return None, fingerprint

    def submit_manual(self, attempt_id: str, responses: Any, stage: str = "baseline") -> dict[str, Any]:
        if not isinstance(stage, str) or stage not in {"baseline", "post"}:
            raise ApiError(HTTPStatus.BAD_REQUEST, "Invalid vocabulary stage.", "invalid_stage")
        with self._lock:
            attempt = self._get_attempt(attempt_id)
            key = stage + "_manual"
            cached, fingerprint = self._cached(attempt, key, responses)
            if cached is not None:
                return cached
            self._get_attempt(attempt_id, key)
            block = attempt[stage]
            received = self._received(responses, block["manualQuestions"])
            results = []
            for question, index in zip(block["manualQuestions"], block["manualOrder"]):
                item = attempt["record"].items[index]
                raw = received[question["questionId"]]
                answer = _clean_string(raw.get("answer"))[:300]
                correct, mode, variant = answers_match(answer, item.meaning)
                results.append({"questionId": question["questionId"], "word": item.word,
                                "expectedMeaning": item.meaning, "answer": answer,
                                "normalizedAnswer": normalize_english(answer), "isCorrect": correct,
                                "matchMode": mode, "matchedExpectedVariant": variant,
                                "responseTimeMs": _safe_time_ms(raw.get("responseTimeMs"))})
            block.update(manualResponses=results, manualSubmittedAtUtc=iso_utc(utc_now()))
            if stage == "baseline":
                attempt["phase"] = "story"
                attempt["storyShownAtUtc"] = iso_utc(utc_now())
                response = {"nextPhase": "story", "baselineSaved": True,
                            "storyText": attempt["record"].full_story, "wordCount": len(results)}
            else:
                attempt["phase"] = "complete"
                attempt["completedAtUtc"] = iso_utc(utc_now())
                response = self._summary(attempt)
            try:
                self._save_result(attempt)
            except OSError:
                attempt["phase"] = key
                attempt["completedAtUtc"] = None
                raise
            response["resultId"] = attempt["resultId"]
            attempt["cache"][key] = (fingerprint, response)
            return response

    @staticmethod
    def _score_matching(responses: Any, questions: list[dict[str, Any]]) -> list[dict[str, Any]]:
        received = DelayedTestService._received(responses, questions)
        results = []
        for question in questions:
            raw = received[question["questionId"]]
            option_id = _clean_string(raw.get("optionId"))
            chosen = next((option for option in question["options"] if option["optionId"] == option_id), None)
            if chosen is None:
                raise ApiError(HTTPStatus.BAD_REQUEST, "A selected option is invalid.", "invalid_option")
            result = {"questionId": question["questionId"], "word": question["word"],
                      "options": [option["label"] for option in question["options"]],
                      "chosenOptionId": option_id, "isCorrect": option_id == question["correctOptionId"],
                      "responseTimeMs": _safe_time_ms(raw.get("responseTimeMs"))}
            result.update(anchorId=question["anchorId"], anchorLabel=question["anchorLabel"],
                          chosenWord=chosen["label"], presentation="original_study_snapshot")
            results.append(result)
        return results

    def submit_story(self, attempt_id: str) -> dict[str, Any]:
        with self._lock:
            attempt = self._get_attempt(attempt_id)
            cached, fingerprint = self._cached(attempt, "story", {})
            if cached is not None:
                return cached
            self._get_attempt(attempt_id, "story")
            rng = random.SystemRandom()
            record = attempt["record"]
            indexes = list(range(len(record.items)))
            rng.shuffle(indexes)
            questions = []
            for index in indexes:
                item = record.items[index]
                others = [other.word for i, other in enumerate(record.items) if i != index]
                rng.shuffle(others)
                labels = [item.word, *others[:2]]
                rng.shuffle(labels)
                options = [{"optionId": secrets.token_hex(12), "label": label} for label in labels]
                questions.append({"questionId": "s-" + secrets.token_hex(12), "word": item.word,
                                  "anchorId": item.anchor_id, "anchorLabel": item.anchor_label,
                                  "snapshotIndex": index, "options": options,
                                  "correctOptionId": next(option["optionId"] for option in options if option["label"] == item.word)})
            attempt["matchingQuestions"] = questions
            attempt["storyAcknowledgedAtUtc"] = iso_utc(utc_now())
            attempt["storyReviewDurationMs"] = max(0, round(
                (utc_now() - parse_utc(attempt["storyShownAtUtc"])).total_seconds() * 1000))
            attempt["phase"] = "matching"
            public = [{"questionId": q["questionId"], "furnitureLabel": q["anchorLabel"], "options": q["options"],
                       "snapshotUrl": f'/api/snapshot?attemptId={attempt_id}&questionId={q["questionId"]}'}
                      for q in questions]
            response = {"nextPhase": "matching", "matchingQuestions": public, "wordCount": len(questions)}
            try:
                self._save_result(attempt)
            except OSError:
                attempt["phase"] = "story"
                raise
            attempt["cache"]["story"] = (fingerprint, response)
            return response

    def snapshot(self, attempt_id: str, question_id: str) -> tuple[bytes, str]:
        with self._lock:
            attempt = self._get_attempt(attempt_id)
            if attempt["phase"] not in {"matching", "post_manual", "complete"}:
                raise ApiError(HTTPStatus.FORBIDDEN, "Snapshots are not available before story review.", "wrong_phase")
            question = next((q for q in attempt["matchingQuestions"] if q["questionId"] == question_id), None)
            if question is None:
                raise ApiError(HTTPStatus.NOT_FOUND, "Snapshot not found.", "snapshot_not_found")
            path = attempt["snapshots"][question["snapshotIndex"]]
            if not self._valid_snapshot(path, attempt["record"]):
                raise ApiError(HTTPStatus.NOT_FOUND, "The original snapshot is no longer available.", "snapshot_not_found")
            try:
                data = path.read_bytes()
            except OSError as error:
                raise ApiError(HTTPStatus.NOT_FOUND, "The original snapshot could not be read.", "snapshot_not_found") from error
            return data, "image/png" if data.startswith(b"\x89PNG") else "image/jpeg"

    def submit_matching(self, attempt_id: str, responses: Any) -> dict[str, Any]:
        with self._lock:
            attempt = self._get_attempt(attempt_id)
            cached, fingerprint = self._cached(attempt, "matching", responses)
            if cached is not None:
                return cached
            self._get_attempt(attempt_id, "matching")
            results = self._score_matching(responses, attempt["matchingQuestions"])
            attempt["matchingResponses"] = results
            attempt["matchingSubmittedAtUtc"] = iso_utc(utc_now())
            questions = self._new_manual(attempt, "post")
            attempt["postStartedAtUtc"] = iso_utc(utc_now())
            attempt["phase"] = "post_manual"
            response = {"nextPhase": "post_manual", "manualQuestions": questions, "wordCount": len(questions)}
            try:
                self._save_result(attempt)
            except OSError:
                attempt["phase"] = "matching"
                raise
            attempt["cache"]["matching"] = (fingerprint, response)
            return response

    @staticmethod
    def _score(results: list[dict[str, Any]]) -> int:
        return sum(bool(result["isCorrect"]) for result in results)

    def _summary(self, attempt: dict[str, Any]) -> dict[str, Any]:
        baseline = attempt["baseline"]
        post = attempt.get("post", {})
        return {
            "nextPhase": "complete", "participantId": attempt["record"].participant_id,
            "sessionId": attempt["record"].session_id, "wordCount": len(attempt["record"].items),
            "baselineManualScore": self._score(baseline["manualResponses"]),
            "matchingScore": self._score(attempt["matchingResponses"]),
            "postManualScore": self._score(post.get("manualResponses", [])),
            "completedAtUtc": attempt["completedAtUtc"],
        }

    def _save_result(self, attempt: dict[str, Any]) -> None:
        record = attempt["record"]
        baseline = attempt["baseline"]
        summary = self._summary(attempt)
        if "resultId" not in attempt:
            stamp = attempt["createdAt"].strftime("%Y%m%d_%H%M%S_%f")
            attempt["resultId"] = f"delayed_{_safe_filename_part(record.participant_id)}_{_safe_filename_part(record.session_id)}_{stamp}_{attempt['id'][:8]}"
        payload = {
            "schemaVersion": 3, "protocolVersion": PROTOCOL_VERSION, "testType": "one_week_delayed_vocabulary",
            "vocabularyTestFormat": "typed_only", "choiceTestAdministered": False,
            "status": "complete" if attempt["phase"] == "complete" else "in_progress",
            "phase": attempt["phase"], "participantId": record.participant_id, "sessionId": record.session_id,
            "sourceExperimentExport": record.source_path.name, "studyCreatedAtUtc": iso_utc(record.created_at_utc),
            "scheduledForUtc": iso_utc(record.created_at_utc + timedelta(days=7)),
            "startedAtUtc": attempt["startedAtUtc"], "completedAtUtc": attempt["completedAtUtc"],
            "daysSinceStudy": round((utc_now() - record.created_at_utc).total_seconds() / 86400.0, 4),
            "attemptNumber": attempt["priorAttemptCount"] + 1, "wordCount": len(record.items),
            "stageOrder": ["baseline_manual", "story", "matching", "post_manual"],
            "baseline": baseline, "spatialMatching": {"responses": attempt["matchingResponses"],
                                                    "submittedAtUtc": attempt.get("matchingSubmittedAtUtc")},
            "postReview": attempt.get("post", {}),
            "storyReview": {"text": record.full_story, "sha256": hashlib.sha256(record.full_story.encode("utf-8")).hexdigest(),
                            "shownAtUtc": attempt.get("storyShownAtUtc"),
                            "acknowledgedAtUtc": attempt.get("storyAcknowledgedAtUtc"),
                            "durationMs": attempt.get("storyReviewDurationMs")},
            "postStartedAtUtc": attempt.get("postStartedAtUtc"),
            # Legacy typed fields refer to baseline; recognition was not administered.
            "manualSubmittedAtUtc": baseline.get("manualSubmittedAtUtc"),
            "choiceSubmittedAtUtc": None,
            "manualResponses": baseline["manualResponses"], "choiceResponses": [],
            "manualScore": summary["baselineManualScore"], "choiceScore": None,
            "manualTotal": len(baseline["manualResponses"]), "choiceTotal": 0,
            **{key: summary[key] for key in ("baselineManualScore", "matchingScore", "postManualScore")},
        }
        self.results_dir.mkdir(parents=True, exist_ok=True)
        stem = attempt["resultId"]
        json_path = self.results_dir / f"{stem}.json"
        csv_path = self.results_dir / f"{stem}.csv"
        json_temp = self.results_dir / f".{stem}.json.tmp"
        csv_temp = self.results_dir / f".{stem}.csv.tmp"
        json_temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        self._write_csv(csv_temp, payload)
        csv_temp.replace(csv_path)
        json_temp.replace(json_path)

    @staticmethod
    def _write_csv(path: Path, payload: dict[str, Any]) -> None:
        baseline = payload["baseline"]
        post = payload["postReview"]
        maps = [{response["word"]: response for response in responses} for responses in
                (baseline["manualResponses"], payload["spatialMatching"]["responses"], post.get("manualResponses", []))]
        columns = ["participant_id", "session_id", "attempt_number", "completed_at_utc", "target_word", "expected_meaning",
                   "manual_answer", "manual_match_mode", "manual_response_time_ms", "manual_correct",
                   "choice_options", "choice_answer", "choice_response_time_ms", "choice_correct",
                   "protocol_version", "status", "anchor_id", "anchor_label", "matching_options", "matching_answer",
                   "matching_response_time_ms", "matching_correct", "post_manual_answer", "post_manual_match_mode",
                   "post_manual_response_time_ms", "post_manual_correct", "post_choice_options", "post_choice_answer",
                   "post_choice_response_time_ms", "post_choice_correct", "story_review_duration_ms",
                   "vocabulary_test_format", "choice_test_administered"]
        with path.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=columns)
            writer.writeheader()
            for word, manual in maps[0].items():
                spatial, post_manual = [mapping.get(word, {}) for mapping in maps[1:]]
                writer.writerow(dict(zip(columns, [
                    payload["participantId"], payload["sessionId"], payload["attemptNumber"], payload["completedAtUtc"],
                    word, manual["expectedMeaning"], manual["answer"], manual["matchMode"], manual["responseTimeMs"], manual["isCorrect"],
                    "", "", "", "",
                    PROTOCOL_VERSION, payload["status"], spatial.get("anchorId", ""), spatial.get("anchorLabel", ""),
                    " | ".join(spatial.get("options", [])), spatial.get("chosenWord", ""), spatial.get("responseTimeMs", ""), spatial.get("isCorrect", ""),
                    post_manual.get("answer", ""), post_manual.get("matchMode", ""), post_manual.get("responseTimeMs", ""), post_manual.get("isCorrect", ""),
                    "", "", "", "",
                    payload["storyReview"]["durationMs"], "typed_only", False,
                ])))


def _safe_time_ms(value: Any) -> int:
    try:
        return max(0, min(int(float(value)), 3_600_000))
    except (TypeError, ValueError, OverflowError):
        return 0


class DelayedTestRequestHandler(SimpleHTTPRequestHandler):
    server_version = "MemoryPalaceDelayedTest/3.0"

    def end_headers(self) -> None:
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'")
        super().end_headers()

    @property
    def service(self) -> DelayedTestService:
        return self.server.service  # type: ignore[attr-defined]

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path == "/api/snapshot":
            try:
                query = parse_qs(parsed.query)
                data, content_type = self.service.snapshot(query.get("attemptId", [""])[0], query.get("questionId", [""])[0])
                self.send_response(HTTPStatus.OK)
                self.send_header("Content-Type", content_type)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)
            except ApiError as error:
                self._send_error(error)
            return
        if parsed.path == "/api/health":
            self._send_json(HTTPStatus.OK, {"ok": True, "service": "one-week-delayed-test", "protocolVersion": PROTOCOL_VERSION})
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
                result = self.service.submit_manual(payload.get("attemptId", ""), payload.get("responses"), payload.get("stage", "baseline"))
            elif parsed.path == "/api/story":
                result = self.service.submit_story(payload.get("attemptId", ""))
            elif parsed.path == "/api/matching":
                result = self.service.submit_matching(payload.get("attemptId", ""), payload.get("responses"))
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
    parser.add_argument("--snapshots-dir", type=Path, action="append", default=[],
                        help="Additional trusted snapshot root (MemoryPalaceSessionData or QuestResultImports). Repeatable.")
    return parser


def main() -> None:
    args = build_argument_parser().parse_args()
    exports_dir = args.exports_dir.resolve()
    results_dir = args.results_dir.resolve()
    web_dir = args.web_dir.resolve()
    if not web_dir.joinpath("index.html").is_file():
        raise SystemExit(f"Web assets were not found: {web_dir}")

    service = DelayedTestService(exports_dir, results_dir, args.snapshots_dir)
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
