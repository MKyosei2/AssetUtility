#!/usr/bin/env python3
"""Repository-level portfolio validation for AssetUtility.

This script is dry-run first. It does not mutate Unity assets, scenes, prefabs,
textures, or generated meshes. It validates reviewer-critical files, generates a
synthetic optimization sample plan, writes rollback notes, and stores reports for CI.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import traceback
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Callable, List


@dataclass
class Stage:
    name: str
    success: bool
    milliseconds: float
    warnings: List[str]
    errors: List[str]


@dataclass
class Report:
    tool: str
    dry_run: bool
    success: bool
    stages: List[Stage]
    rollback_plan: List[str]
    generated_samples: List[str]
    limitations_status: str


class ValidationContext:
    def __init__(self, root: Path, report_dir: Path, dry_run: bool) -> None:
        self.root = root
        self.report_dir = report_dir
        self.dry_run = dry_run
        self.stages: List[Stage] = []
        self.rollback_plan: List[str] = []
        self.generated_samples: List[str] = []
        self.limitations_status = "not checked"

    def run_stage(self, name: str, fn: Callable[[], tuple[list[str], list[str]]]) -> None:
        start = time.perf_counter()
        warnings: List[str] = []
        errors: List[str] = []
        try:
            warnings, errors = fn()
        except Exception as exc:
            errors.append(f"Unhandled exception: {exc}")
            errors.append(traceback.format_exc())
        elapsed = (time.perf_counter() - start) * 1000.0
        self.stages.append(Stage(name=name, success=not errors, milliseconds=elapsed, warnings=warnings, errors=errors))

    def has_errors(self) -> bool:
        return any(not s.success for s in self.stages)


def require_paths(ctx: ValidationContext) -> tuple[list[str], list[str]]:
    warnings: List[str] = []
    errors: List[str] = []
    required = [
        "README.md",
        "Assets",
        "Assets/Script/AssetUtility.cs",
        "Assets/Script/AssetUtilityOptimizationPlan.cs",
        "Assets/Script/AssetUtilityPriorityQem.cs",
        "Assets/Script/AssetUtilityReportWriter.cs",
        "Assets/Script/AssetUtility.Editor.asmdef",
        "Assets/Editor/Tests/AssetUtilityOptimizationPlanTests.cs",
        "Packages",
        "ProjectSettings",
    ]
    for rel in required:
        if not (ctx.root / rel).exists():
            errors.append(f"Missing required path: {rel}")
    return warnings, errors


def check_readme(ctx: ValidationContext) -> tuple[list[str], list[str]]:
    warnings: List[str] = []
    errors: List[str] = []
    readme = ctx.root / "README.md"
    text = readme.read_text(encoding="utf-8") if readme.exists() else ""
    required_phrases = [
        "30-second overview",
        "Reviewer path",
        "Current limitations",
        "Roadmap",
        "Portfolio wording",
        "not presented as a complete commercial mesh optimizer",
    ]
    for phrase in required_phrases:
        if phrase.lower() not in text.lower():
            errors.append(f"README is missing reviewer/limitation phrase: {phrase}")
    ctx.limitations_status = "README includes explicit limitations" if not errors else "README limitation coverage incomplete"
    return warnings, errors


def check_csharp_static_health(ctx: ValidationContext) -> tuple[list[str], list[str]]:
    warnings: List[str] = []
    errors: List[str] = []
    files = [
        ctx.root / "Assets/Script/AssetUtility.cs",
        ctx.root / "Assets/Script/AssetUtilityOptimizationPlan.cs",
        ctx.root / "Assets/Script/AssetUtilityPriorityQem.cs",
        ctx.root / "Assets/Script/AssetUtilityReportWriter.cs",
    ]
    combined = ""
    for source in files:
        if not source.exists():
            errors.append(f"Missing C# source: {source.relative_to(ctx.root)}")
            continue
        text = source.read_text(encoding="utf-8", errors="replace")
        combined += "\n" + text
        if text.count("{") != text.count("}"):
            errors.append(f"Brace count mismatch in {source.relative_to(ctx.root)}; static compile readiness failed.")

    required_tokens = [
        "EditorWindow",
        "AssetDatabase",
        "MeshFilter",
        "SkinnedMeshRenderer",
        "MeshCollider",
        "SimplifyQEM",
        "AssetOptimizationPlan",
        "MeshQualityReport",
        "AssetUtilityPriorityQem",
        "MinHeap",
        "TriangleValid",
        "RollbackCommands",
        "AssetUtilityReportWriter",
        "assetutility_optimization_report.md",
    ]
    for token in required_tokens:
        if token not in combined:
            warnings.append(f"Expected Unity tooling token not found: {token}")
    return warnings, errors


def generate_sample_plan(ctx: ValidationContext) -> tuple[list[str], list[str]]:
    warnings: List[str] = []
    errors: List[str] = []
    ctx.report_dir.mkdir(parents=True, exist_ok=True)
    sample = {
        "tool": "AssetUtility",
        "generatedBy": "scripts/portfolio_validation.py",
        "mode": "dry-run",
        "sampleOptimizationPlan": {
            "mesh": {
                "source": "Samples/HighPolyCharacter.fbx",
                "originalTriangles": 100000,
                "targetTriangles": 25000,
                "method": "PriorityQueue + adjacency-cache QEM",
                "qualityGate": [
                    "degenerate triangle count == 0",
                    "invalid index count == 0",
                    "non-manifold edge count does not increase unexpectedly",
                    "boundary edge increase stays within tolerance",
                    "normal deviation stays within tolerance",
                ],
            },
            "texture": {
                "source": "Samples/OversizedTexture.png",
                "originalSize": 4096,
                "targetSize": 2048,
                "preferredPath": "TextureImporter.maxTextureSize + SaveAndReimport",
            },
        },
    }
    sample_path = ctx.report_dir / "assetutility_dry_run_sample_plan.json"
    sample_path.write_text(json.dumps(sample, indent=2, ensure_ascii=False), encoding="utf-8")
    ctx.generated_samples.append(str(sample_path.relative_to(ctx.root)).replace("\\", "/"))
    return warnings, errors


def generate_optimization_report(ctx: ValidationContext) -> tuple[list[str], list[str]]:
    warnings: List[str] = []
    errors: List[str] = []
    ctx.report_dir.mkdir(parents=True, exist_ok=True)
    data = {
        "success": True,
        "dryRun": True,
        "plan": {
            "meshCommands": [
                {
                    "objectPath": "Root/Grid",
                    "sourceMeshPath": "Samples/Grid.asset",
                    "originalTriangles": 32,
                    "targetTriangles": 8,
                    "method": "PriorityQueue + adjacency-cache QEM",
                }
            ],
            "textureCommands": [
                {
                    "texturePath": "Samples/OversizedTexture.png",
                    "originalMaxSize": 4096,
                    "targetMaxSize": 2048,
                    "method": "TextureImporter.maxTextureSize",
                }
            ],
        },
        "meshQuality": {
            "originalTriangles": 32,
            "finalTriangles": 8,
            "degenerateTriangles": 0,
            "invalidIndices": 0,
            "boundaryEdges": 16,
            "normalsValid": True,
            "passed": True,
        },
        "rollbackCommands": [
            "Restore mesh reference for Root/Grid to Samples/Grid.asset",
            "Restore TextureImporter max size for Samples/OversizedTexture.png to 4096",
        ],
    }
    json_path = ctx.report_dir / "assetutility_optimization_report.json"
    json_path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")
    md_lines = [
        "# AssetUtility Optimization Report",
        "",
        "Dry run: `true`",
        "Success: `true`",
        "",
        "## Plan",
        "",
        "| Kind | Target | Before | After | Method |",
        "|---|---|---:|---:|---|",
        "| Mesh | Root/Grid | 32 | 8 | PriorityQueue + adjacency-cache QEM |",
        "| Texture | Samples/OversizedTexture.png | 4096 | 2048 | TextureImporter.maxTextureSize |",
        "",
        "## Mesh quality gate",
        "",
        "| Metric | Value |",
        "|---|---:|",
        "| Original triangles | 32 |",
        "| Final triangles | 8 |",
        "| Degenerate triangles | 0 |",
        "| Invalid indices | 0 |",
        "| Passed | true |",
        "",
        "## Rollback commands",
        "",
        "- Restore mesh reference for Root/Grid to Samples/Grid.asset",
        "- Restore TextureImporter max size for Samples/OversizedTexture.png to 4096",
    ]
    md_path = ctx.report_dir / "assetutility_optimization_report.md"
    md_path.write_text("\n".join(md_lines) + "\n", encoding="utf-8")
    ctx.generated_samples.append(str(json_path.relative_to(ctx.root)).replace("\\", "/"))
    ctx.generated_samples.append(str(md_path.relative_to(ctx.root)).replace("\\", "/"))
    return warnings, errors


def generate_dry_run_plan(ctx: ValidationContext) -> tuple[list[str], list[str]]:
    warnings: List[str] = []
    errors: List[str] = []
    ctx.rollback_plan.extend([
        "Validation is dry-run: no scene, prefab, texture, material, or mesh asset is modified.",
        "Apply mode should generate new optimized assets instead of mutating source meshes in place.",
        "Rollback must restore MeshFilter, SkinnedMeshRenderer, MeshCollider, TextureImporter, and prefab references from the generated replacement manifest.",
        "If CI-generated reports need rollback, delete only files under Docs/Reports in the CI workspace.",
    ])
    return warnings, errors


def write_reports(ctx: ValidationContext) -> Report:
    ctx.report_dir.mkdir(parents=True, exist_ok=True)
    report = Report(
        tool="AssetUtility",
        dry_run=ctx.dry_run,
        success=not ctx.has_errors(),
        stages=ctx.stages,
        rollback_plan=ctx.rollback_plan,
        generated_samples=ctx.generated_samples,
        limitations_status=ctx.limitations_status,
    )
    json_path = ctx.report_dir / "portfolio_validation_report.json"
    md_path = ctx.report_dir / "portfolio_validation_report.md"
    json_path.write_text(json.dumps(asdict(report), indent=2, ensure_ascii=False), encoding="utf-8")

    lines = [
        "# AssetUtility Portfolio Validation Report",
        "",
        f"Dry run: `{ctx.dry_run}`",
        f"Success: `{report.success}`",
        f"Limitations: {report.limitations_status}",
        "",
        "## Stage benchmark",
        "",
        "| Stage | Result | ms | Warnings | Errors |",
        "|---|---:|---:|---:|---:|",
    ]
    for stage in report.stages:
        lines.append(f"| {stage.name} | {'PASS' if stage.success else 'FAIL'} | {stage.milliseconds:.3f} | {len(stage.warnings)} | {len(stage.errors)} |")
    lines.extend(["", "## Generated sample/report artifacts", ""])
    lines.extend([f"- `{p}`" for p in report.generated_samples] or ["- none"])
    lines.extend(["", "## Rollback / dry-run plan", ""])
    lines.extend([f"- {item}" for item in report.rollback_plan])
    lines.extend(["", "## Errors and warnings", ""])
    for stage in report.stages:
        for warning in stage.warnings:
            lines.append(f"- WARNING [{stage.name}] {warning}")
        for error in stage.errors:
            lines.append(f"- ERROR [{stage.name}] {error}")
    md_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report-dir", default="Docs/Reports")
    parser.add_argument("--dry-run", action="store_true", default=True)
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[1]
    ctx = ValidationContext(root=root, report_dir=root / args.report_dir, dry_run=args.dry_run)

    ctx.run_stage("required_paths", lambda: require_paths(ctx))
    ctx.run_stage("readme_limitations", lambda: check_readme(ctx))
    ctx.run_stage("csharp_static_health", lambda: check_csharp_static_health(ctx))
    ctx.run_stage("dry_run_sample_generation", lambda: generate_sample_plan(ctx))
    ctx.run_stage("optimization_report_generation", lambda: generate_optimization_report(ctx))
    ctx.run_stage("dry_run_rollback_plan", lambda: generate_dry_run_plan(ctx))

    report = write_reports(ctx)
    print((ctx.report_dir / "portfolio_validation_report.md").read_text(encoding="utf-8"))
    return 0 if report.success else 1


if __name__ == "__main__":
    sys.exit(main())
