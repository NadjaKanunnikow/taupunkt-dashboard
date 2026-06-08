FROM node:20-alpine AS frontend
WORKDIR /src/frontend
RUN npm install -g pnpm
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY frontend ./
RUN pnpm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src
COPY backend/*.csproj ./backend/
RUN dotnet restore ./backend/Taupunkt.Api.csproj
COPY backend/ ./backend/
RUN dotnet publish ./backend/Taupunkt.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
# Alpine has no ICU libraries — use invariant globalization mode to prevent segfault on startup
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
WORKDIR /app
COPY --from=backend-build /app/publish ./
COPY --from=frontend /src/frontend/dist ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "Taupunkt.Api.dll"]
