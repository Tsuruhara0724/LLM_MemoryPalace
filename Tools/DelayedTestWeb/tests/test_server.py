import csv
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOL_DIR = Path(__file__).resolve().parents[1]
FIXTURES_DIR = Path(__file__).resolve().parent / "fixtures"
sys.path.insert(0, str(TOOL_DIR))

from server import DelayedTestService, answers_match, normalize_english, resolve_session  # noqa: E402


MEANINGS = {
    "rodilla": "knee",
    "grieta": "crack",
    "pegamento": "glue",
    "rueda": "wheel",
    "pajaro": "bird",
    "juego": "game",
    "paraguas": "umbrella",
    "burbuja": "bubble",
}


class MorphologyTests(unittest.TestCase):
    def assert_matches(self, answer, expected):
        matched, _, _ = answers_match(answer, expected)
        self.assertTrue(matched, f"Expected {answer!r} to match {expected!r}")

    def test_normalization_and_common_inflections(self):
        self.assertEqual(normalize_english("  The KNEE!  "), "the knee")
        self.assert_matches("knee", "knee")
        self.assert_matches("the knee", "knee")
        self.assert_matches("cracks", "crack")
        self.assert_matches("searching", "to search")
        self.assert_matches("fearful", "fear")
        self.assert_matches("children", "child")

    def test_blank_and_unrelated_answers_fail(self):
        self.assertFalse(answers_match("", "wheel")[0])
        self.assertFalse(answers_match("umbrella", "wheel")[0])


class SessionResolutionTests(unittest.TestCase):
    def _write_session(self, folder, participant, session, created):
        payload = {
            "participantId": participant,
            "sessionId": session,
            "createdAtUtc": created,
            "items": [
                {"word": "uno", "meaning": "one"},
                {"word": "dos", "meaning": "two"},
                {"word": "tres", "meaning": "three"},
            ],
        }
        (folder / f"session_{participant}_{session}.json").write_text(
            json.dumps(payload), encoding="utf-8"
        )

    def test_participant_chooses_latest_but_session_is_exact(self):
        with tempfile.TemporaryDirectory() as raw_dir:
            folder = Path(raw_dir)
            self._write_session(folder, "P100", "OLD", "2026-01-01T00:00:00Z")
            self._write_session(folder, "P100", "NEW", "2026-02-01T00:00:00Z")

            latest, matched_by, count = resolve_session("p100", folder)
            self.assertEqual(latest.session_id, "NEW")
            self.assertEqual(matched_by, "participantId")
            self.assertEqual(count, 2)

            exact, matched_by, count = resolve_session("old", folder)
            self.assertEqual(exact.session_id, "OLD")
            self.assertEqual(matched_by, "sessionId")
            self.assertEqual(count, 1)


class FullTestFlowTests(unittest.TestCase):
    def test_baseline_keeps_old_vocabulary_rules_and_csv_columns(self):
        from fixture_support import make_fixture
        with tempfile.TemporaryDirectory() as folder:
            exports, results, _, _ = make_fixture(folder)
            service = DelayedTestService(exports, results)
            started = service.start("P-DEMO")
            manual = []
            for question in started["manualQuestions"]:
                answer = MEANINGS[question["word"]]
                manual.append({"questionId": question["questionId"],
                               "answer": "cracks" if answer == "crack" else answer, "responseTimeMs": 1200})
            story = service.submit_manual(started["attemptId"], manual)
            self.assertEqual(story["nextPhase"], "story")
            payload = json.loads(next(results.glob("*.json")).read_text(encoding="utf-8"))
            self.assertEqual(payload["manualScore"], 8)
            self.assertIsNone(payload["choiceScore"])
            self.assertFalse(payload["choiceTestAdministered"])
            self.assertEqual(payload["status"], "in_progress")
            with next(results.glob("*.csv")).open(encoding="utf-8-sig", newline="") as stream:
                rows = list(csv.DictReader(stream))
            self.assertEqual(len(rows), 8)
            self.assertTrue(all(row["manual_correct"] == "True" and row["choice_correct"] == "" for row in rows))


if __name__ == "__main__":
    unittest.main()
