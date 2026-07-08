# Formal Material Workflow

Last verified: 2026-07-08

The old 12 x 10 mnemonic catalog and 480-image anchor-pair catalog are not used by the active continuous-story design.

## Formal word material

- Source set: `formal_12_pool` in `Assets/Resources/MemPalaceDemoData.json`
- Pool size: 12 distinct Spanish nouns
- Session sample: 8 distinct words
- Story source in the LLM condition: one live Ollama or Gemini continuous story
- Visual source: one fixed local image per Spanish word
- Study protocol: independent room study for 20 minutes
- Comparison conditions: `LLM Story + Own Order`, `Write Story + Own Order`, and `Empty Room`
- Spatial-order procedure for both story conditions: before Study, the participant clicks actual furniture models on PC and chooses one unused word from a 2 x 4 card; doors and windows are not selectable
- Post-study option in both story conditions: reveal every furniture word-picture UI simultaneously in the room, with participant-controlled entry and finish and no countdown
- Empty-room baseline: room shell only, with no furniture, words, pictures, story, narration, or picture-dependent tests

Formal pool:

```text
estrella, espejo, castillo, máscara, vela, tambor,
nube, campana, linterna, flor, corona, barco
```

## Image preparation

1. Store each image at `Assets/Resources/WordImages/{word}.png`.
2. Keep the filename lower-case and identical to the Spanish `word` value.
3. Prefer one unmistakable object/concept with little visual clutter.
4. Do not include the Spanish word or English answer as visible text.
5. Check the image in Unity at the actual study-panel and world-space sizes.
6. Update `Assets/Resources/WordImages/ATTRIBUTION.md` whenever a source changes.
7. Confirm all selected words avoid `_placeholder.png` before a participant run.

Current formal coverage is 12/12. Current whole-preset coverage is 26/26.

## Vocabulary selection rules

- Prefer basic-level, everyday nouns: `pan` is acceptable; a named subtype such as `croissant` or a regional bread variety is not.
- Avoid a narrow taxonomic subtype when a common parent category is the intended concept.
- Avoid forms whose meaning changes only through grammatical gender, especially when the app presents the bare noun without an article.
- Preserve required written accents, for example `máscara` rather than `mascara`.
- Natural person-gender pairs such as `vecino / vecina` are acceptable when both mean the same role.
- Record every accepted word, article, grammatical gender, English meaning, and ambiguity note in `Docs/VOCABULARY_AUDIT.md`.

## Story review

Before freezing a formal session story, confirm:

- all eight selected `meaning (Spanish word)` pairs appear;
- each selected word creates exactly one route item;
- the story has one continuous premise rather than isolated object scenes;
- no furniture/anchor names leak into the story;
- the participant's furniture mapping does not change story order;
- no missing-word repair sentence makes the ending feel artificial;
- local fallback material is not being used.

## Pre-participant validation

- Run one complete session with the exact word set and room.
- Time the independent study period for 20 minutes.
- Verify Desktop or OpenXR controls on the study machine.
- Verify ElevenLabs voice output, route order, correct-anchor gating, story-segment completion, replay, and restart controls with the formal-session Voice ID.
- Verify that the first spoken pass cannot be skipped, then verify segment-start route jumping from the whole-route Desktop/VR progress bar.
- For both story conditions, verify one-to-one furniture assignment through actual model clicks and a 2 x 4 word card; verify that only spatial mapping changes.
- For the participant-written condition, verify eight segments are exported as one continuous story and narrated by the normal Study route.
- For the empty-room condition, verify no furniture, word-picture UI, story text, subtitle, route progress, or voice is present.
- Verify enter/finish/skip behavior for the optional post-Study all-picture room display.
- Complete the configured post-study assessment.
- Export JSON and CSV and inspect participant ID, story, word order, anchors, study duration, responses, and questionnaire values.
