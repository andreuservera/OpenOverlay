---
description: Añade al CHANGELOG una entrada de versión orientada al usuario a partir de los cambios de la rama actual
argument-hint: <versión> [fecha AAAA-MM-DD]
allowed-tools: Bash(git log:*), Bash(git diff:*), Bash(git status:*), Bash(git branch:*), Bash(git merge-base:*), Bash(git rev-parse:*), Bash(date:*), Read, Edit
---

## Objetivo
Añadir al CHANGELOG una entrada para la versión **$1** que resuma, de forma breve y orientada al usuario, los cambios de la rama actual.

- Versión: `$1`
- Fecha: `$2`. Si está vacía, usa la fecha de hoy (`date +%Y-%m-%d`).
- Si no se ha indicado versión, detente y pídemela. No la deduzcas.

## Contexto
La entrada la leerán usuarios del proyecto, no desarrolladores. Debe contar qué cambia para ellos: nuevas capacidades, cambios de comportamiento, correcciones visibles y cambios incompatibles. No es un listado de commits.

## Requisitos
- Detecta la rama base (main, master o develop, la que exista y de la que parta esta rama) y analiza todos los cambios con `git log <base>..HEAD` y `git diff <base>...HEAD`. Incluye también los cambios sin commitear si los hay, y avísame de que existen.
- Si el CHANGELOG ya tiene una entrada para la versión `$1`, no la dupliques: avísame y pregúntame si quiero reemplazarla.
- Clasifica los cambios en dos grupos:
  - Visibles para el usuario: funcionalidades, cambios de comportamiento, correcciones de bugs que notaría, cambios en configuración, CLI o API pública, y breaking changes.
  - Internos: refactors, tests, CI, tipado, formato, dependencias sin efecto visible. Estos NO van al CHANGELOG.
- Agrupa los cambios relacionados en una sola entrada generalista. Por ejemplo, varios ajustes de rendimiento se convierten en "Mejoras de rendimiento en X", no en una línea por cada ajuste.
- Escribe un máximo de 5 a 7 entradas en total, priorizando lo más importante de la versión. Cada entrada debe tener una línea y estar redactada en lenguaje de usuario, sin nombres de funciones, archivos ni clases.
- Si hay algún breaking change, debe aparecer siempre, aunque haya que sacrificar otra entrada, y con una indicación breve de qué debe hacer el usuario.
- Sigue exactamente el formato, idioma, estilo de encabezado, formato de fecha y categorías que ya use el CHANGELOG existente. Si la fecha recibida está en otro formato, conviértela al del archivo. Si el CHANGELOG no existe o está vacío, usa el formato de Keep a Changelog en español con fecha AAAA-MM-DD.
- Coloca la nueva entrada en la posición que marque la convención del archivo, normalmente arriba del todo, por encima de la versión anterior y debajo de una sección "Unreleased" si existe.

## Fuera de alcance / restricciones
- No modifiques entradas de versiones anteriores.
- No cambies el número de versión en otros archivos (package.json, pyproject, etc.).
- No hagas commit.

## Criterios de aceptación
- El CHANGELOG contiene una entrada `$1` con la fecha correcta en el formato del archivo.
- La entrada tiene como máximo 7 líneas de cambios, todas comprensibles para un usuario sin leer el código.
- Ningún cambio puramente interno aparece en la entrada.
- El resto del archivo queda intacto (compruébalo con `git diff` del CHANGELOG).

## Forma de trabajar
1. Identifica la rama base y lista todos los cambios detectados, cada uno marcado como "usuario" o "interno".
2. Muéstrame la propuesta de entrada antes de escribirla en el archivo, junto con lo que has descartado o agrupado y por qué.
3. Cuando la confirme, edita el CHANGELOG y enséñame el diff final.