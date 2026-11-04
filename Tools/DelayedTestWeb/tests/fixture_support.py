import json
import struct
import zlib
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]


def synthetic_png(index=0):
    """Small code-generated test pattern; not a photograph or research stimulus."""
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    rows = []
    for y in range(120):
        row = bytearray([0])
        for x in range(200):
            block = 40 < x < 160 and 30 < y < 90
            row.extend(((45 + index * 23) % 220, 100, 125) if block else (230, 233, 225))
        rows.append(bytes(row))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 200, 120, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b''.join(rows))) + chunk(b'IEND', b'')


def make_fixture(root):
    root = Path(root)
    exports = root / 'exports'
    exports.mkdir(parents=True, exist_ok=True)
    payload = json.loads((TOOL_DIR / 'tests/fixtures/session_P-DEMO_20260820_120000.json').read_text(encoding='utf-8'))
    photos = root / 'MemoryPalaceSessionData' / ('session_' + payload['sessionId']) / 'SpatialSnapshots'
    photos.mkdir(parents=True, exist_ok=True)
    for index, item in enumerate(payload['items']):
        photo = photos / ('test_' + str(index) + '.png')
        photo.write_bytes(synthetic_png(index))
        item['sceneSnapshotPath'] = str(photo)
    path = exports / 'session_P-DEMO_20260820_120000.json'
    path.write_text(json.dumps(payload, ensure_ascii=False), encoding='utf-8')
    return exports, root / 'results', payload, path

