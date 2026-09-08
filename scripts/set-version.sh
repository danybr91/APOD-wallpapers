#!/bin/bash

# Todos los proyectos comparten la misma versión; se sincronizan todos.
CSPROJS=(src/APOD.Core/APOD.Core.csproj src/APOD.Console/APOD.Console.csproj src/APOD.UI/APOD.UI.csproj)

CSPROJ="${CSPROJS[0]}"

for f in "${CSPROJS[@]}"; do
  if [ ! -f "$f" ]; then
    echo "Error: No se encontró $f" >&2
    exit 1
  fi
done

OLD_VERSION=$(grep -oP '<Version>\K[^<]+' "$CSPROJ")

if [ -z "$OLD_VERSION" ]; then
  echo "Error: No se encontró la etiqueta <Version> en $CSPROJ" >&2
  exit 1
fi

if [ -z "$1" ]; then
  echo "Versión actual: $OLD_VERSION"
  echo "Uso: $0 <versión>" >&2
  echo "Ejemplo: $0 2.2.0" >&2
  exit 0
fi

NEW_VERSION="$1"

if ! [[ "$NEW_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Error: Formato de versión no válido. Usa x.y.z (ej: 2.2.0)" >&2
  exit 1
fi

for f in "${CSPROJS[@]}"; do
  sed -i "s|<Version>$OLD_VERSION</Version>|<Version>$NEW_VERSION</Version>|" "$f"
done

echo "Versión cambiada: $OLD_VERSION -> $NEW_VERSION"
