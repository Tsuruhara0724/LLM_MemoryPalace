"""Invitation security checks with synthetic sessions only."""
import json
import sys
import tempfile
import threading
import unittest
from datetime import timedelta
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))
from fixture_support import make_fixture
from share_server import COOKIE_NAME, create_server
from server import utc_now


class InvitationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.exports, self.results, self.payload, self.path = make_fixture(self.temp.name)
        # A second real-looking export exists, but must be unreachable via the invitation.
        foreign = json.loads(self.path.read_text(encoding='utf-8'))
        foreign['participantId'] = 'P-OTHER'
        foreign['sessionId'] = '20260821_120000'
        (self.exports / 'session_P-OTHER.json').write_text(json.dumps(foreign), encoding='utf-8')
        self.server = create_server(self.exports, self.results, self.payload['sessionId'], port=0, hours=24)
        self.server.RequestHandlerClass.func.log_message = lambda *args: None
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.base = 'http://127.0.0.1:' + str(self.server.server_port)
        self.cookie = COOKIE_NAME + '=' + self.server.invitation.cookie
        self.addCleanup(self.stop)

    def stop(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)

    def request(self, route, body=None, cookie=None, token=None, origin=None, method=None):
        headers = {}
        if cookie is not None: headers['Cookie'] = cookie
        if token is not None: headers['Authorization'] = 'Bearer ' + token
        if origin is not None: headers['Origin'] = origin
        data = None if body is None else json.dumps(body).encode()
        if data is not None: headers['Content-Type'] = 'application/json'
        return urlopen(Request(self.base + route, data=data, headers=headers, method=method), timeout=3)

    def test_static_page_is_public_but_all_data_and_direct_files_are_protected(self):
        with self.request('/') as response:
            html = response.read().decode()
            self.assertNotIn(self.payload['storySession']['fullStory'], html)
        for route, body in [('/api/start', {'lookupId': 'P-DEMO'}), ('/api/health', None), ('/api/snapshot?attemptId=x&questionId=y', None)]:
            with self.assertRaises(HTTPError) as error: self.request(route, body)
            self.assertEqual(error.exception.code, 403)
        with self.assertRaises(HTTPError) as error: self.request('/../server.py', cookie=self.cookie)
        self.assertEqual(error.exception.code, 404)
        with self.assertRaises(HTTPError) as error: self.request('/api/snapshot', method='HEAD')
        self.assertEqual(error.exception.code, 404)

    def test_link_token_exchanges_for_secure_cookie_without_returning_secret(self):
        for token in ['', 'wrong', 'é']:
            with self.assertRaises(HTTPError) as error: self.request('/api/access', {}, token=token)
            self.assertEqual(error.exception.code, 403)
        with self.request('/api/access', {}, token=self.server.invitation.token) as response:
            text = response.read().decode()
            self.assertNotIn(self.server.invitation.token, text)
            self.assertNotIn(self.server.invitation.cookie, text)
            self.assertEqual(json.loads(text)['sessionId'], self.payload['sessionId'])
            cookie = response.headers['Set-Cookie']
            for flag in ['HttpOnly', 'Secure', 'SameSite=Strict', 'Max-Age=']:
                self.assertIn(flag, cookie)
        with self.request('/api/start', {'lookupId': 'P-DEMO'}, cookie=self.cookie) as response:
            started = json.load(response)
        self.assertEqual(started['participantId'], 'P-DEMO')
        self.assertEqual(self.server.service._attempts[started['attemptId']]['phase'], 'baseline_manual')

    def test_valid_invitation_cannot_switch_to_another_export(self):
        for lookup in ['P-OTHER', '20260821_120000', 'nonexistent']:
            with self.assertRaises(HTTPError) as error: self.request('/api/start', {'lookupId': lookup}, cookie=self.cookie)
            self.assertEqual(error.exception.code, 403)
        self.assertEqual(len(self.server.service._attempts), 0)

    def test_cross_origin_submission_and_expired_or_old_invitation_are_denied(self):
        with self.assertRaises(HTTPError) as error:
            self.request('/api/access', {}, token=self.server.invitation.token, origin='https://foreign.example')
        self.assertEqual(error.exception.code, 403)
        with self.assertRaises(HTTPError) as error:
            self.request('/api/start', {'lookupId': 'P-DEMO'}, cookie=COOKIE_NAME + '=old-cookie')
        self.assertEqual(error.exception.code, 403)
        self.server.invitation.expires_at = utc_now() - timedelta(seconds=1)
        for route, body, token, cookie in [('/api/access', {}, self.server.invitation.token, None),
                                          ('/api/start', {'lookupId': 'P-DEMO'}, None, self.cookie),
                                          ('/api/health', None, None, self.cookie)]:
            with self.assertRaises(HTTPError) as error:
                self.request(route, body, token=token, cookie=cookie)
            self.assertEqual(error.exception.code, 410)


if __name__ == '__main__':
    unittest.main()
