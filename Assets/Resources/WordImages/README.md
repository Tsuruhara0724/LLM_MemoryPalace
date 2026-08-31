# Word Images

The runtime loads one local image per Spanish target word with:

`Resources.Load<Texture2D>("WordImages/{word}")`

Use the lower-case Spanish word as the filename, for example `estrella.png`.
If a file is missing, `_placeholder.png` is used.

Current formal-pool coverage:

- 10/32 words in `formal_32_pool`
- The 22 newly added candidate images are pending manual addition

Most current images are OpenMoji icons downloaded from the internet. Several files
have since been replaced locally; their source/license status is tracked separately.
They are initial study assets and may be replaced by dropping a new PNG at the same path.

See `ATTRIBUTION.md` for source URLs and licenses.
