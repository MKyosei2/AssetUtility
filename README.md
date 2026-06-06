# AssetUtility

**AssetUtility** は、Unity シーン内の Mesh、Texture、Material をスキャンし、コストを確認しながら、安全に最適化するための **Unity Editor アセットQA / 最適化ツール** です。

このリポジトリは、**Technical Artist / Tools Programmer 向けのポートフォリオ作品** として設計しています。Unity プロジェクトでは、どのアセットが重いのか、どこまで削減できるのか、最適化後に参照が壊れていないかを、アーティストとエンジニアが確認できる必要があります。AssetUtility は、その確認と最適化を Editor 上のワークフローとしてまとめることを目的にしています。

> スコープ注記: AssetUtility は Unity Editor 上の asset QA / optimization workflow です。商用の万能メッシュ最適化ツールの完全な代替ではありません。主張できる範囲は、実用的な Editor tooling、レビュー可能な最適化判断、参照維持、安全な適用フロー、performance-aware な実装です。

---

## ポートフォリオ要約

```text
Unity Scene / GameObject
  -> hierarchy を scan
  -> Mesh / Texture / Material metadata を収集
  -> editable asset table に表示
  -> texture resolution / target triangle count を編集
  -> AssetUtilityData に edit plan を保存
  -> apply changes
  -> optimized mesh または texture output を生成
  -> MeshFilter / SkinnedMeshRenderer / MeshCollider references を置換
  -> before / after report を出力
```

このツールは **inspect before optimize / 最適化する前に確認する** という考え方で作っています。変更前に object type、scene、hierarchy path、material、polygon count、texture resolution、estimated VRAM、warning を表示し、最適化判断をレビュー可能にします。

---

## 課題 / 利用者 / 出力

| 項目 | 内容 |
|---|---|
| 課題 | シーン内アセットは重くなりやすい一方、盲目的な最適化は参照破壊や見た目の劣化を起こします。 |
| 主な利用者 | Technical Artist、Tools Programmer、Environment Artist、Unity Developer。 |
| 入力 | Active Scene、All Scenes、または選択した GameObject。 |
| 出力 | Asset table、optimization plan、optimized mesh assets、texture output / importer target、updated references、before / after report。 |
| 安全性の方針 | 先にレビューし、名前だけで対象を決めず、参照を維持し、source mesh を直接破壊しないこと。 |

---

## レビュー手順

短時間で確認する場合は、以下の順番を推奨します。

```text
1. Unity project を開く。
2. Tools > Asset Utility を実行する。
3. Active Scene または Single Object を scan する。
4. All / Texture / Material / Polygon view を確認する。
5. target texture size または target polygon count を編集する。
6. Apply changes を実行する。
7. 生成された optimized mesh asset と before / after report を確認する。
8. Assets/Script/AssetUtility.cs を確認する。
```

確認対象として重要なファイル:

```text
Assets/Script/AssetUtility.cs
Assets/Editor/Resources/AssetUtilityData.asset
Assets/OptimizedMeshes/        # optimization 適用時の generated output folder
```

---

## メインワークフロー

```text
Open Asset Utility
  -> scan mode を選択
  -> scene または selected object を scan
  -> asset table を確認
  -> texture / polygon target を編集
  -> edits を apply
  -> output を validate
  -> generated mesh assets を保存
  -> scene / prefab references を更新
```

### Scan mode

| Mode | 目的 |
|---|---|
| Active Scene | 現在開いている scene を scan します。 |
| All Scenes | project scenes を広く確認します。 |
| Single Object | 1つの GameObject を対象に直接最適化します。 |

### View mode

| View | 目的 |
|---|---|
| All | Object、material、polygon、texture の summary を表示します。 |
| Texture | Texture name、type、resolution、estimated VRAM、target resolution を表示します。 |
| Material | Material name と material classification を表示します。 |
| Polygon | current / target triangle count と reduction view を表示します。 |

---

## 実装済み機能

| 領域 | 状態 | 内容 |
|---|---:|---|
| Unity EditorWindow workflow | 実装済み | `Tools > Asset Utility` から起動します。 |
| Scene / object scan | 実装済み | mesh、texture、material、scene、hierarchy 情報を収集します。 |
| Asset QA table | 実装済み | asset cost と editable target を表示します。 |
| Persistent edit plan | 実装済み | ScriptableObject 系の edit data で計画を保持します。 |
| Texture metadata | 実装済み | texture path、type、resolution、estimated VRAM を扱います。 |
| Mesh metadata | 実装済み | mesh path、polygon count、target count、editability を扱います。 |
| Mesh simplification | 実装済み | shape-preserving QEM path と fallback behavior を持ちます。 |
| Texture resize path | 実装済み | 実験用に GPU conversion / PNG output path を持ちます。 |
| Reference replacement | 実装済み | MeshFilter、SkinnedMeshRenderer、MeshCollider references を更新します。 |
| Scene / prefab dirty handling | 実装済み | 必要に応じて scene / prefab instance を dirty にします。 |
| Before / after report | 実装済み | original target、result、processing information を記録します。 |
| QEM priority queue refactor | 計画中 | 現在の hot spot は repeated full edge / triangle scan です。 |
| Automated editor tests | 計画中 | regression confidence を上げるための今後の課題です。 |

---

## Data model

### `AssetInfo`

`AssetInfo` は Editor table の row model です。

| Field | 意味 |
|---|---|
| `instanceID` | Unity instance ID。 |
| `name` | Object name。 |
| `scene` / `scenePath` | Scene identity。 |
| `objectPath` | 同名 object の誤適用を避けるための hierarchy path。 |
| `objectType` | Mesh / skinned mesh / collider-style classification。 |
| `materialType` / `materialName` | Material metadata。 |
| `polygonCount` / `targetPolygonCount` | 現在と目標の triangle count。 |
| `meshPath` | Source mesh asset path。 |
| `textureName` / `texturePath` | Texture metadata。 |
| `textureType` | Texture classification。 |
| `vramMB` | 推定 texture memory。 |
| `resolution` / `targetResolution` | 現在と目標の texture size。 |

### Edit records

| Type | 目的 |
|---|---|
| `TextureEditData` | texture resize / max-size edit plan を保存します。 |
| `MeshEditData` | mesh optimization target と generated asset path を保存します。 |
| `MaterialEditData` | material 関連の edit plan を保存します。 |
| `AssetUtilityData` | 現在の edit plan を保持します。 |

---

## Mesh optimization architecture

AssetUtility は、品質を優先しつつ fallback できるように複数の optimization path を持ちます。

```text
Target Mesh
  -> optional subdivision / smoothing
  -> Shape-Preserving QEM path
  -> GPU simplification path
  -> CPU fallback path
  -> validation
  -> optimized Mesh asset として保存
  -> scene / prefab references を置換
```

### Shape-Preserving QEM options

| Option | 目的 |
|---|---|
| `preserveBorders` | border edge の collapse を避けます。 |
| `preserveUVSeams` | 可能な範囲で UV seam の破壊を避けます。 |
| `preserveHardNormals` | hard normal boundary を保持します。 |
| `preventNonManifold` | non-manifold collapse を避けます。 |
| `maxPositionError` | position drift を制限します。 |
| `maxNormalDeviation` | normal direction change を制限します。 |
| `minTriangleArea` | degenerate / tiny triangle を避けます。 |
| `uvWeight` | UV difference penalty を追加します。 |
| `normalWeight` | normal difference penalty を追加します。 |
| `edgeLengthClamp` | 長すぎる collapse を避けます。 |
| `snapToLocalSurface` | collapsed vertex を local surface に寄せます。 |
| `sliverAspectMin` | sliver triangle の生成を抑えます。 |
| `curvatureWeight` | high-curvature region を潰しにくくします。 |
| `compactOnFinish` | simplification 後に unused vertices を削除します。 |
| `recomputeQuadricsLocally` | collapse 後に local QEM rebuild を行う optional path です。 |

---

## Safety / validation design

AssetUtility は、Editor tool で起こりやすい事故を避けるため、次の設計を重視しています。

- object name だけでなく scene path / object path で対象を識別する。
- MeshFilter references を更新する。
- SkinnedMeshRenderer references を更新する。
- MeshCollider references を更新する。
- source mesh を直接 mutate せず、generated mesh asset として保存する。
- edit records を persistent data に残す。
- 必要に応じて scene / prefab instance を dirty にする。
- before / after report を生成する。

---

## 現在の performance status

現在の QEM implementation は機能しますが、重要な高速化余地があります。

Known hot spots:

```text
Current QEM path
  -> simplification iteration ごとに全 candidate edge を探索
  -> collapse 後に全 triangle indices を scan
  -> loop 中に degenerate triangles を削除
  -> edge / border data を繰り返し rebuild する場合がある
```

Planned performance refactor:

```text
PriorityQueue<EdgeCandidate>
  + vertex -> triangle adjacency cache
  + edge -> triangle adjacency cache
  + triangle valid flags
  + delayed compaction
  + local edge candidate refresh
```

実装後に出したい portfolio evidence:

```text
Input mesh:      50k / 100k triangles
Before:          full edge scan + full triangle rewrite
After:           priority queue + adjacency cache
Measured result: xxx ms -> yyy ms
Output report:   Docs/Reports/qem_benchmark_YYYY-MM-DD.md
```

---

## Texture optimization workflow

Texture view では memory を推定し、resolution edit を行えます。実運用では、可能な限り importer settings を使う方が安全です。

Recommended production-oriented path:

```text
TextureImporter.maxTextureSize
  -> compression / mipmap / sRGB setting review
  -> SaveAndReimport
  -> before / after report
```

GPU conversion と PNG output による direct resize path もありますが、実際の Unity project では importer-setting change の方が安全です。

---

## 現在の architecture と refactor target

現在の主要ファイル:

```text
Assets/Script/AssetUtility.cs
Assets/Editor/Resources/AssetUtilityData.asset
Assets/OptimizedMeshes/
```

より review しやすい構成にするための refactor target:

```text
Assets/AssetUtility/
  Editor/
    AssetUtilityWindow.cs
    AssetScanController.cs
    AssetOptimizationController.cs
    AssetUtilityReportWriter.cs

  Core/
    Models/
      AssetInfo.cs
      MeshEditData.cs
      TextureEditData.cs

    Optimization/
      QemMeshSimplifier.cs
      QemSimplifyOptions.cs
      MeshReferenceReplacer.cs
      TextureImportOptimizer.cs

  Tests/
    Editor/
      AssetScanTests.cs
      MeshSimplificationTests.cs
      ReferenceReplacementTests.cs
```

現在の実装は、最終的な package architecture よりも、prototype speed と feature proof を優先した状態です。

---

## 実行方法

Unity project を開き、以下を実行します。

```text
Tools > Asset Utility
```

Manual validation の例:

```text
1. test scene を開く。
2. Asset Utility を起動する。
3. Active Scene を scan する。
4. 1つの mesh target triangle count を変更する。
5. Apply する。
6. generated mesh asset が存在することを確認する。
7. MeshFilter / SkinnedMeshRenderer / MeshCollider references が更新されていることを確認する。
8. report に original count、target count、final count、processing time が記録されていることを確認する。
```

---

## Recommended benchmark report format

```text
Environment
- Unity version:
- OS:
- CPU:
- GPU:
- Input mesh:
- Original triangles:
- Target triangles:

Result
- Method:
- Time before:
- Time after:
- Final triangles:
- Boundary edge increase:
- Degenerate triangles:
- Visual result:
```

---

## 現在の制限

- QEM performance は priority queue + adjacency cache refactor が必要です。
- Texture optimization は、より安全な実運用向けに `TextureImporter` settings 中心へ拡張する必要があります。
- Multi-scene scanning は大規模 project での追加検証が必要です。
- Complex skinned mesh cases には追加 sample が必要です。
- Benchmark reports は `Docs/Reports/` に生成・commit するとレビューしやすくなります。
- Automated Unity Editor tests はまだ十分ではありません。
- 現在の code は、より小さな review-friendly modules に分割する余地があります。

---

## 次の改善

### Short term

- priority queue + adjacency cache QEM refactor を実装する。
- loop 中の triangle removal を triangle valid flags + final compaction に置き換える。
- QEM benchmark report を before / after timings 付きで追加する。
- before / after screenshot または GIF を追加する。
- sample scene と high-poly mesh fixture を追加する。
- core models、QEM simplifier、reference replacement、report writer を別 file に分割する。

### Mid term

- TextureImporter-based platform override workflow。
- scene-wide optimization 向け batch report export。
- automated editor validation tests。
- safer prefab editing workflow。
- 可能であれば CI compile check。

### Long term

- MAYAtoUnity import reports との integration。
- project-wide asset budget dashboard。
- per-platform optimization profiles。
- team-friendly QA report export。

---

## ポートフォリオ用説明文

> Unity Editor 上で scene / selected object を scan し、mesh / texture / material cost を editable table として表示する Asset QA / optimization tool を開発しました。optimization plan を保存し、mesh / texture changes を適用し、MeshFilter / SkinnedMeshRenderer / MeshCollider references を維持したまま generated asset へ置換し、before / after report を出力します。主な engineering focus は、安全な Editor tooling、reference preservation、performance-aware mesh simplification です。

避けるべき主張:

```text
Commercial-grade universal mesh optimizer
Perfect automatic LOD generation
Guaranteed visual preservation for all meshes
```

使うべき主張:

```text
Unity asset QA tool
Editor-driven optimization workflow
Shape-preserving QEM simplification path
Reference-preserving mesh replacement
Before / after optimization reporting
Technical Artist tooling prototype
```
