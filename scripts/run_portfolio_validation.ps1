$ErrorActionPreference = 'Stop'

$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $Root

python scripts/portfolio_validation.py --dry-run --report-dir Docs/Reports

Write-Host ""
Write-Host "Reports written to: Docs/Reports"
Write-Host "Primary report: Docs/Reports/portfolio_validation_report.md"
Write-Host "Optimization report: Docs/Reports/assetutility_optimization_report.md"
