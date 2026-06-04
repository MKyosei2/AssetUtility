# AssetUtility Production Standard

This document defines the minimum bar required for AssetUtility to become a main portfolio project for top-tier game company applications.

The requested target is:

> AssetUtility must function correctly under any reasonable Unity project condition.

For a production-facing Unity Editor tool, this means the tool must be safe, reversible, testable, measurable, and robust across common asset types and project states.

---

## 1. Final target

AssetUtility should become a production-style Unity Editor optimization and validation tool.

The final target is:

- Scan Unity scenes and project assets reliably.
- Visualize Mesh / Texture / Material cost.
- Apply optimization safely.
- Update references correctly.
- Generate reports.
- Support undo / restore.
- Handle errors without corrupting scenes or prefabs.
- Prove behavior through samples and tests.

The tool should be presented as:

> A Unity Editor asset audit and optimization tool that scans scene assets, reports polygon / texture / material cost, applies mesh and texture optimization, updates scene / prefab references, and generates before-after reports with validation.

It should not be presented as:

> A perfect automatic optimizer that can reduce any asset without visual or data risk.

---

## 2. Non-negotiable minimum requirements

AssetUtility is not main-project ready until all of the following are true.

### 2.1 Demo scene

The repository must include a reproducible demo scene:

```text
Assets/Samples/AssetUtilityDemo/
  Scenes/DemoScene.unity
  Meshes/HighPolyStatic.asset or .fbx
  Meshes/SkinnedMeshSample.fbx
  Textures/LargeTexture.png
  Materials/DemoMaterials.mat
  Reports/before_after_report.json
  Screenshots/before.png
  Screenshots/after.png
```

### 2.2 Before / after proof

The README must show:

- Before scan table.
- After scan table.
- Mesh visual comparison.
- Texture resolution comparison.
- Polygon count reduction.
- VRAM estimate reduction.
- Processing time.

### 2.3 Report output

Every apply operation must be able to generate a report:

```json
{
  "scene": "DemoScene",
  "timestamp": "2026-01-01T00:00:00Z",
  "objectsScanned": 120,
  "meshesScanned": 34,
  "texturesScanned": 48,
  "materialsScanned": 25,
  "trianglesBefore": 1200000,
  "trianglesAfter": 650000,
  "textureVramBeforeMB": 512.0,
  "textureVramAfterMB": 256.0,
  "optimizedMeshes": 12,
  "resizedTextures": 8,
  "warnings": [],
  "errors": [],
  "durationMs": 14200
}
```

### 2.4 Safe apply behavior

Optimization must never silently corrupt a scene.

Required behavior:

- Dry-run preview before apply.
- Undo support for scene reference changes.
- Restore point for generated assets.
- Report of every changed object.
- Clear warning when SkinnedMesh / blend shape / UV seam risk exists.
- No modification when validation fails.
- No modification when target triangle count is invalid.

### 2.5 Robust reference update

The tool must correctly update:

- MeshFilter.sharedMesh
- SkinnedMeshRenderer.sharedMesh
- MeshCollider.sharedMesh
- Prefab instance overrides
- Scene dirty state
- AssetDatabase refresh

It must not break:

- prefab connections
- materials
- UVs
- normals
- tangents
- submesh material assignment
- mesh collider references

---

## 3. Required architecture upgrade

The current implementation should be split into services.

Target structure:

```text
Assets/AssetUtility/
  Editor/
    AssetUtilityWindow.cs
    AssetTableView.cs
    OptimizationPreviewWindow.cs
    ReportViewerWindow.cs

  Scan/
    AssetScanService.cs
    SceneScanService.cs
    ProjectAssetScanService.cs
    ObjectAssetCollector.cs
    AssetCostEstimator.cs

  Optimization/
    MeshOptimizationService.cs
    MeshSimplifierQEM.cs
    MeshSimplifierGPU.cs
    MeshSimplifierFallback.cs
    TextureOptimizationService.cs
    TextureImporterOptimizer.cs

  Apply/
    AssetApplyService.cs
    MeshReferenceReplacer.cs
    TextureReferenceUpdater.cs
    PrefabModificationService.cs
    UndoRestoreService.cs

  Validation/
    MeshValidationService.cs
    TextureValidationService.cs
    SceneValidationService.cs
    OptimizationRiskAnalyzer.cs

  Data/
    AssetInfo.cs
    MeshInfo.cs
    TextureInfo.cs
    MaterialInfo.cs
    OptimizationPlan.cs
    OptimizationReport.cs
    AssetUtilityData.cs

  Reports/
    JsonReportWriter.cs
    CsvReportWriter.cs
    MarkdownReportWriter.cs

  Tests/
    Editor/
      AssetScanTests.cs
      MeshOptimizationTests.cs
      TextureOptimizationTests.cs
      ReferenceReplacementTests.cs
      ReportWriterTests.cs
```

UI code must not directly own scan, optimization, validation, or apply logic.

---

## 4. Feature upgrade roadmap

### Phase 1: Make scan trustworthy

- Fix all-scene aggregation behavior.
- Separate scan logic from UI.
- Add active scene scan test.
- Add all scene scan test.
- Add single object scan test.
- Add texture VRAM estimate test.
- Add polygon count test.

Acceptance criteria:

- Demo scene scan produces expected object / mesh / texture counts.
- All Scenes scan does not drop previous scene results.

### Phase 2: Make optimization safe

- Add optimization plan preview.
- Add mesh validation before saving.
- Add target triangle validation.
- Add submesh / material preservation checks.
- Add SkinnedMesh warning system.
- Add Undo for reference replacement.
- Add restore path for generated meshes.

Acceptance criteria:

- Invalid target count does not modify assets.
- Failed simplification does not change scene references.
- MeshCollider is updated only after mesh validation succeeds.

### Phase 3: Make reports professional

- Add JSON report.
- Add CSV report.
- Add Markdown before-after report.
- Add screenshot guide.
- Add README benchmark table.

Acceptance criteria:

- Demo scene produces before / after report.
- Report includes triangles, texture size, VRAM estimate, object count, warnings, and errors.

### Phase 4: Improve texture workflow

Current texture path should move toward TextureImporter-based optimization.

Required upgrades:

- Read TextureImporter settings.
- Change max texture size safely.
- Preserve texture type.
- Preserve sRGB setting.
- Preserve normal map setting.
- Preserve mipmap setting.
- Support platform overrides.
- Add revert support.

Acceptance criteria:

- Texture resizing works without replacing source texture with incorrect format.
- Normal maps stay normal maps.
- sRGB / non-sRGB settings are preserved.

### Phase 5: Main portfolio release

- Add GIF.
- Add sample scene.
- Add before-after report.
- Add tests.
- Add architecture diagram.
- Add release package.

---

## 5. Minimum test requirements

Required tests:

```text
AssetScanTests
  - Active scene scan counts objects correctly.
  - All scenes scan aggregates all scenes.
  - Single object scan only scans selected hierarchy.

MeshOptimizationTests
  - Static mesh simplification produces valid mesh.
  - Invalid target triangle count fails safely.
  - Submesh material count is preserved or warning is generated.
  - MeshCollider reference updates after successful apply.

TextureOptimizationTests
  - VRAM estimate is deterministic.
  - TextureImporter max size can be changed and restored.
  - Normal map settings are preserved.

ReferenceReplacementTests
  - MeshFilter reference updates.
  - SkinnedMeshRenderer reference updates.
  - Prefab instance override is recorded.

ReportWriterTests
  - JSON report is valid.
  - CSV report includes expected columns.
```

---

## 6. Edge cases that must be handled

AssetUtility must not crash or corrupt data when encountering:

- Missing mesh reference.
- Missing material.
- Missing texture.
- Read-only asset path.
- Prefab instance.
- Nested prefab.
- Multi-scene setup.
- SkinnedMeshRenderer.
- Mesh with blend shapes.
- Mesh with multiple submeshes.
- Mesh with no UVs.
- Mesh with invalid normals.
- MeshCollider sharing original mesh.
- Texture marked as normal map.
- Texture with platform override.
- AssetDatabase refresh during operation.
- Scene not saved.
- User cancels operation.

---

## 7. Reported metrics

Main portfolio README must include at least one real benchmark table:

| Metric | Before | After | Change |
|---|---:|---:|---:|
| Objects scanned | TBD | TBD | - |
| Meshes scanned | TBD | TBD | - |
| Textures scanned | TBD | TBD | - |
| Total triangles | TBD | TBD | TBD |
| Texture VRAM estimate | TBD | TBD | TBD |
| Optimized mesh count | TBD | TBD | - |
| Resized texture count | TBD | TBD | - |
| Processing time | - | TBD | - |

---

## 8. Portfolio wording rule

Allowed wording after Phase 3:

> Unity Editor上でMesh / Texture / Materialをスキャンし、ポリゴン数・VRAM概算・メモリ使用量を可視化するasset audit / optimization toolを開発。最適化plan preview、mesh validation、texture importer optimization、scene / prefab reference update、before-after report出力を実装した。

Forbidden wording:

> どんなアセットでも自動で安全に最適化できます。

Allowed wording for final target:

> 一般的なUnity production asset workflowで壊れにくいよう、dry-run、validation、undo、report、fallbackを備えたEditor最適化ツールとして設計した。

---

## 9. Main-project readiness checklist

AssetUtility can become a main portfolio project only when this checklist is complete.

- [ ] Demo scene exists.
- [ ] Before / after screenshots exist.
- [ ] README has GIF.
- [ ] All Scenes scan aggregation is fixed and tested.
- [ ] Code is split into modules.
- [ ] Optimization plan preview exists.
- [ ] Mesh validation exists.
- [ ] Safe apply / rollback exists.
- [ ] TextureImporter-based optimization exists.
- [ ] JSON / CSV / Markdown report exists.
- [ ] MeshFilter replacement test exists.
- [ ] SkinnedMeshRenderer replacement test exists.
- [ ] MeshCollider replacement test exists.
- [ ] Prefab override test exists.
- [ ] Benchmark table exists.
- [ ] Known limitations are documented honestly.
