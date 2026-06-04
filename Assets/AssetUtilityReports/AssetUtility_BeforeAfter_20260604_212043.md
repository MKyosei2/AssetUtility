# AssetUtility Before / After Report

- Generated: 2026-06-04 21:20:43
- Objects: 5
- Triangles: 25602 → planned 12804 → after 12802
- Texture VRAM: 1.00 MB → planned 1.00 MB → after 1.00 MB

## Changed Objects

| Object | Scene | Poly Before | Poly Planned | Poly After | Texture Before | Texture Planned | Texture After | Warning |
|---|---|---:|---:|---:|---|---|---|---|
| Main Camera | AssetUtilityDemoScene | 0 | 1 | 0 | — | — | — | 静的Mesh対象外; MeshPathなし; Materialなし; Textureなし |
| Directional Light | AssetUtilityDemoScene | 0 | 1 | 0 | — | — | — | 静的Mesh対象外; MeshPathなし; Materialなし; Textureなし |
| AU_HighPoly_StaticMesh | AssetUtilityDemoScene | 12800 | 6400 | 6400 | — | — | — | Materialなし; Textureなし |
| AU_MeshCollider_SharedMesh | AssetUtilityDemoScene | 12800 | 6400 | 6400 | — | — | — | Materialなし; Textureなし |

## Apply Log

- 🟢 AU_HighPoly_StaticMesh: 12800 → 6400 tris（GPU優先）
- 🟢 AU_MeshCollider_SharedMesh: 12800 → 6400 tris（GPU優先）
