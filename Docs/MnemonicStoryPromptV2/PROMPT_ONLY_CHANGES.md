# MSF-2.1 Prompt-only revisions

Source: user-supplied `MSF-2.1_prompt_only_compatible.zip` and [shared design conversation](https://chatgpt.com/share/6ad98fd4-7f58-83ee-b850-ac00baebee55), integrated 2026-10-22.

The eight files in `prompts/` match the supplied revision. The three files in `schemas/` and `examples/writer.examples.json` remain unchanged. Documentation is updated locally to describe actual integration status; the archive's preserved MSF-2.0 READMEs were not copied over newer status notes.

## Prompt changes

- Semantic accuracy before plot mechanics: preserve each supplied word and meaning.
- Bind the exact word–meaning phrase to one distinct action, state or consequence in the same sentence.
- Require meaningful event participation, not physical indispensability for every target.
- Use plausible event-to-event handoffs; matching cue strings alone is not semantic proof.
- Preserve natural English grammar, including when a Spanish target is a verb or abstract concept.
- Keep spatial assignment independent; do not add furniture bindings, phonetic hooks or study instructions.
- Audit semantics first; repair documented failures with minimal local changes.

## Version compatibility

`MSF-2.1` names the **prompt content policy**, not a new serialized schema. Expand `{{FRAMEWORK_VERSION}}` as `MSF-2.0`: all three existing schemas enforce that constant. Score names, error enums, placeholders, examples and JSON structures are unchanged.

## Unity runtime adapter

`Assets/Scripts/Services/MnemonicStoryPrompts.cs` applies the policy through the existing generation flow; Unity does not load the Docs templates. `OllamaLlmService` delegates its prompt builders and shares the system instructions across provider paths.

Preserved runtime contracts:

- One plan (`goal`, `items[word, need, action, result, goal_link]`), one writer call, at most one repair.
- Final output remains `fullStory` plus `items[word, storyOrder]`; Unity derives segments as before.
- Existing 160–210 word budget for eight targets, temperatures and token limits.
- Existing models, experiment conditions, participant-authoring flow, spatial assignments, TTS and exports.

The runtime does **not** now implement four-plan overgeneration, profile ranking, independent Audit calls or the richer `MSF-2.0` story schema. Writer self-check instructions are not an independent semantic validator. Existing local checks cannot guarantee meaning correctness or memory benefits.

## Engineering checks

Use Unity's `Tools > Memory Palace > Check Story Prompt Compatibility` menu, or batch entry point `MemPalaceLLM.Editor.MnemonicStoryPromptChecks.RunBatch` when the project is not already open. The checks exercise actual prompt builders, JSON data escaping, per-request delimiters, fallback semantics and legacy parser/TTS sentence compatibility. They make no network calls and measure no human learning outcomes.

Generated check artifacts are stored under ignored `.utmp/`. Compilation checks and any small generation probes should be reported separately from participant recall results; neither establishes that this policy outperforms MSF-2.0.

2026-10-22: full Runtime/Editor compilation succeeded, and 56 compatibility checks passed in a minimal isolated Unity project using actual source files. One mixed-eight-target sample required manually flagged semantic/grammar repairs; the repaired sample had 199 words, eight sentences and exact coverage 8/8. This is not evidence of autonomous semantic validation or learning gains. See `VERIFICATION_2026-10-22.md` for the original sample, parameters and limitations.
