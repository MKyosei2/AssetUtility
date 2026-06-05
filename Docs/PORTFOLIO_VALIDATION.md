# AssetUtility Portfolio Validation

This document is the reviewer-facing validation entry point.

## Local dry-run validation

Linux / macOS:

```bash
bash scripts/run_portfolio_validation.sh
```

Windows PowerShell:

```powershell
./scripts/run_portfolio_validation.ps1
```

Generated reports:

```text
Docs/Reports/portfolio_validation_report.md
Docs/Reports/portfolio_validation_report.json
Docs/Reports/assetutility_dry_run_sample_plan.json
Docs/Reports/assetutility_optimization_report.md
Docs/Reports/assetutility_optimization_report.json
```

## What is validated without Unity

The dry-run validation checks:

```text
- reviewer-critical files exist
- README includes explicit limitations
- dry-run sample optimization plan is generated
- priority-queue QEM implementation exists
- adjacency-cache / triangle-valid-flag implementation tokens exist
- rollback command workflow exists
- MeshQualityReport workflow exists
- report writer workflow exists
- dry-run optimization Markdown/JSON reports are generated
```

## Unity validation

For full Unity validation, open the project in Unity and run the AssetUtility window manually:

```text
Tools > Asset Utility
```

Manual EditMode tests can be run from GitHub Actions when Unity license secrets are configured:

```text
Unity EditMode Tests
```

## Why this matters

The portfolio goal is not just to show an editor window. The goal is to show that asset optimization is treated as a safe production workflow: dry-run first, quality gate, generated report, reference replacement evidence, and rollback commands.
