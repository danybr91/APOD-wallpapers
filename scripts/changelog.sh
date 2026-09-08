#!/bin/bash

LAST_TAG=$(git describe --tags --abbrev=0 2>/dev/null)

if [ -z "$LAST_TAG" ]; then
  echo "No se encontraron tags en el repositorio." >&2
  exit 1
fi

git log "${LAST_TAG}..HEAD" --pretty=format:"- %s"
echo
