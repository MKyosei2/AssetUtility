# AssetUtility

AssetUtility is a Unity Editor tool for scanning, visualizing, and optimizing scene assets.  
It helps inspect Mesh, Texture, and Material usage inside Unity scenes, then applies optimization operations such as texture resizing and mesh simplification directly from a custom Editor window.

This project focuses on **asset optimization workflow tooling** for game development: identifying heavy assets, editing target values, applying changes safely, and keeping optimization data inspectable inside the Unity project.

---

## Goals

- Visualize asset cost inside Unity scenes.
- Inspect object type, material type, texture information, memory usage, VRAM estimate, and polygon count.
- Edit texture resolution and mesh polygon targets from an Editor window.
- Apply mesh simplification using Shape-Preserving QEM, GPU optimization, or CPU fallback.
- Support active-scene, all-scene, and single-object workflows.
- Provide a portfolio-quality example of Unity Editor tooling for technical art / optimization pipelines.

---

## What this tool does

AssetUtility provides a custom Unity Editor window available from:

```text
Tools > Asset Utility
```

The tool scans scene objects, collects asset information, displays it in editable tables, and applies optimization changes.

High-level workflow:

```text
Unity Scene / GameObject
  ↓
Asset scan
  ↓
AssetInfo list
  ↓
Texture / Material / Polygon view
  ↓
Edit target resolution or triangle count
  ↓
Apply changes
  ↓
Generated optimized assets + updated scene / prefab references
```

---

## Key features

### Scene asset scanning

AssetUtility can scan different scopes:

- Active scene
- All scenes in the project
- Single selected GameObject

For each object, it collects information such as:

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

### Editor table views

The Editor window provides multiple views:

| View | Purpose |
|---|---|
| All | Shows object type and memory usage. |
| Texture | Shows texture name, type, VRAM estimate, and editable resolution. |
| Material | Shows material type information. |
| Polygon | Shows polygon count and editable target triangle count. |

Header filters are available for object type, texture type, and material type.

### Texture optimization support

Texture data is tracked through editable resolution records.  
The tool can save target texture sizes and estimate VRAM changes based on edited resolution.

A GPU-based texture resize path is also implemented through `Graphics.ConvertTexture`, saving resized PNG assets back into the project.

### Mesh simplification

AssetUtility includes multiple mesh optimization paths:

1. **Shape-Preserving QEM**
2. **GPU simplification using ComputeShader**
3. **CPU fallback simplification**

The QEM path includes options designed to preserve mesh quality:

- Preserve borders
- Preserve UV seams
- Preserve hard normals
- Prevent non-manifold collapse
- Limit maximum position error
- Limit normal deviation
- Minimum triangle area guard
- UV weight
- Normal weight
- Edge length clamp
- Snap to local surface
- Sliver triangle guard
- Curvature weighting
- Mesh compaction after simplification

### Single-object optimization

In Single Object mode, a target GameObject can be selected directly.  
The tool reads its `MeshFilter` or `SkinnedMeshRenderer`, shows the current triangle count, accepts a target triangle count, and applies an optimized mesh.

When optimization is applied, AssetUtility can update:

- MeshFilter shared mesh
- SkinnedMeshRenderer shared mesh
- MeshCollider shared mesh
- Scene dirty state
- Prefab instance modifications

### Batch-style editing

The tool supports editing multiple assets in the table and then applying the changes together.  
It also includes a quick operation for reducing polygon targets by 50%.

### Persistent optimization data

Optimization edits are stored in a ScriptableObject asset:

```text
Assets/Editor/Resources/AssetUtilityData.asset
```

This data tracks:

- Material edit records
- Texture edit records
- Mesh edit records
- Generated simplified mesh paths
- Target object names and instance IDs
- Target triangle counts

---

## Installation

1. Open a Unity project.
2. Copy this repository into the project so that the tool scripts are under the Unity `Assets` folder.
3. Let Unity compile the scripts.
4. Open the tool from:

```text
Tools > Asset Utility
```

Recommended placement:

```text
Assets/EasyTool/AssetUtility.cs
```

or another Editor-safe folder depending on your project structure.

---

## Basic usage

1. Open `Tools > Asset Utility`.
2. Choose a scan mode:
   - `すべてのシーン`
   - `開いているシーン`
   - `オブジェクト単体`
3. Choose a view mode:
   - `すべて`
   - `テクスチャー`
   - `マテリアル`
   - `ポリゴン`
4. Inspect the collected asset data.
5. Edit texture resolution or target polygon count.
6. Click `適応する` to apply changes.
7. Check generated optimized meshes under:

```text
Assets/OptimizedMeshes
```

---

## Mesh simplification modes

| Mode | Description | Use case |
|---|---|---|
| Shape-Preserving QEM | Quality-focused simplification with border / UV / normal preservation options. | Portfolio-quality mesh optimization and safer visual reduction. |
| GPU optimization | ComputeShader-based path when available. | Fast experimental simplification. |
| CPU fallback | Conservative CPU simplification used when GPU path fails or is unavailable. | Compatibility and safety fallback. |

---

## Technical highlights

- Custom Unity `EditorWindow` workflow.
- Scene traversal and asset cost collection.
- Runtime memory estimation through Unity profiling APIs.
- Editable asset tables with filterable views.
- ScriptableObject-backed edit persistence.
- Shape-Preserving QEM mesh simplification.
- GPU / CPU fallback architecture.
- Prefab and scene reference updating after optimization.
- MeshCollider synchronization after mesh replacement.
- Automatic secondary UV generation before saving optimized meshes when supported.

---

## Current status

| Area | Status | Notes |
|---|---:|---|
| Active scene scan | Supported | Scans the active Unity scene. |
| All scene scan | Implemented | Needs validation for multi-scene aggregation behavior. |
| Single object mode | Supported | Works with MeshFilter and SkinnedMeshRenderer targets. |
| Texture table | Supported | Shows texture information and editable resolution values. |
| Material table | Supported | Shows material type information. |
| Polygon table | Supported | Shows and edits polygon targets. |
| QEM simplification | Implemented | Main quality-focused simplification path. |
| GPU simplification | Experimental | Requires compatible ComputeShader setup. |
| CPU fallback | Implemented | Used when GPU path is unavailable or fails. |
| Prefab update | Implemented | Applies prefab instance modifications where possible. |
| Undo / rollback | Partial | Some edit operations use Undo, but full rollback workflow needs more work. |

---

## Known limitations

- The current implementation is concentrated in a large script file and should be split into smaller modules for production maintainability.
- GPU simplification depends on a compatible ComputeShader being available in the expected project path.
- All-scene scanning should be carefully validated to ensure collected results are aggregated as intended.
- Texture resize behavior currently focuses on PNG-style output and may need extension for broader import settings workflows.
- Mesh simplification results should be visually inspected before being used in production.
- Full automated tests and benchmark scenes are not yet included.

---

## Recommended refactor plan

For production-level maintainability, the current implementation should be separated into clearer modules:

```text
Assets/AssetUtility/
  Editor/
    AssetUtilityWindow.cs
    AssetScanService.cs
    AssetApplyService.cs
  Core/
    AssetOptimizer.cs
    MeshSimplifierQEM.cs
    TextureResizeUtility.cs
  Data/
    AssetInfo.cs
    AssetUtilityData.cs
    MeshEditData.cs
    TextureEditData.cs
  Tests/
    MeshSimplifierTests.cs
    AssetScanTests.cs
```

This would make the project easier to review during technical screening and easier to extend in a production environment.

---

## Roadmap

- Add demo scene and before / after screenshots.
- Add benchmark results for polygon reduction and VRAM reduction.
- Add CSV / JSON export for optimization reports.
- Add safer preview mode before applying mesh replacement.
- Add full Undo / restore support for generated optimized meshes.
- Add automated tests for mesh simplification edge cases.
- Add validation for UV seams, normals, skinned meshes, and mesh colliders.
- Split the current implementation into smaller production-style modules.

---

## Portfolio focus

This project demonstrates:

- Unity Editor tooling
- Asset optimization workflow design
- Mesh processing and simplification
- Texture and VRAM cost visualization
- Scene scanning and batch editing
- ScriptableObject-based editor data persistence
- Technical artist / tools programmer problem solving

Used together with `MAYAtoUnity`, this tool can be presented as part of a larger DCC-to-Unity pipeline:

```text
Maya asset import
  ↓
Unity reconstruction
  ↓
Asset inspection
  ↓
Optimization
  ↓
Production-ready scene / prefab data
```
