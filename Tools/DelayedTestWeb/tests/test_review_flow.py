"""Synthetic, temporary-data checks. Never submit a real participant attempt."""
import copy
import csv
import json
import sys
import tempfile
import threading
import unittest
from functools import partial
from http.server import ThreadingHTTPServer
from pathlib import Path
from unittest import mock
from urllib.error import HTTPError
from urllib.request import Request, urlopen

TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))
from server import ApiError, DelayedTestRequestHandler, DelayedTestService, PROTOCOL_VERSION


from fixture_support import make_fixture

class ReviewFlowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.exports, self.results, self.payload, self.path = make_fixture(self.temp.name)
        self.service = DelayedTestService(self.exports, self.results)
        self.started = self.service.start('P-DEMO')
        self.attempt_id = self.started['attemptId']
        self.meanings = {item['word']: item['meaning'] for item in self.payload['items']}

    def manual(self, questions, correct=True):
        return [{'questionId': q['questionId'], 'answer': self.meanings[q['word']] if correct else '', 'responseTimeMs': 1200} for q in questions]

    def to_story(self):
        return self.service.submit_manual(self.attempt_id, self.manual(self.started['manualQuestions']))

    def match(self, questions):
        by_label = {item['anchorLabel']: item['word'] for item in self.payload['items']}
        return [{'questionId': q['questionId'], 'optionId': next(o['optionId'] for o in q['options'] if o['label'] == by_label[q['furnitureLabel']]), 'responseTimeMs': 800} for q in questions]

    def test_start_does_not_expose_story_meanings_or_snapshots(self):
        text = json.dumps(self.started)
        self.assertNotIn('storyText', text)
        self.assertNotIn('fullStory', text)
        self.assertNotIn('snapshot', text.casefold())
        self.assertNotIn('expectedMeaning', text)
        self.assertEqual(self.started['protocolVersion'], PROTOCOL_VERSION)
        self.assertEqual(len(self.started['manualQuestions']), 8)
        with self.assertRaises(ApiError) as error:
            self.service.snapshot(self.attempt_id, 'unknown')
        self.assertEqual(error.exception.status, 403)

    def test_complete_flow_preserves_baseline_and_saves_separate_scores(self):
        original_file = self.path.read_bytes()
        story = self.to_story()
        self.assertEqual(story['storyText'], self.payload['storySession']['fullStory'])
        self.assertEqual(story['nextPhase'], 'story')
        self.assertNotIn('choiceQuestions', story)
        self.assertNotIn('Score', json.dumps(story))
        file = next(self.results.glob('delayed_*.json'))
        checkpoint = json.loads(file.read_text(encoding='utf-8'))
        self.assertEqual(checkpoint['status'], 'in_progress')
        self.assertEqual(checkpoint['manualScore'], 8)
        baseline = copy.deepcopy(checkpoint['baseline'])
        matching = self.service.submit_story(self.attempt_id)
        for question in matching['matchingQuestions']:
            self.assertEqual(len(question['options']), 3)
            self.assertNotIn('word', question)
            self.assertNotIn('correctOptionId', question)
            self.assertNotIn('SpatialSnapshots', question['snapshotUrl'])
            data, content_type = self.service.snapshot(self.attempt_id, question['questionId'])
            self.assertTrue(data.startswith(b'\x89PNG'))
            self.assertEqual(content_type, 'image/png')
        post = self.service.submit_matching(self.attempt_id, self.match(matching['matchingQuestions']))
        self.assertNotIn('Score', json.dumps(post))
        self.assertTrue({q['questionId'] for q in post['manualQuestions']}.isdisjoint(q['questionId'] for q in self.started['manualQuestions']))
        final_responses = self.manual(post['manualQuestions'], False)
        summary = self.service.submit_manual(self.attempt_id, final_responses, 'post')
        self.assertEqual(summary['nextPhase'], 'complete')
        self.assertNotIn('choiceQuestions', summary)
        self.assertEqual(summary['baselineManualScore'], 8)
        self.assertEqual(summary['matchingScore'], 8)
        self.assertEqual(summary['postManualScore'], 0)
        self.assertNotIn('baselineChoiceScore', summary)
        self.assertNotIn('postChoiceScore', summary)
        complete = json.loads(file.read_text(encoding='utf-8'))
        self.assertEqual(complete['schemaVersion'], 3)
        self.assertEqual(complete['vocabularyTestFormat'], 'typed_only')
        self.assertFalse(complete['choiceTestAdministered'])
        self.assertIsNone(complete['choiceScore'])
        self.assertEqual(complete['choiceResponses'], [])
        self.assertNotIn('choiceQuestions', complete['baseline'])
        self.assertNotIn('choiceQuestions', complete['postReview'])
        self.assertEqual(complete['status'], 'complete')
        self.assertEqual(complete['baseline'], baseline)
        self.assertEqual(complete['manualScore'], 8)
        self.assertEqual(len(complete['postReview']['manualResponses']), 8)
        self.assertEqual(complete['stageOrder'], ['baseline_manual', 'story', 'matching', 'post_manual'])
        self.assertIsNotNone(complete['storyReview']['acknowledgedAtUtc'])
        with next(self.results.glob('delayed_*.csv')).open(encoding='utf-8-sig', newline='') as stream:
            rows = list(csv.DictReader(stream))
        self.assertEqual(len(rows), 8)
        self.assertTrue(all(row['manual_correct'] == 'True' and row['matching_correct'] == 'True' and row['post_manual_correct'] == 'False' for row in rows))
        self.assertTrue(all(row['choice_correct'] == '' and row['post_choice_correct'] == ''
                            and row['choice_test_administered'] == 'False' for row in rows))
        self.assertEqual(self.path.read_bytes(), original_file)
        self.assertEqual(self.service.submit_manual(self.attempt_id, final_responses, 'post'), summary)
        self.assertEqual(len(list(self.results.glob('*.json'))), 1)
        self.assertEqual(self.service.start('P-DEMO')['priorDelayedAttemptCount'], 1)

    def test_stage_order_cannot_be_bypassed(self):
        for action in [lambda: self.service.submit_story(self.attempt_id), lambda: self.service.submit_matching(self.attempt_id, []),
                       lambda: self.service.submit_manual(self.attempt_id, [], 'post')]:
            with self.assertRaises(ApiError) as error:
                action()
            self.assertEqual(error.exception.status, 409)

    def test_incomplete_duplicate_unknown_and_malformed_questions_are_rejected(self):
        valid = self.manual(self.started['manualQuestions'])
        for invalid in [valid[:-1], valid + [valid[0]], [{'questionId': 'unknown'}], [{'questionId': []}], 'not a list']:
            with self.assertRaises(ApiError) as error:
                self.service.submit_manual(self.attempt_id, invalid)
            self.assertEqual(error.exception.status, 400)
        self.assertEqual(self.service._attempts[self.attempt_id]['phase'], 'baseline_manual')
        for stage in [[], {}, None, 'unknown']:
            with self.assertRaises(ApiError) as error:
                self.service.submit_manual(self.attempt_id, [], stage)
            self.assertEqual(error.exception.status, 400)

    def test_retries_are_idempotent_and_saved_answers_cannot_be_replaced(self):
        responses = self.manual(self.started['manualQuestions'])
        first = self.service.submit_manual(self.attempt_id, responses)
        self.assertEqual(self.service.submit_manual(self.attempt_id, responses), first)
        changed = copy.deepcopy(responses)
        changed[0]['answer'] = 'different'
        with self.assertRaises(ApiError) as error:
            self.service.submit_manual(self.attempt_id, changed)
        self.assertEqual(error.exception.status, 409)
        matching = self.service.submit_story(self.attempt_id)
        self.assertEqual(self.service.submit_story(self.attempt_id), matching)
        invalid = self.match(matching['matchingQuestions'])
        invalid[0]['optionId'] = 'not-an-option'
        with self.assertRaises(ApiError):
            self.service.submit_matching(self.attempt_id, invalid)
        self.assertEqual(self.service._attempts[self.attempt_id]['phase'], 'matching')
        valid = self.match(matching['matchingQuestions'])
        first_post = self.service.submit_matching(self.attempt_id, valid)
        self.assertEqual(self.service.submit_matching(self.attempt_id, valid), first_post)

    def test_save_failure_can_retry_without_skipping_story(self):
        responses = self.manual(self.started['manualQuestions'])
        with mock.patch.object(self.service, '_save_result', side_effect=OSError('synthetic disk failure')):
            with self.assertRaises(OSError):
                self.service.submit_manual(self.attempt_id, responses)
        self.assertEqual(self.service._attempts[self.attempt_id]['phase'], 'baseline_manual')
        self.assertEqual(self.service.submit_manual(self.attempt_id, responses)['nextPhase'], 'story')

    def test_post_save_failure_can_retry_without_repeating_matching(self):
        self.to_story()
        matching = self.service.submit_story(self.attempt_id)
        post = self.service.submit_matching(self.attempt_id, self.match(matching['matchingQuestions']))
        responses = self.manual(post['manualQuestions'])
        with mock.patch.object(self.service, '_save_result', side_effect=OSError('synthetic disk failure')):
            with self.assertRaises(OSError):
                self.service.submit_manual(self.attempt_id, responses, 'post')
        self.assertEqual(self.service._attempts[self.attempt_id]['phase'], 'post_manual')
        self.assertIsNone(self.service._attempts[self.attempt_id]['completedAtUtc'])
        self.assertEqual(self.service.submit_manual(self.attempt_id, responses, 'post')['nextPhase'], 'complete')

    def test_missing_materials_are_explicit_not_synthetic_substitutions(self):
        for field, code in [('story', 'missing_story'), ('anchor', 'missing_furniture'), ('snapshot', 'missing_snapshots')]:
            changed = copy.deepcopy(self.payload)
            if field == 'story': changed['storySession'] = {}
            if field == 'anchor': changed['items'][0]['anchorId'] = ''
            if field == 'snapshot': changed['items'][0]['sceneSnapshotPath'] = ''
            self.path.write_text(json.dumps(changed), encoding='utf-8')
            service = DelayedTestService(self.exports, self.results)
            with self.assertRaises(ApiError) as error: service.start('P-DEMO')
            self.assertEqual(error.exception.code, code)
            self.assertEqual(service._attempts, {})

    def test_snapshot_traversal_unc_and_wrong_session_are_rejected(self):
        for value in ['../private.png', '//network/share/photo.png', str(Path(self.temp.name) / 'private.png')]:
            changed = copy.deepcopy(self.payload)
            changed['items'][0]['sceneSnapshotPath'] = value
            self.path.write_text(json.dumps(changed), encoding='utf-8')
            with self.assertRaises(ApiError) as error: DelayedTestService(self.exports, self.results).start('P-DEMO')
            self.assertEqual(error.exception.code, 'missing_snapshots')

    def test_snapshot_question_and_expiry_checks(self):
        self.to_story()
        questions = self.service.submit_story(self.attempt_id)['matchingQuestions']
        with self.assertRaises(ApiError): self.service.snapshot(self.attempt_id, 'another-question')
        with self.assertRaises(ApiError): self.service.snapshot('another-attempt', questions[0]['questionId'])
        from datetime import timedelta
        self.service._attempts[self.attempt_id]['createdAt'] -= timedelta(hours=9)
        with self.assertRaises(ApiError): self.service.snapshot(self.attempt_id, questions[0]['questionId'])

    def test_relocated_original_snapshots_require_session_matching_trusted_root(self):
        from fixture_support import synthetic_png
        with tempfile.TemporaryDirectory() as moved_folder:
            moved = Path(moved_folder)
            photos = moved / ('session_' + self.payload['sessionId']) / 'SpatialSnapshots'
            photos.mkdir(parents=True)
            changed = copy.deepcopy(self.payload)
            for index, item in enumerate(changed['items']):
                name = 'moved_' + str(index) + '.png'
                (photos / name).write_bytes(synthetic_png(index))
                item['sceneSnapshotPath'] = 'Z:/old-machine/session_' + self.payload['sessionId'] + '/SpatialSnapshots/' + name
            self.path.write_text(json.dumps(changed), encoding='utf-8')
            with self.assertRaises(ApiError):
                DelayedTestService(self.exports, self.results).start('P-DEMO')
            service = DelayedTestService(self.exports, self.results, [moved])
            self.assertEqual(service.start('P-DEMO')['wordCount'], 8)
            # A folder with only a similar session prefix must not match.
            photos.parent.rename(moved / ('session_' + self.payload['sessionId'] + '_wrong'))
            with self.assertRaises(ApiError):
                DelayedTestService(self.exports, self.results, [moved]).start('P-DEMO')

    def test_old_result_files_remain_untouched_and_count_as_previous_attempts(self):
        self.results.mkdir()
        previous = []
        for schema in [1, 2]:
            path = self.results / ('delayed_old_' + str(schema) + '.json')
            data = {'schemaVersion': schema, 'sessionId': self.started['sessionId'], 'manualScore': 3, 'choiceScore': 5}
            path.write_text(json.dumps(data), encoding='utf-8')
            previous.append((path, path.read_bytes()))
        self.assertEqual(self.service.start('P-DEMO')['priorDelayedAttemptCount'], 2)
        self.to_story()
        for path, original in previous:
            self.assertEqual(path.read_bytes(), original)


class HttpBoundaryTests(unittest.TestCase):
    def test_http_phase_gates_and_private_snapshot_headers(self):
        with tempfile.TemporaryDirectory() as folder:
            exports, results, payload, _ = make_fixture(folder)
            service = DelayedTestService(exports, results)
            class QuietHandler(DelayedTestRequestHandler):
                def log_message(self, *args): pass
            server = ThreadingHTTPServer(('127.0.0.1', 0), partial(QuietHandler, directory=str(TOOL_DIR / 'web')))
            server.service = service
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            base = 'http://127.0.0.1:' + str(server.server_port)
            def post(route, body):
                request = Request(base + route, data=json.dumps(body).encode(), headers={'Content-Type': 'application/json'})
                with urlopen(request) as response: return json.load(response)
            try:
                with urlopen(base + '/api/health') as response:
                    self.assertEqual(json.load(response)['protocolVersion'], PROTOCOL_VERSION)
                started = post('/api/start', {'lookupId': 'P-DEMO'})
                with self.assertRaises(HTTPError) as error: post('/api/story', {'attemptId': started['attemptId']})
                self.assertEqual(error.exception.code, 409)
                meanings = {item['word']: item['meaning'] for item in payload['items']}
                with self.assertRaises(HTTPError) as error:
                    post('/api/choice', {'attemptId': started['attemptId'], 'responses': []})
                self.assertEqual(error.exception.code, 404)
                story = post('/api/manual', {'attemptId': started['attemptId'], 'responses': [{'questionId': q['questionId'], 'answer': meanings[q['word']]} for q in started['manualQuestions']]})
                self.assertEqual(story['nextPhase'], 'story')
                self.assertNotIn('choiceQuestions', story)
                matching = post('/api/story', {'attemptId': started['attemptId']})
                with urlopen(base + matching['matchingQuestions'][0]['snapshotUrl']) as response:
                    self.assertEqual(response.headers['Content-Type'], 'image/png')
                    self.assertIn('no-store', response.headers['Cache-Control'])
                    self.assertEqual(response.headers['X-Content-Type-Options'], 'nosniff')
                with self.assertRaises(HTTPError): urlopen(base + '/api/snapshot?attemptId=bad&questionId=bad')
                words_by_anchor = {item['anchorLabel']: item['word'] for item in payload['items']}
                retest = post('/api/matching', {'attemptId': started['attemptId'], 'responses': [
                    {'questionId': q['questionId'], 'optionId': next(o['optionId'] for o in q['options']
                     if o['label'] == words_by_anchor[q['furnitureLabel']])} for q in matching['matchingQuestions']]})
                summary = post('/api/manual', {'attemptId': started['attemptId'], 'stage': 'post', 'responses': [
                    {'questionId': q['questionId'], 'answer': meanings[q['word']]} for q in retest['manualQuestions']]})
                self.assertEqual(summary['nextPhase'], 'complete')
                self.assertEqual(summary['postManualScore'], 8)
            finally:
                server.shutdown()
                server.server_close()
                thread.join(timeout=3)


if __name__ == '__main__':
    unittest.main()
