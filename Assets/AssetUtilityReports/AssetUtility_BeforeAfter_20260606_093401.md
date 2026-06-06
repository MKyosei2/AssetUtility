# AssetUtility Before / After Report

- Generated: 2026-06-06 09:34:01
- Objects: 6
- Triangles: 19200 → planned 9604 → after 9600
- Texture VRAM: 0.00 MB → planned 0.00 MB → after 0.00 MB

## Changed Objects

| Object | Scene | Poly Before | Poly Planned | Poly After | Texture Before | Texture Planned | Texture After | Warning |
|---|---|---:|---:|---:|---|---|---|---|
| Main Camera | AssetUtilityEvidence_20260605_090515 | 0 | 1 | 0 | — | — | — | 静的Mesh対象外; MeshPathなし; Materialなし; Textureなし |
| Directional Light | AssetUtilityEvidence_20260605_090515 | 0 | 1 | 0 | — | — | — | 静的Mesh対象外; MeshPathなし; Materialなし; Textureなし |
| AU_Before_12800tris | AssetUtilityEvidence_20260605_090515 | 12800 | 6400 | 6400 | — | — | — | Materialなし; Textureなし |
| AU_After_6400tris_NoHoles | AssetUtilityEvidence_20260605_090515 | 6400 | 3200 | 3200 | — | — | — | Materialなし; Textureなし |
| Before: 12,800 tris / Texture 1024 | AssetUtilityEvidence_20260605_090515 | 0 | 1 | 0 | — | — | — | 静的Mesh対象外; MeshPathなし; Materialなし; Textureなし |
| After: 6,400 tris / Texture 512 / No holes | AssetUtilityEvidence_20260605_090515 | 0 | 1 | 0 | — | — | — | 静的Mesh対象外; MeshPathなし; Materialなし; Textureなし |

## Apply Log

- 🟢 AU_Before_12800tris: 12800 → 6400 tris（GPU優先）
- 🟢 AU_After_6400tris_NoHoles: 6400 → 3200 tris（GPU優先）
