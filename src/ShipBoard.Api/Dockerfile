# syntax=docker/dockerfile:1

# ---- Stage 1: build e publish ----
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

# Prima solo il .csproj: finche' non cambiano i pacchetti, il restore resta in cache.
COPY ShipBoard.Api.csproj ./
RUN dotnet restore ShipBoard.Api.csproj

# Poi tutto il codice e il publish in Release.
COPY . ./
RUN dotnet publish ShipBoard.Api.csproj -c Release -o /app --no-restore

# ---- Stage 2: runtime minimale (chiseled, senza shell ne' package manager) ----
FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled AS final
WORKDIR /app

COPY --from=build /app ./

# La pipeline passa lo SHA del commit: /info mostra quale versione sta girando.
ARG APP_VERSION=dev
ENV APP_VERSION=$APP_VERSION

EXPOSE 8080

# L'immagine aspnet definisce APP_UID: il processo gira come utente non-root.
USER $APP_UID

ENTRYPOINT ["dotnet", "ShipBoard.Api.dll"]
