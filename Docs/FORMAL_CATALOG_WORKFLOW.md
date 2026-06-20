# Formal Catalog Workflow

Last verified: 2026-06-20

Formal experiment assets use:

- 12 fixed vocabulary words.
- 10 fixed anchor types: `door`, `bed`, `desk`, `chair`, `table`, `sofa`, `wardrobe`, `bookshelf`, `air_conditioner`, `television`.
- 120 reviewed mnemonic text entries in `Assets/Resources/PreGeneratedMnemonics.json`.
- 480 reviewed image cue files, four per word-anchor pair, in `Assets/Resources/PreGeneratedImageCueCatalog.json`.

## Mnemonic Text Review

1. In Setup, enable `Show Mnemonic Catalog Review Builder`.
2. Use `Export Missing Draft JSON` to export only missing 12 x 10 pairs, or `Export Full Review JSON` to re-review all 120 pairs.
3. Paste the JSON into GPT-5.5 or another quality-first model.
4. Ask the model to return strict JSON with the same `items` array and no Markdown fences.
5. Paste the reviewed JSON back into the Unity text box.
6. Click `Import Reviewed Mnemonics`.
7. Use `Check 12 x 10 Mnemonic Matrix` and `Validate Catalog` before generating images.

The import path writes valid reviewed items into `Assets/Resources/PreGeneratedMnemonics.json` by word plus normalized `anchorType`.

## Review Rules

- Use `STORY_ONLY` unless a natural hook is genuinely strong.
- Use `HOOK_PLUS_STORY` only when `hookAccepted` is true and `hookScore >= 7`.
- The learner-facing `mnemonic` should be short and easy to imagine.
- The `associationPrompt`, `imagePrompt`, and `imagePromptCandidates` must clearly show both the target meaning object/action and the assigned anchor object.
- Image prompts must avoid text, logos, abstract icons, translations, and meta wording such as learner, mnemonic, remember, or Spanish word.
