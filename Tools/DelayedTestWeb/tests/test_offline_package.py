"""Offline export and local grading checks using synthetic material only."""
import copy
import json
import re
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))
from fixture_support import make_fixture
from offline_package import OFFLINE_PROTOCOL, export_package, grade_result


class OfflinePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.exports, _, self.original, self.export_path = make_fixture(root / 'source')
        self.original_bytes = self.export_path.read_bytes()
        self.paths = export_package('P-DEMO', self.exports, root / 'packages')
        self.html = self.paths['html'].read_text(encoding='utf-8')
        self.public = json.loads(re.search(r'<script id="offlineData" type="application/json">(.*?)</script>', self.html, re.S)[1])
        self.key = json.loads(self.paths['key'].read_text(encoding='utf-8'))

    def test_participant_archive_is_self_contained_and_has_no_scoring_key(self):
        with zipfile.ZipFile(self.paths['zip']) as archive:
            self.assertEqual(set(archive.namelist()), {'START_TEST.html', 'READ_ME.txt'})
        self.assertNotIn('correctOptionId', self.html)
        self.assertNotIn('expectedMeaning', self.html)
        self.assertNotIn('sourceExportSha256', self.html)
        self.assertNotIn(str(self.export_path.parent), self.html)
        self.assertNotIn('<script src=', self.html)
        self.assertNotIn('<link rel="stylesheet"', self.html)
        self.assertIn("connect-src 'none'", self.html)
        self.assertEqual(self.public['storyText'], self.original['storySession']['fullStory'])
        self.assertEqual(len(self.public['matchingQuestions']), 8)
        for question in self.public['matchingQuestions']:
            self.assertTrue(question['snapshotUrl'].startswith('data:image/png;base64,'))
            self.assertEqual(len(question['options']), 3)
            self.assertNotIn('word', question)
            self.assertNotIn('anchorId', question)
        self.assertEqual(self.export_path.read_bytes(), self.original_bytes)

    def make_returned(self):
        meanings = self.key['meanings']
        return {'schemaVersion': 1, 'protocolVersion': OFFLINE_PROTOCOL, 'deliveryMode': 'offline',
                'packageId': self.key['packageId'], 'participantId': self.key['participantId'], 'sessionId': self.key['sessionId'],
                'status': 'complete', 'phase': 'complete', 'clientReportedTimestamps': True,
                'baselineResponses': [{'questionId': 'b' + str(index), 'word': word, 'answer': 'cracks' if meaning == 'crack' else meaning} for index, (word, meaning) in enumerate(meanings.items())],
                'postResponses': [{'questionId': 'p' + str(index), 'word': word, 'answer': ''} for index, word in enumerate(meanings)],
                'matchingResponses': [{'questionId': question['questionId'], 'optionId': question['correctOptionId'], 'chosenWord': question['word']} for question in self.key['matchingQuestions']]}

    def test_returned_file_is_graded_locally_without_overwriting_raw_or_source(self):
        returned = self.make_returned()
        raw = Path(self.temp.name) / 'returned.json'
        raw.write_text(json.dumps(returned), encoding='utf-8')
        raw_bytes = raw.read_bytes()
        graded, csv = grade_result(self.paths['key'], raw, Path(self.temp.name) / 'graded')
        result = json.loads(graded.read_text(encoding='utf-8'))
        self.assertEqual(result['baselineManualScore'], 8)
        self.assertEqual(result['matchingScore'], 8)
        self.assertEqual(result['postManualScore'], 0)
        self.assertEqual(result['deliveryMode'], 'offline')
        self.assertTrue(result['clientReportedTimestamps'])
        self.assertTrue(csv.is_file())
        self.assertEqual(raw.read_bytes(), raw_bytes)
        self.assertEqual(self.export_path.read_bytes(), self.original_bytes)

    def test_grading_rejects_wrong_package_duplicate_targets_or_invalid_choices(self):
        good = self.make_returned()
        invalid_cases = []
        other = copy.deepcopy(good); other['packageId'] = 'foreign'; invalid_cases.append(other)
        other = copy.deepcopy(good); other['status'] = 'in_progress'; invalid_cases.append(other)
        other = copy.deepcopy(good); other['baselineResponses'][0] = other['baselineResponses'][1]; invalid_cases.append(other)
        other = copy.deepcopy(good); other['matchingResponses'][0]['chosenWord'] = 'wrong'; invalid_cases.append(other)
        path = Path(self.temp.name) / 'invalid.json'
        for data in invalid_cases:
            path.write_text(json.dumps(data), encoding='utf-8')
            with self.assertRaises(ValueError):
                grade_result(self.paths['key'], path, Path(self.temp.name) / 'not-written')
        self.assertFalse((Path(self.temp.name) / 'not-written').exists())


if __name__ == '__main__':
    unittest.main()
