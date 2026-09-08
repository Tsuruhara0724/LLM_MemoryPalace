# Current 2 × 2 Experimental Flow

Updated: 2026-07-31

This file is the authoritative runtime flow for the Unity experiment. The factors are:

- Room source: participant-created familiar room vs fixed default room
- Story source: participant-written story vs selected LLM-generated story

## Conditions

| ID | Room phase | Story phase |
|---|---|---|
| 1 | Self-room: create a familiar room on PC | Self-story: write and classify one continuous story |
| 2 | Self-room: create a familiar room on PC | LLM-story: choose one of three complete generated stories |
| 3 | Default-room: explore the fixed room on PC | Self-story: write and classify one continuous story |
| 4 | Default-room: explore the fixed room on PC | LLM-story: choose one of three complete generated stories |

## Shared order

1. PC room phase — target 10 minutes.
2. Spanish-to-English pre-test — maximum 5 minutes, with no correctness feedback.
3. Story phase — target 10 minutes.
4. Furniture-word mapping on PC — target 3 minutes.
5. TTS audio preparation — actual service time is recorded through event logs.
6. HMD memory-palace study — the immediate post-test unlocks after 10 minutes.
7. Immediate Spanish-to-English post-test — maximum 5 minutes.
8. Questionnaire — target 5 minutes.
9. JSON and CSV export.

The displayed target timers for room creation/exploration, story authoring/selection,
furniture mapping, and questionnaire do not hard-lock the researcher. The pre-test and
post-test have five-minute timeouts. The HMD study has a hard ten-minute minimum.

## Exported factors and measures

JSON exports include:

- `condition`, `conditionLabel`, `roomSource`, and `storySource`
- actual duration of every experimental phase
- every pre-test answer, correctness value, and response time
- all LLM story candidates and the selected candidate index
- the selected story and furniture-word mapping
- immediate post-test responses
- questionnaire answers and interaction logs

CSV exports contain both `pre_test` and `final_word_meaning` rows so the vocabulary
outcome can be analyzed as pre/post performance under the same participant/session ID.
