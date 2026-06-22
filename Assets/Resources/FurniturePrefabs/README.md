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
- `door.prefab`

If a prefab is missing, the runtime uses the built-in low-poly procedural model.

Recommended prefab convention:

- Pivot at the bottom center of the furniture.
- Front faces local `-Z`, matching the current low-poly fallback.
- Model authored around a 1x1x1 local bounding box; the room anchor scale is applied at runtime.
