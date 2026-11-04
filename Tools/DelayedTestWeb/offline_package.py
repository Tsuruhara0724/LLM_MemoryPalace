#!/usr/bin/env python3
"""Export a participant-only offline page; grade returned JSON locally."""
from __future__ import annotations

import argparse
import base64
import csv
import hashlib
import json
import random
import re
import secrets
import zipfile
from datetime import timedelta, timezone
from pathlib import Path

from server import (APP_DIR, DEFAULT_EXPORTS_DIR, DEFAULT_RESULTS_DIR, PROTOCOL_VERSION,
                    REPO_ROOT, DelayedTestService, answers_match, iso_utc, resolve_session, utc_now)

OFFLINE_PROTOCOL = "offline-delayed-story-spatial-typed-v1"


def safe_name(value):
    return re.sub(r"[^A-Za-z0-9_-]", "_", value)[:100]


def export_package(lookup_id, exports_dir=DEFAULT_EXPORTS_DIR, output_dir=None, snapshot_dirs=None):
    record, _, _ = resolve_session(lookup_id, Path(exports_dir))
    service = DelayedTestService(Path(exports_dir), DEFAULT_RESULTS_DIR, snapshot_dirs)
    snapshots = service.check_materials(record)
    package_id = secrets.token_hex(16)
    stamp = utc_now().astimezone(timezone(timedelta(hours=9))).strftime("%Y%m%d-%H%M%S")
    root = Path(output_dir or REPO_ROOT / "Builds/OfflineDelayedTests") / (safe_name(record.participant_id) + "_" + stamp + "_" + package_id[:6])
    participant = root / "SendToParticipant"
    private = root / "ResearcherOnly"
    participant.mkdir(parents=True, exist_ok=False)
    private.mkdir()
    public = {"packageId": package_id, "participantId": record.participant_id, "sessionId": record.session_id,
              "studyCreatedAtUtc": iso_utc(record.created_at_utc),
              "scheduledForUtc": iso_utc(record.created_at_utc + timedelta(days=7)),
              "words": [item.word for item in record.items], "storyText": record.full_story, "matchingQuestions": []}
    key = {"packageId": package_id, "participantId": record.participant_id, "sessionId": record.session_id,
           "protocolVersion": OFFLINE_PROTOCOL, "sourceExportSha256": hashlib.sha256(record.source_path.read_bytes()).hexdigest(),
           "storySha256": hashlib.sha256(record.full_story.encode()).hexdigest(),
           "meanings": {item.word: item.meaning for item in record.items}, "matchingQuestions": []}
    rng = random.SystemRandom()
    for item, photo in zip(record.items, snapshots):
        others = [other.word for other in record.items if other.word != item.word]
        rng.shuffle(others)
        labels = [item.word, *others[:2]]
        rng.shuffle(labels)
        options = [{"optionId": secrets.token_hex(12), "label": label} for label in labels]
        question_id = secrets.token_hex(12)
        data = photo.read_bytes()
        mime = "image/png" if data.startswith(b"\x89PNG") else "image/jpeg"
        public["matchingQuestions"].append({"questionId": question_id, "furnitureLabel": item.anchor_label,
            "options": options, "snapshotUrl": "data:" + mime + ";base64," + base64.b64encode(data).decode("ascii")})
        key["matchingQuestions"].append({"questionId": question_id, "word": item.word, "anchorId": item.anchor_id,
            "anchorLabel": item.anchor_label, "options": options,
            "correctOptionId": next(option["optionId"] for option in options if option["label"] == item.word)})

    html = (APP_DIR / "web/index.html").read_text(encoding="utf-8")
    css = (APP_DIR / "web/styles.css").read_text(encoding="utf-8")
    adapter = (APP_DIR / "offline/adapter.js").read_text(encoding="utf-8")
    frontend = (APP_DIR / "web/app.js").read_text(encoding="utf-8")
    html = re.sub(r'<link rel="stylesheet" href="styles.css\?v=\d+">', "<style>" + css + "</style>", html)
    policy = "default-src 'none'; img-src data:; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'"
    html = html.replace('<meta charset="utf-8">', '<meta charset="utf-8">\n<meta http-equiv="Content-Security-Policy" content="' + policy + '">')
    html = html.replace('<p class="eyebrow">Memory Palace Study</p>', '<p class="eyebrow">Memory Palace Study · Offline</p>')
    html = html.replace('Enter the Participant ID or Session ID provided by the researcher.',
        'This offline file is for ' + safe_name(record.participant_id) + '. Use a desktop browser. Keep this page open until you download your answers. Nothing is uploaded automatically.')
    html = html.replace('Your responses are stored with the matched study Session ID.',
        'Use Chrome, Edge or Firefox. Do not preview the source or search for answers. After the test, download the result JSON and send it to the researcher.')
    html = html.replace('Both word tests and furniture matching have been saved separately. You may now close this page.',
        'Your answers are recorded in this open page, but have NOT been sent to the researcher. Download the result JSON below, verify it is in your Downloads folder, and send that file back. Scores will be calculated by the researcher.')
    html = html.replace('<div class="score-grid">', '<button id="offlineDownload" class="primary-button" type="button">Download result JSON</button><p id="offlineDownloadStatus" class="lead" role="status"></p><div class="score-grid">')
    data_text = json.dumps(public, ensure_ascii=False).replace("<", "\\u003c").replace(">", "\\u003e").replace("&", "\\u0026")
    embedded = '<script id="offlineData" type="application/json">' + data_text + '</script><script>\n' + adapter + '\n' + frontend + '\n</script>'
    html, substitutions = re.subn(r'<script src="app.js\?v=\d+" defer></script>', lambda _: embedded, html)
    if substitutions != 1 or 'id="offlineDownload"' not in html:
        raise ValueError("The page template changed; review offline export integration.")
    (participant / "START_TEST.html").write_text(html, encoding="utf-8")
    instructions = ("TEST01" if record.participant_id == "TEST01" else record.participant_id) + " — OFFLINE FOLLOW-UP TEST\n\n" + (
        "1. Extract this ZIP to a folder.\n2. Open START_TEST.html in Chrome, Edge or Firefox on a computer.\n"
        "3. Enter the Participant ID printed above. Complete typed word test -> original story -> furniture matching -> typed word retest.\n"
        "4. Do not refresh, close the page, inspect the source, or use a dictionary. Nothing is uploaded.\n"
        "5. At the end click Download result JSON. Find the file in Downloads and send it to the researcher.\n"
        "6. Keep the page open until you have checked that the downloaded file exists.\n\n"
        "Your original study story and furniture images are included for this test. Do not forward this package to anyone else.\n")
    (participant / "READ_ME.txt").write_text(instructions, encoding="utf-8")
    key_path = private / "SCORING_KEY_DO_NOT_SEND.json"
    key_path.write_text(json.dumps(key, ensure_ascii=False, indent=2), encoding="utf-8")
    (private / "RESEARCHER_NOTES.txt").write_text(
        "Send ONLY the ZIP in SendToParticipant. Never send this ResearcherOnly directory.\n"
        "This is an offline delivery variant, not a secured web link. The story and images are embedded and can be inspected ahead of time by a technically skilled participant.\n"
        "There is no identity verification, tamper-proofing, server time, expiry, automatic return, or crash/reload recovery. The participant must download and return their JSON.\n"
        "Keep these limitations separate from supervised or server-gated testing in analysis. No English scoring key or furniture-answer mapping is included in the ZIP.\n"
        "Grade a returned file locally: py -3 offline_package.py grade --key <this key JSON> --result <returned JSON>\n", encoding="utf-8")
    archive = participant / ("DelayedTest_" + safe_name(record.participant_id) + ".zip")
    with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED) as package:
        for name in ["START_TEST.html", "READ_ME.txt"]:
            package.write(participant / name, arcname=name)
    return {"zip": archive, "html": participant / "START_TEST.html", "key": key_path, "root": root}


def grade_result(key_path, result_path, output_dir=DEFAULT_RESULTS_DIR):
    key = json.loads(Path(key_path).read_text(encoding="utf-8-sig"))
    result = json.loads(Path(result_path).read_text(encoding="utf-8-sig"))
    for field in ["packageId", "participantId", "sessionId", "protocolVersion"]:
        if result.get(field) != key.get(field):
            raise ValueError("Returned result does not match the scoring key: " + field)
    if result.get("protocolVersion") != OFFLINE_PROTOCOL or result.get("status") != "complete":
        raise ValueError("A complete offline result is required.")
    meanings = key["meanings"]
    def manual(field):
        responses = result.get(field)
        if not isinstance(responses, list) or len(responses) != len(meanings):
            raise ValueError("Incomplete typed stage: " + field)
        words = [response.get("word") for response in responses]
        if len(set(words)) != len(meanings) or set(words) != set(meanings):
            raise ValueError("Invalid or duplicate typed target.")
        scored = []
        for response in responses:
            answer = response.get("answer")
            if not isinstance(answer, str) or len(answer) > 300:
                raise ValueError("Invalid typed answer.")
            correct, mode, variant = answers_match(answer, meanings[response["word"]])
            scored.append({**response, "expectedMeaning": meanings[response["word"]], "isCorrect": correct,
                           "matchMode": mode, "matchedExpectedVariant": variant})
        return scored
    baseline = manual("baselineResponses")
    post = manual("postResponses")
    questions = key["matchingQuestions"]
    raw = result.get("matchingResponses", [])
    received = DelayedTestService._received(raw, questions)
    matching = []
    for question in questions:
        response = received[question["questionId"]]
        option = next((entry for entry in question["options"] if entry["optionId"] == response.get("optionId")), None)
        if option is None or option["label"] != response.get("chosenWord"):
            raise ValueError("Invalid matching option.")
        matching.append({**response, "word": question["word"], "anchorId": question["anchorId"],
                         "anchorLabel": question["anchorLabel"], "isCorrect": option["optionId"] == question["correctOptionId"]})
    payload = {**result, "deliveryMode": "offline", "clientReportedTimestamps": True, "integrityVerified": False,
               "gradingProtocolVersion": PROTOCOL_VERSION, "gradedAtUtc": iso_utc(utc_now()),
               "baselineResponses": baseline, "matchingResponses": matching, "postResponses": post,
               "baselineManualScore": sum(item["isCorrect"] for item in baseline),
               "matchingScore": sum(item["isCorrect"] for item in matching),
               "postManualScore": sum(item["isCorrect"] for item in post), "wordCount": len(meanings)}
    folder = Path(output_dir)
    folder.mkdir(parents=True, exist_ok=True)
    stem = "offline_graded_" + safe_name(key["participantId"]) + "_" + utc_now().strftime("%Y%m%d_%H%M%S") + "_" + secrets.token_hex(4)
    json_path = folder / (stem + ".json")
    csv_path = folder / (stem + ".csv")
    json_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    post_by_word = {entry["word"]: entry for entry in post}
    matching_by_word = {entry["word"]: entry for entry in matching}
    with csv_path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(["participant_id", "session_id", "package_id", "delivery_mode", "word", "expected_meaning", "baseline_answer", "baseline_correct", "matching_answer", "matching_correct", "post_answer", "post_correct"])
        for entry in baseline:
            word = entry["word"]
            writer.writerow([key["participantId"], key["sessionId"], key["packageId"], "offline", word, meanings[word],
                             entry["answer"], entry["isCorrect"], matching_by_word[word]["chosenWord"],
                             matching_by_word[word]["isCorrect"], post_by_word[word]["answer"], post_by_word[word]["isCorrect"]])
    return json_path, csv_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    export = commands.add_parser("export")
    export.add_argument("--id", required=True)
    export.add_argument("--exports-dir", type=Path, default=DEFAULT_EXPORTS_DIR)
    export.add_argument("--output-dir", type=Path)
    export.add_argument("--snapshots-dir", type=Path, action="append", default=[])
    grade = commands.add_parser("grade")
    grade.add_argument("--key", type=Path, required=True)
    grade.add_argument("--result", type=Path, required=True)
    grade.add_argument("--output-dir", type=Path, default=DEFAULT_RESULTS_DIR)
    args = parser.parse_args()
    if args.command == "export":
        paths = export_package(args.id, args.exports_dir, args.output_dir, args.snapshots_dir)
        print("SEND ONLY: " + str(paths["zip"].resolve()))
        print("KEEP PRIVATE: " + str(paths["key"].resolve()))
    else:
        for path in grade_result(args.key, args.result, args.output_dir):
            print(path.resolve())


if __name__ == "__main__":
    main()
