# AssetUtility Before / After Report

- Generated: 2026-06-06 17:25:24
- Objects: 5
- Triangles: 25602 → planned 6404 → after 6402
- Texture VRAM: 1.00 MB → planned 1.00 MB → after 1.00 MB

## Changed Objects

| Object | Scene | Poly Before | Poly Planned | Poly After | Texture Before | Texture Planned | Texture After | Warning |
|---|---|---:|---:|---:|---|---|---|---|
| Main Camera | AssetUtilityDemoScene | 0 | 1 | 0 | — | — | — | 静的Mesh対象外; MeshPathなし; Materialなし; Textureなし |
| Directional Light | AssetUtilityDemoScene | 0 | 1 | 0 | — | — | — | 静的Mesh対象外; MeshPathなし; Materialなし; Textureなし |
| AU_HighPoly_StaticMesh | AssetUtilityDemoScene | 12800 | 3200 | 3200 | — | — | — | Materialなし; Textureなし |
| AU_MeshCollider_SharedMesh | AssetUtilityDemoScene | 12800 | 3200 | 3200 | — | — | — | Materialなし; Textureなし |

## Apply Log

- 🟢 Texture: AU_CheckerTexture.png: maxSize 2048 → 512
- 🟢 AU_HighPoly_StaticMesh: 12800 → 3200 tris（GPU優先）
- 🟢 AU_MeshCollider_SharedMesh: 12800 → 3200 tris（GPU優先）
