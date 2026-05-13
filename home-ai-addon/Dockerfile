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
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8099
EXPOSE 8099
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HomeAiAddon.Api.dll"]
