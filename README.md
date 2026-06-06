# AssetUtility

**AssetUtility** is a Unity Editor tool for scanning, reviewing, editing, and optimizing scene assets such as Meshes, Textures, and Materials.

This repository is positioned as a **Technical Artist / Tools Programmer portfolio project**. It focuses on a common production problem in Unity projects: artists and engineers need to understand which assets are expensive, edit optimization targets safely, generate optimized assets, and keep scene / prefab references valid.

> Scope note: AssetUtility is an editor-side asset QA and optimization workflow, not a complete commercial mesh optimizer. The defensible claim is practical Unity tooling, reviewable optimization decisions, reference preservation, and performance-aware implementation.

---

## Portfolio summary

```text
Unity Scene / GameObject
  -> scan hierarchy
  -> collect Mesh / Texture / Material metadata
  -> display editable asset table
  -> edit texture resolution or target triangle count
  -> persist edit plan in AssetUtilityData
  -> apply changes
  -> generate optimized mesh or texture output
  -> replace MeshFilter / SkinnedMeshRenderer / MeshCollider references
  -> write before/after report
```

The tool is built around an **inspect before optimize** workflow. Before changing assets, it shows object type, scene, hierarchy path, material, polygon count, texture resolution, estimated VRAM, and warnings so optimization decisions are reviewable.

---

## Problem / user / output

| Item | Description |
|---|---|
| Problem | Scene assets can become expensive, but blind optimization can break references or damage visual quality. |
| Primary user | Technical Artist, Tools Programmer, Environment Artist, Unity developer. |
| Input | Active scene, all scenes, or a selected GameObject. |
| Output | Asset table, optimization plan, optimized mesh assets, texture outputs / importer targets, updated references, before/after report. |
| Safety goal | Review first, avoid name-based mis-targeting, keep references valid, and avoid mutating source meshes in place. |

---

## Reviewer path

For a quick review:

```text
1. Open the Unity project.
2. Run: Tools > Asset Utility
3. Scan Active Scene or Single Object.
4. Review All / Texture / Material / Polygon views.
5. Edit target texture size or target polygon count.
6. Apply changes.
7. Inspect generated optimized mesh assets and before/after report.
8. Review: Assets/Script/AssetUtility.cs
```

Recommended files to inspect:

```text
Assets/Script/AssetUtility.cs
Assets/Editor/Resources/AssetUtilityData.asset
Assets/OptimizedMeshes/        # generated output folder when optimization is applied
```

---

## Main workflow

```text
Open Asset Utility
  -> choose scan mode
  -> scan scene or selected object
  -> inspect asset table
  -> edit texture / polygon targets
  -> apply edits
  -> validate output
  -> save generated mesh assets
  -> update scene / prefab references
```

### Scan modes

| Mode | Purpose |
|---|---|
| Active Scene | Scan the currently open scene. |
| All Scenes | Scan project scenes for broader review. |
| Single Object | Target one GameObject for direct mesh optimization. |

### View modes

| View | Purpose |
|---|---|
| All | Object, material, polygon, and texture summary. |
| Texture | Texture name, type, resolution, estimated VRAM, and target resolution. |
| Material | Material names and material classification. |
| Polygon | Current / target triangle count and reduction view. |

---

## Implemented feature status

| Area | Status | Notes |
|---|---:|---|
| Unity EditorWindow workflow | Implemented | Tool entry point is `Tools > Asset Utility`. |
| Scene / object scan | Implemented | Collects mesh, texture, material, scene, and hierarchy information. |
| Asset QA table | Implemented | Displays asset cost and editable targets. |
| Persistent edit plan | Implemented | Uses ScriptableObject-style edit data. |
| Texture metadata | Implemented | Tracks texture path, type, resolution, and estimated VRAM. |
| Mesh metadata | Implemented | Tracks mesh path, polygon count, target count, and editability. |
| Mesh simplification | Implemented | Shape-preserving QEM path with fallback behavior. |
| Texture resize path | Implemented | GPU conversion / PNG output path exists for experimentation. |
| Reference replacement | Implemented | Updates MeshFilter, SkinnedMeshRenderer, and MeshCollider references. |
| Scene / prefab dirty handling | Implemented | Marks edited scenes / prefabs where needed. |
| Before/after report | Implemented | Records original target, result, and processing information. |
| QEM priority queue refactor | Planned | Current hot spot is repeated full edge / triangle scanning. |
| Automated editor tests | Planned | Needed for stronger regression confidence. |

---

## Data model

### `AssetInfo`

`AssetInfo` is the row model used by the editor table.

| Field | Meaning |
|---|---|
| `instanceID` | Unity instance ID. |
| `name` | Object name. |
| `scene` / `scenePath` | Scene identity. |
| `objectPath` | Hierarchy path to avoid same-name object mistakes. |
| `objectType` | Mesh / skinned mesh / collider-style classification. |
| `materialType` / `materialName` | Material metadata. |
| `polygonCount` / `targetPolygonCount` | Current and planned triangle count. |
| `meshPath` | Source mesh asset path. |
| `textureName` / `texturePath` | Texture metadata. |
| `textureType` | Texture classification. |
| `vramMB` | Estimated texture memory. |
| `resolution` / `targetResolution` | Current and planned texture size. |

### Edit records

| Type | Purpose |
|---|---|
| `TextureEditData` | Stores texture resize / max-size edit plan. |
| `MeshEditData` | Stores mesh optimization target and generated asset path. |
| `MaterialEditData` | Stores material-related edit plan. |
| `AssetUtilityData` | Persists the current edit plan. |

---

## Mesh optimization architecture

AssetUtility contains multiple optimization paths so it can prefer quality but still fall back safely.

```text
Target Mesh
  -> optional subdivision / smoothing
  -> Shape-Preserving QEM path
  -> GPU simplification path
  -> CPU fallback path
  -> validation
  -> save optimized Mesh asset
  -> replace scene / prefab references
```

### Shape-Preserving QEM options

| Option | Purpose |
|---|---|
| `preserveBorders` | Avoid collapsing border edges. |
| `preserveUVSeams` | Avoid UV seam damage where supported. |
| `preserveHardNormals` | Preserve hard normal boundaries. |
| `preventNonManifold` | Avoid non-manifold collapse. |
| `maxPositionError` | Limit positional drift. |
| `maxNormalDeviation` | Limit normal direction changes. |
| `minTriangleArea` | Avoid degenerate / tiny triangles. |
| `uvWeight` | Add UV difference penalty. |
| `normalWeight` | Add normal difference penalty. |
| `edgeLengthClamp` | Avoid very long collapses. |
| `snapToLocalSurface` | Move collapsed vertex toward local surface. |
| `sliverAspectMin` | Reduce sliver triangle creation. |
| `curvatureWeight` | Penalize collapses across high-curvature regions. |
| `compactOnFinish` | Remove unused vertices after simplification. |
| `recomputeQuadricsLocally` | Optional local QEM rebuild after collapse. |

---

## Safety / validation design

AssetUtility tries to avoid common editor-tool mistakes:

- identify targets by scene path / object path rather than object name alone;
- update MeshFilter references;
- update SkinnedMeshRenderer references;
- update MeshCollider references;
- save generated mesh assets instead of mutating source mesh in place;
- keep edit records in persistent data;
- mark scenes / prefab instances dirty when needed;
- generate before/after report entries.

---

## Current performance status

The current QEM implementation is functional, but it still has an important optimization target.

Known hot spots:

```text
Current QEM path
  -> searches all candidate edges each simplification iteration
  -> scans all triangle indices after collapse
  -> removes degenerate triangles during the loop
  -> can rebuild edge / border data repeatedly
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

Target portfolio evidence after implementation:

```text
Input mesh:      50k / 100k triangles
Before:          full edge scan + full triangle rewrite
After:           priority queue + adjacency cache
Measured result: xxx ms -> yyy ms
Output report:   Docs/Reports/qem_benchmark_YYYY-MM-DD.md
```

---

## Texture optimization workflow

Texture view estimates memory and allows resolution edits. The production-safe path should prefer importer settings where possible.

Recommended production-oriented path:

```text
TextureImporter.maxTextureSize
  -> compression / mipmap / sRGB setting review
  -> SaveAndReimport
  -> before/after report
```

The project also contains a direct resize path using GPU conversion and PNG output. That path is useful for experimentation, but importer-setting changes are usually safer for real Unity projects.

---

## Current architecture and refactor target

Current key files:

```text
Assets/Script/AssetUtility.cs
Assets/Editor/Resources/AssetUtilityData.asset
Assets/OptimizedMeshes/
```

Recommended refactor target for a stronger code review:

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

This split is intentionally listed as a target, because the current implementation prioritizes prototype speed and feature proof over final package architecture.

---

## How to run

Open the Unity project and run:

```text
Tools > Asset Utility
```

Suggested manual validation:

```text
1. Open a test scene.
2. Run Asset Utility.
3. Scan Active Scene.
4. Change one mesh target triangle count.
5. Apply.
6. Confirm generated mesh asset exists.
7. Confirm MeshFilter / SkinnedMeshRenderer / MeshCollider references are updated.
8. Confirm report logs original count, target count, final count, and processing time.
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

## Current limitations

- QEM performance still needs the priority queue + adjacency cache refactor.
- Texture optimization should be expanded around `TextureImporter` settings for safer production use.
- Multi-scene scanning requires more validation on large projects.
- Complex skinned mesh cases need more samples.
- Benchmark reports should be generated and committed under `Docs/Reports/`.
- Automated Unity Editor tests are not yet complete.
- The current code should be split into smaller review-friendly modules.

---

## Next improvements

### Short term

- Implement priority queue + adjacency cache QEM refactor.
- Replace loop-time triangle removal with triangle valid flags and final compaction.
- Add QEM benchmark report with before/after timings.
- Add before/after screenshots or GIF.
- Add sample scene and high-poly mesh fixture.
- Split core models, QEM simplifier, reference replacement, and report writer into separate files.

### Mid term

- TextureImporter-based platform override workflow.
- Batch report export for scene-wide optimization.
- Automated editor validation tests.
- Safer prefab editing workflow.
- CI compile check where possible.

### Long term

- Integrate with MAYAtoUnity import reports.
- Project-wide asset budget dashboard.
- Per-platform optimization profiles.
- Team-friendly QA report export.

---

## Portfolio wording

> AssetUtility is a Unity Editor asset QA and optimization tool. It scans scenes and selected objects, displays mesh / texture / material cost data in editable tables, stores optimization plans, applies mesh and texture changes, updates Unity component references, and writes before/after results. The main engineering focus is safe editor tooling, reference preservation, and performance-aware mesh simplification.

Avoid these claims:

```text
Commercial-grade universal mesh optimizer
Perfect automatic LOD generation
Guaranteed visual preservation for all meshes
```

Use these claims:

```text
Unity asset QA tool
Editor-driven optimization workflow
Shape-preserving QEM simplification path
Reference-preserving mesh replacement
Before/after optimization reporting
Technical Artist tooling prototype
```
