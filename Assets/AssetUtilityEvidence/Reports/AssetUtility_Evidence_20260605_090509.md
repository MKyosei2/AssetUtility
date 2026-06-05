# AssetUtility Evidence Pack

Generated: 2026-06-05 09:05:09

## Summary

- Result: **PASS**
- Method: AssetOptimizer.Simplify(GPU preferred / CPU fallback) -> validation-safe grid reduction
- Scene: `Assets/AssetUtilityEvidence/Scenes/AssetUtilityEvidence_20260605_090509.unity`
- Source Mesh: `Assets/AssetUtilityEvidence/Meshes/AU_Evidence_HighPolyGrid.asset`
- Optimized Mesh: `Assets/AssetUtilityEvidence/Meshes/AU_Evidence_OptimizedGrid_6400.asset`
- Texture: `Assets/AssetUtilityEvidence/Textures/AU_Evidence_Checker_1024.png`

## Before / After Metrics

| Item | Before | Target | After |
|---|---:|---:|---:|
| Triangles | 12800 | 6400 | 6400 |
| Vertices | 6561 | - | 3321 |
| Reduction | 0% | 50% | 50.0% |
| Texture Max Size | 1024 | 512 | 512 |

## Mesh Safety Checks

| Check | Before | After | Result |
|---|---:|---:|---|
| Boundary Edges | 320 | 240 | PASS |
| Non-Manifold Edges | 0 | 0 | PASS |
| Degenerate Triangles | 0 | 0 | PASS |
| Invalid Indices | 0 | 0 | PASS |
| Isolated Vertices | 0 | 0 | PASS |

## Screenshots

- Before: ``
- After: ``
- Comparison: ``

## Portfolio Notes

- このEvidence Packは、平面グリッドの50%削減で内部穴が発生していないかを境界エッジ数・非多様体・退化三角形で検証します。
- Before / After の数値、比較Scene、PNGスクリーンショット、CSVを同時に出力します。
- Console Error 0 の最終確認はUnity上で実行後、ConsoleをClearしてからEvidence Pack生成までエラーが出ないことをスクリーンショットで残してください。
