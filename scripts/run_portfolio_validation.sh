#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

python3 scripts/portfolio_validation.py --dry-run --report-dir Docs/Reports

echo ""
echo "Reports written to: Docs/Reports"
echo "Primary report: Docs/Reports/portfolio_validation_report.md"
echo "Optimization report: Docs/Reports/assetutility_optimization_report.md"
