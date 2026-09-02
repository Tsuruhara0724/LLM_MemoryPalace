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
    def test_two_round_flow_hides_answers_then_saves_json_and_csv(self):
        with tempfile.TemporaryDirectory() as raw_results:
            results_dir = Path(raw_results)
            service = DelayedTestService(FIXTURES_DIR, results_dir)

            started = service.start("P-DEMO")
            self.assertEqual(started["wordCount"], 8)
            self.assertEqual(started["sessionId"], "20260820_120000")
            self.assertEqual(started["priorDelayedAttemptCount"], 0)
            serialized_start = json.dumps(started).casefold()
            for meaning in MEANINGS.values():
                self.assertNotIn(f'"expectedmeaning": "{meaning}"', serialized_start)

            manual_responses = []
            for question in started["manualQuestions"]:
                answer = MEANINGS[question["word"]]
                if answer == "crack":
                    answer = "cracks"
                manual_responses.append(
                    {
                        "questionId": question["questionId"],
                        "answer": answer,
                        "responseTimeMs": 1200,
                    }
                )

            second_round = service.submit_manual(started["attemptId"], manual_responses)
            self.assertEqual(second_round["wordCount"], 8)
            self.assertEqual(len(second_round["choiceQuestions"]), 8)
            for question in second_round["choiceQuestions"]:
                self.assertNotIn("expectedMeaning", question)
                self.assertEqual(len(question["options"]), 3)
                self.assertEqual(len({option["label"] for option in question["options"]}), 3)
                self.assertTrue(all("isCorrect" not in option for option in question["options"]))

            choice_responses = []
            for question in second_round["choiceQuestions"]:
                expected = MEANINGS[question["word"]]
                correct = next(option for option in question["options"] if option["label"] == expected)
                choice_responses.append(
                    {
                        "questionId": question["questionId"],
                        "optionId": correct["optionId"],
                        "responseTimeMs": 900,
                    }
                )

            summary = service.submit_choice(started["attemptId"], choice_responses)
            self.assertEqual(summary["manualScore"], 8)
            self.assertEqual(summary["choiceScore"], 8)

            json_files = list(results_dir.glob("delayed_*.json"))
            csv_files = list(results_dir.glob("delayed_*.csv"))
            self.assertEqual(len(json_files), 1)
            self.assertEqual(len(csv_files), 1)

            payload = json.loads(json_files[0].read_text(encoding="utf-8"))
            self.assertEqual(payload["testType"], "one_week_delayed_vocabulary")
            self.assertEqual(payload["manualScore"], 8)
            self.assertEqual(payload["choiceScore"], 8)
            self.assertEqual(len(payload["manualResponses"]), 8)
            self.assertEqual(len(payload["choiceResponses"]), 8)

            with csv_files[0].open(encoding="utf-8-sig", newline="") as stream:
                rows = list(csv.DictReader(stream))
            self.assertEqual(len(rows), 8)
            self.assertEqual({row["target_word"] for row in rows}, set(MEANINGS))
            self.assertTrue(all(row["manual_correct"] == "True" for row in rows))
            self.assertTrue(all(row["choice_correct"] == "True" for row in rows))

            repeated = service.start("20260820_120000")
            self.assertEqual(repeated["priorDelayedAttemptCount"], 1)


if __name__ == "__main__":
    unittest.main()
