#!/bin/sh
set -e

PYTHON="/venv/bin/python"
if [ ! -x "$PYTHON" ]; then
  PYTHON="/venv/bin/python3"
fi
if [ ! -x "$PYTHON" ]; then
  echo "Python not found in /venv. Rebuild the add-on image."
  exit 1
fi

ML_LOG="${ML_LOG_PATH:-/data/logs/ml-service.log}"
mkdir -p "$(dirname "$ML_LOG")"

echo "Starting behavior analysis ML service ($PYTHON)..."
"$PYTHON" -m uvicorn app.main:app \
  --host 127.0.0.1 \
  --port 8100 \
  --workers 1 \
  --chdir /app/ml-service \
  >>"$ML_LOG" 2>&1 &
ML_PID=$!

echo "Waiting for ML service on :8100 (pid $ML_PID)..."
READY=0
i=0
while [ "$i" -lt 45 ]; do
  if "$PYTHON" -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8100/health', timeout=1)" >/dev/null 2>&1; then
    READY=1
    break
  fi
  if ! kill -0 "$ML_PID" 2>/dev/null; then
    echo "ML service process exited. Last log lines:"
    tail -n 40 "$ML_LOG" || true
    exit 1
  fi
  i=$((i + 1))
  sleep 1
done

if [ "$READY" -ne 1 ]; then
  echo "ML service did not become healthy in time. Log:"
  tail -n 40 "$ML_LOG" || true
  exit 1
fi

echo "ML service is healthy. Starting .NET API..."
exec dotnet /app/HomeAiAddon.Api.dll
