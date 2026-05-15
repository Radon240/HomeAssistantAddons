# syntax=docker/dockerfile:1

# --- Frontend (Vite + React) ---
FROM node:22-bookworm-slim AS frontend
WORKDIR /src/web
COPY web/package.json ./
RUN npm install
COPY web/ ./
RUN npm run build

# --- Backend publish ---
FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS publish
WORKDIR /src/HomeAiAddon.Api
COPY src/HomeAiAddon.Api/ ./
COPY --from=frontend /src/web/dist ./wwwroot
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# --- Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        python3 \
        python3-venv \
        python3-pip \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8099 \
    PYTHONUNBUFFERED=1
EXPOSE 8099

COPY --from=publish /app/publish .
COPY ml-service/ /app/ml-service/

# venv must be created in THIS image (copying /venv from python:3.12 breaks symlinks).
RUN python3 -m venv /venv \
    && /venv/bin/pip install --no-cache-dir --upgrade pip \
    && /venv/bin/pip install --no-cache-dir -r /app/ml-service/requirements.txt \
    && /venv/bin/python -c "import fastapi, pandas, sklearn, river; print('ml dependencies ok')"

COPY docker-entrypoint.sh /app/docker-entrypoint.sh
RUN chmod +x /app/docker-entrypoint.sh
ENTRYPOINT ["/app/docker-entrypoint.sh"]
