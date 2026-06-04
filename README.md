# AssetUtility

**AssetUtility** は、Unity シーン内の Mesh / Texture / Material をスキャンし、負荷の可視化・編集・最適化を行うための Unity Editor Tool です。

このツールは、ゲーム制作における **アセット最適化、描画負荷確認、Technical Artist 向け検証ワークフロー、Unity Editor 拡張** を想定して作成しています。  
単にポリゴン数を減らすだけではなく、シーン内のアセット情報を一覧化し、テクスチャ解像度やメッシュのターゲットポリゴン数を編集し、生成した最適化アセットを scene / prefab に反映するところまでを扱います。

---

## 1. このツールが解決しようとしている課題

Unity プロジェクトでは、開発が進むにつれて以下のような問題が起こります。

- どの GameObject / Mesh / Texture が重いのか把握しにくい
- シーン全体のポリゴン数や VRAM 使用量を俯瞰しにくい
- Texture 解像度や Mesh の削減目標を表形式で調整したい
- 最適化前後の状態を記録したい
- MeshFilter / SkinnedMeshRenderer / MeshCollider の参照更新を手作業で行いたくない
- Prefab instance に対する変更をまとめて反映したい
- Technical Artist や Tool Engineer が使える Editor 拡張として整理したい

AssetUtility は、こうした問題に対して **Unity Editor 上でスキャン → 表示 → 編集 → 適用 → 保存** までを行う最適化支援ツールです。

---

## 2. コンセプト

### Inspect before optimize

最適化は、まず現状を把握できなければ安全に行えません。  
AssetUtility では、Scene / Object から AssetInfo を収集し、以下の情報をテーブル化します。

- Object name
- Scene name
- Object type
- Runtime memory estimate
- Material type
- Polygon count
- Texture name
- Texture type
- Texture resolution
- Estimated VRAM usage

### Editor-driven workflow

外部ツールではなく Unity EditorWindow として実装しているため、Unity 上で直接確認・編集・適用できます。

- `Tools > Asset Utility` から起動
- Active Scene / All Scenes / Single Object を切り替え
- Texture / Material / Polygon などの view を切り替え
- テーブル上で数値を編集
- Apply ボタンで反映

### Safe-ish optimization pipeline

最適化によって参照が壊れたり、MeshCollider が古い mesh を参照し続けたりする問題を避けるため、適用時に関連コンポーネントも更新します。

- MeshFilter
- SkinnedMeshRenderer
- MeshCollider
- Scene dirty state
- Prefab instance modification
- Generated mesh asset
- ScriptableObject edit record

---

## 3. High-level workflow

```text
Unity Scene / GameObject
  ↓
Scan scene hierarchy
  ↓
Collect AssetInfo
  ↓
Display table by view mode
  ↓
Edit texture resolution or target polygon count
  ↓
Store edit data in AssetUtilityData
  ↓
Apply changes
  ↓
Generate optimized assets
  ↓
Update MeshFilter / SkinnedMeshRenderer / MeshCollider
  ↓
Mark scene and prefab dirty
```

---

## 4. Editor entry point

AssetUtility は Unity Editor menu から起動します。

```text
Tools > Asset Utility
```

EditorWindow 内には、主に以下の UI があります。

- Scan mode selector
- View mode selector
- Asset table
- QEM shape preservation options
- Apply button
- Discard changes button
- 50% polygon reduction shortcut

---

## 5. Scan modes

| Mode | Purpose |
|---|---|
| Active Scene | 現在開いている scene をスキャンする |
| All Scenes | Project 内の scene を順に開いてスキャンする |
| Single Object | 指定した GameObject だけを対象にする |

### Active Scene

現在の active scene に含まれる root object から再帰的に GameObject を辿り、Mesh / Material / Texture 情報を収集します。

### All Scenes

Project 内の scene asset を列挙し、additive に開いて scan します。  
この機能は大規模 project の検査に向いていますが、現在の実装では multi-scene aggregation の検証をさらに行う必要があります。

### Single Object

特定 GameObject の MeshFilter / SkinnedMeshRenderer を対象に、直接 target triangle count を指定して最適化できます。

---

## 6. View modes

| View | Description |
|---|---|
| All | Object type と memory estimate を表示 |
| Texture | Texture 名、種類、VRAM estimate、解像度を表示・編集 |
| Material | Material type を表示 |
| Polygon | Polygon count を表示・編集 |

各 view は EditorWindow 内で切り替えられます。  
Object type / Texture type / Material type の filter も保持しています。

---

## 7. Data model

### AssetInfo

Scene 内の GameObject / Asset 情報を表示するための基本データです。

| Field | Meaning |
|---|---|
| `instanceID` | Unity instance ID |
| `name` | Object name |
| `scene` | Scene name |
| `objectType` | Object classification |
| `memoryMB` | Runtime memory estimate |
| `materialType` | Material classification |
| `polygonCount` | Triangle count |
| `textureName` | Main texture name |
| `textureType` | Texture classification |
| `vramMB` | Estimated VRAM usage |
| `resolution` | Texture resolution |

### TextureEditData

Texture 解像度編集を記録するデータです。

| Field | Meaning |
|---|---|
| `texturePath` | Texture asset path |
| `originalSize` | Original resolution |
| `editedSize` | Target resolution |
| `processTimeMS` | Processing time |

### MeshEditData

Mesh 最適化の対象と結果を記録するデータです。

| Field | Meaning |
|---|---|
| `assetPath` | Original mesh asset path |
| `simplifiedMeshPath` | Generated optimized mesh path |
| `targetObjectName` | Target GameObject name |
| `targetInstanceID` | Target instance ID |
| `targetTriangleCount` | Target triangle count |
| `processTimeMS` | Processing time |

### AssetUtilityData

Editor 上の編集状態を永続化する ScriptableObject です。

```text
Assets/Editor/Resources/AssetUtilityData.asset
```

この asset に texture edit / mesh edit / material edit の情報を保存することで、Window の再描画や再スキャン後も編集状態を参照できます。

---

## 8. Mesh optimization architecture

AssetUtility には複数の mesh optimization path があります。

```text
Target Mesh
  ↓
Optional subdivision / smoothing
  ↓
+-----------------------------+
| Shape-Preserving QEM        |
| GPU simplification          |
| CPU fallback simplification |
+-----------------------------+
  ↓
Validation
  ↓
Save optimized Mesh asset
  ↓
Replace references in scene / prefab
```

---

## 9. Shape-Preserving QEM

メインの品質重視 path として、Shape-Preserving QEM 系の簡略化を実装しています。

### QEM options

| Option | Purpose |
|---|---|
| `preserveBorders` | 境界 edge を保持する |
| `preserveUVSeams` | UV seam を保持する |
| `preserveHardNormals` | hard normal を保持する |
| `preventNonManifold` | non-manifold collapse を避ける |
| `maxPositionError` | 位置誤差の上限 |
| `maxNormalDeviation` | 法線変化の上限 |
| `minTriangleArea` | 極小 triangle を避ける |
| `uvWeight` | UV 差分の重み |
| `normalWeight` | normal 差分の重み |
| `edgeLengthClamp` | 長すぎる collapse の制限 |
| `snapToLocalSurface` | collapse 後に局所 surface へ寄せる |
| `maxIterationsPerStep` | simplification step の反復上限 |
| `sliverAspectMin` | sliver triangle 抑止 |
| `curvatureWeight` | 曲率保持の重み |
| `compactOnFinish` | 未使用 vertex を削除する |
| `recomputeQuadricsLocally` | collapse 後に近傍 QEM を再計算する |

### Why QEM?

単純な triangle 間引きでは、輪郭・UV seam・hard normal・細長い triangle の破綻が起きやすくなります。  
QEM 系の手法を使うことで、見た目の変化を抑えながら triangle count を削減する方向を目指しています。

この実装では、portfolio / research tool として以下を重視しています。

- 形状保持のための option を明示する
- fallback path を用意する
- 生成 mesh を asset として保存する
- 適用結果を scene / prefab に反映する

---

## 10. GPU simplification path

QEM を使わない場合の高速化 path として、ComputeShader を使った GPU simplification path を用意しています。

```text
Mesh vertices / triangles
  ↓
ComputeBuffer
  ↓
ComputeShader dispatch
  ↓
Output buffers
  ↓
Mesh reconstruction
```

GPU path は experimental 扱いです。  
ComputeShader が存在しない、kernel が見つからない、出力が空になるなどの場合は fallback します。

---

## 11. CPU fallback path

GPU path が使えない場合や失敗した場合は CPU fallback を使用します。

Fallback path では、以下のような保守的な処理を行います。

- submesh が複数ある場合は安全側に倒す
- UV 距離が大きい edge collapse を避ける
- triangle area が極小になる collapse を避ける
- collapse 後に degenerate triangle を削除する
- normals / tangents を再計算する

完全な production mesh optimizer ではありませんが、Editor tool として壊れにくい fallback を目指しています。

---

## 12. Texture optimization

Texture view では、texture resolution と VRAM estimate を表示します。  
編集された resolution は `TextureEditData` として保存され、表示上の VRAM estimate も更新されます。

実装には `Graphics.ConvertTexture` を使った resize path も含まれています。

```text
Source Texture2D
  ↓
Graphics.ConvertTexture
  ↓
EncodeToPNG
  ↓
Save as resized texture asset
  ↓
AssetDatabase.ImportAsset
```

現在の texture optimization は、実運用向けには以下の拡張余地があります。

- TextureImporter max size の直接変更
- format / compression 設定の変更
- platform override 対応
- mipmap / normal map / sRGB 設定の保持
- PNG 以外の保存方針

---

## 13. Apply pipeline

変更を適用すると、AssetUtility は `AssetUtilityData` の edit records を参照し、対象 asset に対して最適化を実行します。

### Mesh apply flow

```text
MeshEditData
  ↓
Load original Mesh
  ↓
Compare original triangle count and target count
  ↓
Run selected simplification path
  ↓
Validate generated mesh
  ↓
Save optimized mesh under Assets/OptimizedMeshes
  ↓
Find scene objects using original mesh
  ↓
Replace MeshFilter / SkinnedMeshRenderer reference
  ↓
Update MeshCollider if present
  ↓
Record prefab instance modifications
  ↓
Mark scene dirty
  ↓
Save AssetUtilityData
```

### Generated assets

Optimized meshes are saved under:

```text
Assets/OptimizedMeshes
```

Generated file names include the simplification mode and target triangle count.

---

## 14. Prefab / Scene integration

最適化後、AssetUtility は単に mesh asset を作るだけでなく、Scene 上の参照も更新します。

対象 component:

- `MeshFilter`
- `SkinnedMeshRenderer`
- `MeshCollider`

Prefab instance の場合は、`PrefabUtility.RecordPrefabInstancePropertyModifications` や `PrefabUtility.ApplyPrefabInstance` を使って変更を記録します。

この部分は、実際の制作現場で重要です。  
最適化 mesh を作っても scene / prefab 側の参照が古いままだと、実際には最適化が反映されないためです。

---

## 15. Current implementation status

| Area | Status | Notes |
|---|---:|---|
| EditorWindow | Implemented | `Tools > Asset Utility` から起動 |
| Active scene scan | Implemented | root object から再帰 scan |
| All scenes scan | Implemented / needs validation | multi-scene aggregation の検証が必要 |
| Single object mode | Implemented | MeshFilter / SkinnedMeshRenderer 対応 |
| AssetInfo collection | Implemented | object / material / texture / polygon 情報を収集 |
| Texture table | Implemented | resolution 編集と VRAM estimate |
| Material table | Implemented | material type 表示 |
| Polygon table | Implemented | triangle count 編集 |
| QEM simplification | Implemented | main optimization path |
| GPU simplification | Experimental | ComputeShader 依存 |
| CPU fallback | Implemented | conservative fallback |
| Mesh asset saving | Implemented | `Assets/OptimizedMeshes` に保存 |
| Scene reference update | Implemented | MeshFilter / SkinnedMeshRenderer / MeshCollider |
| Prefab modification | Implemented / partial | Prefab instance へ反映 |
| Undo support | Partial | 一部 editor 操作で Undo を使用 |
| Automated tests | Not yet | 今後追加予定 |
| Benchmark report | Not yet | 今後追加予定 |

---

## 16. Known issues / limitations

現在の制限を明確に記載します。

- 実装が大きな単一 script に集中しており、production では module 分割が必要
- All Scenes scan は `allAssets` aggregation の挙動を追加検証する必要がある
- GPU simplification は ComputeShader の存在と互換性に依存する
- Texture resize は TextureImporter workflow ではなく PNG 保存 path が中心
- Undo / rollback は完全ではない
- QEM の品質評価は sample mesh / screenshot / benchmark による検証が必要
- Skinned mesh / blend shape / UV seam / hard normal の破綻検証を追加する必要がある
- 大規模 project での performance test は未実施

これらは、今後の refactor / validation / test で改善する前提です。

---

## 17. Recommended refactor plan

現状の機能を保ちながら production-level に近づける場合、以下のように分割します。

```text
Assets/AssetUtility/
  Editor/
    AssetUtilityWindow.cs
    AssetTableView.cs
    AssetUtilityMenu.cs

  Scan/
    AssetScanService.cs
    SceneScanService.cs
    ObjectAssetCollector.cs

  Optimization/
    AssetOptimizer.cs
    MeshSimplifierQEM.cs
    MeshSimplifierGPU.cs
    MeshSimplifierFallback.cs
    TextureResizeUtility.cs

  Apply/
    AssetApplyService.cs
    MeshReferenceReplacer.cs
    PrefabApplyUtility.cs

  Data/
    AssetInfo.cs
    MaterialInfo.cs
    MeshInfo.cs
    TextureEditData.cs
    MeshEditData.cs
    MaterialEditData.cs
    AssetUtilityData.cs

  Reports/
    AssetOptimizationReport.cs
    CsvReportExporter.cs

  Tests/
    AssetScanTests.cs
    MeshSimplifierTests.cs
    TextureResizeTests.cs
```

### Refactor goals

- UI と処理を分離する
- scan / optimize / apply / report を独立させる
- test しやすい構造にする
- mesh simplification を差し替え可能にする
- benchmark と regression test を追加する

---

## 18. Verification plan

本格的な portfolio / production tool として見せるため、以下の検証を追加予定です。

### Benchmark scene

同じ scene に対して最適化前後の数値を比較します。

| Metric | Before | After | Reduction |
|---|---:|---:|---:|
| Total triangles | TBD | TBD | TBD |
| Mesh memory | TBD | TBD | TBD |
| Texture VRAM estimate | TBD | TBD | TBD |
| Optimized mesh count | TBD | TBD | TBD |
| Apply time | TBD | TBD | TBD |

### Visual validation

- mesh before / after screenshot
- wireframe comparison
- UV seam comparison
- normal / shading comparison
- collider replacement check

### Automated tests

- empty mesh
- single triangle mesh
- high-poly mesh
- mesh with UV seams
- mesh with hard normals
- mesh with MeshCollider
- SkinnedMeshRenderer target
- invalid target triangle count

---

## 19. Roadmap

### Short term

- README に screenshot / GIF を追加
- demo scene を追加
- All Scenes scan の aggregation を修正・検証
- ComputeShader dependency を明示または同梱
- Optimization report を追加
- Before / After の数値を追加

### Mid term

- class 分割 refactor
- TextureImporter-based resize / compression workflow
- CSV / JSON export
- preview mode
- revert / rollback workflow
- automated tests

### Long term

- project-wide asset audit
- CI での editor test
- LOD generation support
- platform-specific texture optimization
- AssetBundle / Addressables 向け report
- MAYAtoUnity と連携した DCC import → optimization pipeline

---

## 20. Portfolio / technical appeal points

このプロジェクトで示せる技術要素:

- Unity EditorWindow tool development
- Scene traversal and asset inspection
- Mesh processing
- QEM-style simplification
- GPU / CPU fallback design
- Texture cost visualization
- ScriptableObject-backed editor state
- Prefab / scene modification handling
- Technical Artist workflow design
- Production pipeline awareness

---

## 21. How to present this project

書類・ポートフォリオでは、以下のように説明できます。

> Unity Editor 上で Mesh / Texture / Material をスキャンし、メモリ使用量・VRAM 概算・ポリゴン数を可視化するアセット最適化ツールを開発しました。  
> Texture 解像度や target triangle count を EditorWindow 上で編集し、Shape-Preserving QEM、GPU simplification、CPU fallback を切り替えながら最適化 mesh を生成します。  
> 生成 mesh は asset として保存し、MeshFilter / SkinnedMeshRenderer / MeshCollider / Prefab instance に反映することで、単なる解析ツールではなく実際の制作 workflow に接続できる設計にしました。

---

## 22. Relationship with MAYAtoUnity

AssetUtility は、別リポジトリの `MAYAtoUnity` と組み合わせることで、DCC import 後の optimization tool として位置付けられます。

```text
Maya scene import
  ↓
Unity hierarchy reconstruction
  ↓
AssetUtility scan
  ↓
Texture / material / polygon inspection
  ↓
Mesh / texture optimization
  ↓
Prefab / scene validation
```

この 2 つを合わせることで、以下のような pipeline portfolio として見せられます。

- Maya file import
- Unity reconstruction
- Asset cost visualization
- Optimization
- Prefab / scene update
- Report / validation

---

## 23. Disclaimer

This project is an independent Unity Editor tooling project for technical research and portfolio purposes.  
It is not a complete replacement for commercial mesh optimization middleware or Unity's official import pipeline.
