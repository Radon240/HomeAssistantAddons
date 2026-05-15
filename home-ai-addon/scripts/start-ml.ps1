$ErrorActionPreference = "Stop"
$AddonRoot = Split-Path -Parent $PSScriptRoot
$MlDir = Join-Path $AddonRoot "ml-service"

if (-not (Test-Path $MlDir)) {
    throw "ML service directory not found: $MlDir"
}

Set-Location $MlDir
Write-Host "Installing ML dependencies (if needed)..."
python -m pip install -q -r requirements.txt
Write-Host "Starting ML service on http://127.0.0.1:8100 ..."
python -m uvicorn app.main:app --host 127.0.0.1 --port 8100 --workers 1
