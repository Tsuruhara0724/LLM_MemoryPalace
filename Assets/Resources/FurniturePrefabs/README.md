# Furniture Prefab Replacement

Put replacement furniture prefabs here using the same file name as the room anchor `modelKey`.

Examples:

- `bed.prefab`
- `wardrobe.prefab`
- `desk.prefab`
- `chair.prefab`
- `bookshelf.prefab`
- `bathtub.prefab`
- `sofa.prefab`
- `television.prefab`
- `air_conditioner.prefab`
- `window.prefab`
- `door.prefab`
- `lamp.prefab`
- `plant.prefab`
- `toilet.prefab`

If a prefab is missing, the runtime uses the built-in low-poly procedural model.

Recommended prefab convention:

- Front faces local `-Z`, matching the current low-poly fallback.
- Keep any source-axis correction and import-unit conversion on the prefab root; runtime preserves its rotation and scale.
- Scene position saved on the prefab root is ignored.
- Runtime uses the prefab root's authored rotation and scale without automatic resizing.
- Runtime aligns floor furniture to the floor and wall furniture to the wall without changing its proportions.
- Adjust each replacement's uniform size in the Prefab Inspector if needed.

Current proportional baseline (Unity units are treated consistently; approximate visible size is shown only for comparison):

| Prefab | Uniform root Scale | Approx. visible size X x Y x Z |
| --- | ---: | ---: |
| Door | `2.88` | `1.00 x 2.10 x 0.19` |
| Bed | `0.385` | `1.74 x 0.67 x 1.99` |
| Wardrobe | `0.30345` | `1.30 x 1.70 x 0.60` |
| Desk | `0.6664` | `1.76 x 1.05 x 0.87` |
| Chair | `0.471` | `0.71 x 0.90 x 0.53` |
| Bookshelf | `0.354` | `0.86 x 1.98 x 0.69` |
| Bathtub | `0.9975` | `2.98 x 1.03 x 1.35` |
| Sofa | `0.59265` | `2.67 x 1.08 x 1.23` |
| Television | `59.14` | `2.40 x 1.42 x 0.24` |
| Air Conditioner | `0.18` | `1.25 x 0.32 x 0.28` |
| Window | `0.70` | `1.06 x 1.11 x 0.06` |
| Floor Lamp | `106.6625` | `0.48 x 1.81 x 0.48` |
| Plant | `0.008181` | `0.80 x 1.49 x 0.82` |
| Toilet | `0.84` | `0.71 x 1.58 x 1.25` |
