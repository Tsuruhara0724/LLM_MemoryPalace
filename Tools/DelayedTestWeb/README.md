# One-week delayed vocabulary test

This local website reads the original Unity experiment export and runs this sequence:

1. **First word test:** Spanish word → typed English meaning only. Common English inflections are accepted, such as `cracks` for `crack` and `searching` for `search`.
2. **Original story:** show the exact `storySession.fullStory` saved during learning. The participant clicks Continue when ready.
3. **Furniture matching:** show each original furniture snapshot and select its assigned Spanish word from three choices.
4. **Word retest:** type the English meanings again, with independently randomized word order. Neither word test includes an English multiple-choice round.

No correctness feedback or scores appear before the final page. Neither typed test receives English answer choices, and the browser receives no story or snapshots before the first word test is saved. Furniture matching retains its Spanish-only choices, not English definitions.

The first word test measures delayed performance before story review. The final test measures performance **after story review and furniture retrieval practice**; do not combine these scores or treat the final result as an uncued delayed-memory score. The protocol identifier is `delayed-story-spatial-typed-retest-v3`.

## Start locally

From this folder in PowerShell:

```powershell
py -3 .\server.py --host 127.0.0.1 --port 8767
```

Then open:

```text
http://127.0.0.1:8767/
```

The direct Python command does not require changing PowerShell's script execution policy. Alternatively use `.\Start-DelayedTestWeb.cmd --port 8767`, or `.\Start-DelayedTestWeb.ps1 -Port 8767` if PowerShell scripts are allowed. Without a port argument, both launchers default to `8765`. Use a free port if the Quest package server or an older delayed-test server is already running. Restart Python after updating the backend; do not mix new web assets with an old running server. Do not restart while a participant is answering. Existing v2 attempts cannot continue under v3; keep their saved checkpoints and start a new attempt after restarting.

The default input folder is the repository's `ExperimentExports` directory. An optional `?id=<sessionId>` URL prefills the lookup field. The participant can enter either:

- an exact `sessionId`, which selects that experiment session; or
- a `participantId`, which selects that participant's newest exported session and shows how many matching sessions were found.

The page also reports whether seven days have elapsed. It deliberately allows an early or repeated test so a researcher can handle exceptional cases; every saved attempt is kept separately and the previous-attempt count is shown before starting.

## Required original materials

Each selected session needs its original `storySession.fullStory`, unique furniture `anchorId` values and readable `anchorLabel` values, and every item's original `sceneSnapshotPath` image. Missing materials prevent the test from starting, with an explicit error. The server does not generate a replacement story, substitute new furniture, or silently omit matching questions.

Keep the original PNG/JPEG files under `MemoryPalaceSessionData/<session folder>/SpatialSnapshots/`. On the original Windows PC, the server also checks the Unity project's LocalLow data folder. For moved data, preserve that session-folder structure and supply a trusted root:

```powershell
.\Start-DelayedTestWeb.ps1 -Port 8767 `
  -ExportsDir "D:\Study\ExperimentExports" `
  -SnapshotsDir "D:\Study\MemoryPalaceSessionData"
```

`-SnapshotsDir` accepts multiple roots; the Python equivalent is repeatable `--snapshots-dir`. Retrieved Quest data may use `QuestResultImports` instead. The resolver only serves session-matching files inside trusted roots; it never exposes an arbitrary filesystem path to the browser.

## Results

Results are written by default to:

```text
ExperimentExports/DelayedTests/
```

Each saved attempt produces:

- `delayed_<participant>_<session>_<time>_<unique-id>.json` — full responses, correctness, response times, schedule, and scores;
- a matching `.csv` — one row per learned word for analysis.

The first checkpoint is written **after the first typed word test, before revealing the story**. Story acknowledgement, completed matching, and the final typed word test update the same result pair. JSON uses `schemaVersion: 3`, `vocabularyTestFormat: typed_only`, `status: in_progress | complete`, and separate `baseline`, `storyReview`, `spatialMatching`, and `postReview` blocks. Only completed stages are available in an interrupted attempt.

The three scores are `baselineManualScore`, `matchingScore`, and `postManualScore`. For older analysis scripts, `manualScore` and the existing CSV `manual_*` columns refer to the **first** typed word test. `matching_*` and `post_manual_*` columns hold the other responses. Legacy `choice_*` / `post_choice_*` CSV columns are retained but blank; JSON `choiceScore` is `null`, not zero, and `choiceTestAdministered` is `false`. English-choice questions are not generated or accepted by an API. Existing schema-1 and schema-2 results are never rewritten; do not combine their earlier recognition-based protocols with v3 without accounting for that difference.

Story-review duration is the server-observed interval between returning the story and receiving Continue; it includes network latency and does not prove active reading. Matching response time starts after its image loads. A failed submission can be retried on the same page without duplicating the saved stage. Do not reload mid-test: active state is in memory, expires after eight hours, and is not resumed after a page reload or server restart. Saved checkpoints remain on disk.

These files, including the copied original story, are experiment data and remain ignored by Git with the rest of `ExperimentExports`. Do not share the result directory with participants.

## Researcher options

Use a different export or result folder:

```powershell
.\Start-DelayedTestWeb.ps1 -Port 8767 `
  -ExportsDir "D:\Study\ExperimentExports" `
  -ResultsDir "D:\Study\DelayedResults"
```

Let other devices on the same trusted local network connect:

```powershell
.\Start-DelayedTestWeb.ps1 -BindAddress 0.0.0.0 -Port 8767
```

Then use `http://<research-computer-IP>:8767/` from the participant device. Windows Firewall may ask for permission. This is LAN access, not an internet deployment.

## Privacy and deployment

Do not upload `ExperimentExports` to a public static host. The browser intentionally uses a server API so the initial typed round does not receive the correct answers, and raw exports stay off the participant's device. The story and matching images are delivered only at their permitted stages, with `no-store` headers. The server has no participant authentication: an ID is not a password. For remote participants, use HTTPS plus researcher-controlled access; never expose the bare HTTP service directly to the internet. The included server is intended for a local machine or trusted study network, not as a hardened public service.

## Web-only sharing without participant email

`Start-DelayedTestShare.cmd` / `share_link.py` starts a separate, loopback-only server on port 8768 and creates a Cloudflare HTTPS tunnel. The participant receives a complete `https://...trycloudflare.com/#access=...` invitation link; no participant email, account or software installation is required. Answers save to the research computer as in the ordinary web protocol. The original 8767 server is not stopped or publicly forwarded.

Prerequisites: Python 3 and the official Windows `cloudflared` executable, either on PATH or at `.utmp/cloudflared/cloudflared.exe`. Only run the official executable. The installed local runtime was obtained from Cloudflare's official GitHub release and verified against its SHA256 asset digest; it is not included in source packages.

From this tool directory:

```powershell
.\Start-DelayedTestShare.cmd --session <exact-session-id>
```

Or use Python directly:

```powershell
py -3 share_link.py --session <exact-session-id> --hours 24
```

The launcher asks for permission before Cloudflare relay begins. `--confirm-public-sharing` is reserved for an explicitly authorized non-interactive launch. It restricts lookup to the chosen exact session and that session's Participant ID; changing an ID cannot open another participant's export. Requests for APIs and snapshots require a valid invitation cookie. The random 256-bit bearer token in the URL fragment is exchanged for an HttpOnly, Secure, SameSite=Strict cookie and removed from the visible URL/history. Server logs omit query strings and secrets.

Keep the full original `#access=...` part when sending the link. Anyone holding the complete link can use it; this is possession-based access, not identity verification. Share it privately, not in a public channel. Requests expire after 24 hours by default (maximum seven days). The launcher shuts down its server and tunnel when it expires or when you press Ctrl+C. A restart generates a different URL/token and invalidates prior cookies; do not restart during a test.

The local `.utmp/delayed-share/<session>/invitation.json` file contains the invitation secret and process IDs. It remains ignored by Git. Do not upload it or a screenshot of the complete URL to public channels. Cloudflare relays the selected session's story, snapshots and responses, so obtain research approval for that data route before starting it. Quick Tunnels have no uptime guarantee and require the research computer to remain online and awake. Use an approved stable hosting arrangement for unattended or production deployments.

`share_server.py` is the underlying invitation server for local diagnostics. It does not create an external tunnel by itself. Do not expose the unprotected `server.py` as a substitute.

## Offline delivery (no email, account, server, or tunnel)

An alternative is a participant-specific ZIP containing one self-contained HTML file and English instructions. From this tool directory:

```powershell
py -3 offline_package.py export --id <exact-session-id>
```

The command prints a **SendToParticipant ZIP** and a separate **ResearcherOnly scoring key**, under the ignored `Builds/OfflineDelayedTests` directory. Send only the ZIP, privately to the matching participant. It includes only that session's Spanish targets, original story and furniture snapshots, not other sessions or the English scoring table / correct furniture-answer mapping.

The participant extracts the ZIP, opens `START_TEST.html` in a desktop browser, enters their ID, and completes the same four-stage sequence. No English multiple-choice round is added. The page uses embedded images and scripts with network connections disabled by its Content Security Policy. At the end they must click **Download result JSON**, check the downloaded file, and return it to the researcher. Nothing is uploaded or saved to your computer automatically. Keep the page open until the file has downloaded; reload/crash recovery is not implemented. The offline completion page says **Recorded**, not a fabricated score.

Grade the returned JSON on the researcher's computer:

```powershell
py -3 offline_package.py grade --key <private-key-json> --result <returned-json>
```

Graded JSON and CSV go to `ExperimentExports/DelayedTests` by default, with separate `offline_graded_` filenames. Raw returned files and original study exports are not changed. Package/session IDs and response sets are checked before grading. Use `--output-dir` to select another result folder.

Offline is a different delivery mode, not a secure web link. Story and image bytes must be included and can be inspected ahead of time by someone examining the source. It has no identity verification, tamper-proofing, expiry or trusted server timestamps. Results explicitly retain `deliveryMode: offline` and `clientReportedTimestamps: true`; account for these differences before combining them with server-gated testing. Do not distribute the `ResearcherOnly` directory.

## Command-line options

```text
python server.py --host 127.0.0.1 --port 8767
                 --exports-dir <folder> --results-dir <folder>
                 --snapshots-dir <trusted-original-snapshot-root>
```

The implementation uses only the Python standard library; no package installation is required. Run its synthetic-data tests from the project root:

```powershell
py -3 -m unittest discover -s Tools/DelayedTestWeb/tests -v
```

Optional code-level frontend regression test (requires Node.js):

```powershell
node Tools/DelayedTestWeb/tests/test_frontend.js
```
