# One-week delayed vocabulary test

This small local website reads the existing Unity experiment exports and runs two delayed-test rounds:

1. The participant sees the learned Spanish word and types its English meaning. Common inflectional changes are accepted, such as `cracks` for `crack` and `searching` for `search`.
2. The participant sees the same learned words again and chooses one English meaning from three options, like the post-test.

Round 1 does not reveal correctness or English meanings before Round 2. After both rounds, a complete JSON file and a one-row-per-word CSV file are saved.

## Start locally

From this folder in PowerShell:

```powershell
.\Start-DelayedTestWeb.ps1
```

Or double-click `Start-DelayedTestWeb.cmd`. Then open:

```text
http://127.0.0.1:8765/
```

The default input folder is the repository's `ExperimentExports` directory. The participant can enter either:

- an exact `sessionId`, which selects that experiment session; or
- a `participantId`, which selects that participant's newest exported session and shows how many matching sessions were found.

The page also reports whether seven days have elapsed. It deliberately allows an early or repeated test so a researcher can handle exceptional cases; every attempt is saved separately and the previous-attempt count is shown before starting.

## Results

Results are written by default to:

```text
ExperimentExports/DelayedTests/
```

Each completed attempt produces:

- `delayed_<participant>_<session>_<time>.json` — full responses, correctness, response times, schedule, and scores;
- a matching `.csv` — one row per learned word for analysis.

These files are experiment data and remain ignored by Git with the rest of `ExperimentExports`.

## Researcher options

Use a different export or result folder:

```powershell
.\Start-DelayedTestWeb.ps1 `
  -ExportsDir "D:\Study\ExperimentExports" `
  -ResultsDir "D:\Study\DelayedResults"
```

Let other devices on the same trusted local network connect:

```powershell
.\Start-DelayedTestWeb.ps1 -BindAddress 0.0.0.0 -Port 8765
```

Then use `http://<research-computer-IP>:8765/` from the participant device. Windows Firewall may ask for permission.

## Privacy and deployment

Do not upload `ExperimentExports` to a public static host. The browser UI intentionally uses a server API so Round 1 does not receive the correct answers, and so raw exports stay off the participant's device. If this must be internet-accessible, put the server behind HTTPS and researcher-controlled access, and use pseudonymous IDs. The included server is intended for a local machine or trusted study network, not as a hardened public service.

## Command-line options

```text
python server.py --host 127.0.0.1 --port 8765
                 --exports-dir <folder> --results-dir <folder>
```

The implementation uses only the Python standard library; no package installation is required.
