#!/bin/sh
set -e

/venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8100 --workers 1 --app-dir /app/ml-service &
exec dotnet /app/HomeAiAddon.Api.dll
