============================================================
2. AssetUtility
============================================================

■ 作品概要
AssetUtility は、Unity シーン内の Mesh、Texture、Material をスキャンし、ポリゴン数・テクスチャ解像度・推定 VRAM などを確認しながら、安全に最適化する Unity Editor アセット QA / 最適化ツールです。
「最適化する前に確認する」ことを重視し、変更対象、変更前後の数値、参照更新、レポート生成までを Editor 上のワークフローとしてまとめています。

■ 実行環境等を含めた実行方法
リポジトリ：MKyosei2/AssetUtility
実行環境：Unity Editor 6000.3.0f1
補足：ProjectSettings/ProjectVersion.txt 上の Unity バージョンは 6000.3.0f1 です。

実行手順：
1. Unity Hub から AssetUtility のプロジェクトフォルダを開きます。
2. Unity の script compile が完了するまで待ちます。
3. 上部メニューから以下を実行します。
   Tools > Asset Utility
4. Asset Utility ウィンドウが開きます。
5. 動画・レビュー用の確認をする場合は、まず以下を押します。
   検証Demo作成
6. scan 対象として以下のいずれかを選びます。
   - 開いているシーン
   - オブジェクト単体
   - すべてのシーン
7. 表示タブを切り替えて確認します。
   - すべて
   - テクスチャー
   - マテリアル
   - ポリゴン
8. ポリゴンの削減を確認する場合は、以下を押します。
   ポリゴン数 50% 削減
9. 内容を確認した上で以下を押します。
   適応する
10. Apply Preview の内容を確認し、「適用する」を選択します。
11. 生成物を確認します。
   Assets/OptimizedMeshes
12. 証拠として以下も実行できます。
   - Before/After保存
   - 証拠Pack生成

確認時に見るポイント：
- scan 後に Objects / Poly / Texture VRAM の summary が表示されるか。
- ポリゴン view で current triangle count、target triangle count、reduction ratio、warning、mesh path が確認できるか。
- source mesh を直接破壊せず、optimized mesh asset が生成されるか。
- MeshFilter、SkinnedMeshRenderer、MeshCollider の参照が更新されるか。
- Before / After report が残るか。

■ プログラムを作成する上で苦労した箇所
1. 参照を壊さない最適化
   Mesh を最適化するだけでなく、Scene や Prefab 上の MeshFilter、SkinnedMeshRenderer、MeshCollider が正しく generated mesh を参照する必要があります。名前だけで対象を探すと誤適用が起きるため、scene path、object path、mesh path を使って対象を識別するようにしました。

2. Editor 操作として安全に apply すること
   Unity の OnGUI 中に Scene open / save / asset update のような重い処理を直接行うと GUI 状態が壊れることがあります。そのため、Apply は preview dialog を挟み、EditorApplication.delayCall を使って遅延実行する構成にしています。

3. Mesh simplification の品質と安全性
   QEM simplification では、境界保持、UV seam、hard normal、non-manifold 防止、sliver triangle 防止、normal deviation、local surface snap などを考慮しました。単純にポリゴン数を減らすだけだと見た目や mesh の健全性が壊れるため、形状保護のための option を多く用意しました。

4. パフォーマンス
   現在の QEM は動作しますが、全 edge / triangle を繰り返し scan する箇所があり、大規模 mesh では高速化余地があります。priority queue、adjacency cache、triangle valid flags、delayed compaction への refactor が今後の改善点です。

5. アーティストとエンジニアの両方が確認できる UI
   数値だけでなく、object type、material、texture、warning、target value を表形式で見せることで、アーティストが判断しやすく、エンジニアが安全性を確認しやすい UI を目指しました。

■ 力を入れて作った部分 / プログラム上で特に注意して見てもらいたい箇所
1. EditorWindow workflow
   ファイル：Assets/Script/AssetUtility.cs
   対象クラス：AssetUtilityWindow
   見てほしい点：
   - Tools > Asset Utility から起動する EditorWindow です。
   - scan mode、view mode、summary、editable table、apply、before/after、evidence pack までを 1 つの workflow としてまとめています。

2. Asset scan と table 表示
   ファイル：Assets/Script/AssetUtility.cs
   見てほしい点：
   - Mesh / Texture / Material を収集し、AssetInfo として table に表示します。
   - object name だけではなく scenePath、objectPath、meshPath、texturePath を持たせ、誤適用を避ける設計にしています。

3. Shape-preserving QEM simplification
   ファイル：Assets/Script/AssetUtility.cs
   対象：AssetOptimizer / SimplifyOptions / QEM 関連処理
   見てほしい点：
   - preserveBorders、preserveUVSeams、preserveHardNormals、preventNonManifold、maxNormalDeviation、sliverAspectMin、curvatureWeight など、形状破綻を防ぐためのパラメータを用意しています。
   - BoneWeight を保持する処理もあり、skinned mesh への対応を意識しています。

4. Apply preview と遅延実行
   ファイル：Assets/Script/AssetUtility.cs
   対象：ScheduleApplyAssetChanges / ApplyAssetChanges 周辺
   見てほしい点：
   - 適用前に preview dialog を出し、意図しない一括変更を防いでいます。
   - IMGUI の Begin/End 状態を壊さないよう、delayCall で処理を遅延させています。

5. Generated mesh 保存
   ファイル：Assets/Script/AssetUtility.cs
   対象：SaveOptimizedMesh
   見てほしい点：
   - source mesh を直接 mutate せず、Assets/OptimizedMeshes に generated mesh asset として保存します。
   - 置換対象の reference を更新する前提で、安全に最適化結果を残します。

6. Before / After report と証拠Pack
   ファイル：Assets/Script/AssetUtility.cs
   見てほしい点：
   - ポートフォリオ審査で、変更前後の数値や処理結果を確認できるようにしています。
   - 実行結果を「見せる」ための仕組みとして力を入れています。

■ 参考にしたソースファイルについて
外部の特定ソースファイルをコピーして実装したものはありません。
参考にした考え方は、Unity EditorWindow、AssetDatabase、TextureImporter、MeshFilter / SkinnedMeshRenderer / MeshCollider の参照更新、QEM mesh simplification の一般的なアルゴリズム、Editor tool における非破壊ワークフローです。

作品内で実装意図を確認しやすいファイル：
- README.md
- Assets/Script/AssetUtility.cs
- Assets/Editor/Resources/AssetUtilityData.asset
- Assets/OptimizedMeshes/

今後分割したい構成：
- Assets/AssetUtility/Editor/AssetUtilityWindow.cs
- Assets/AssetUtility/Editor/AssetScanController.cs
- Assets/AssetUtility/Editor/AssetOptimizationController.cs
- Assets/AssetUtility/Core/Models/AssetInfo.cs
- Assets/AssetUtility/Core/Optimization/QemMeshSimplifier.cs
- Assets/AssetUtility/Core/Optimization/MeshReferenceReplacer.cs
- Assets/AssetUtility/Tests/Editor/ReferenceReplacementTests.cs