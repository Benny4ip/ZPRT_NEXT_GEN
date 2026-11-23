# Закреплённая версия движка (engine-lock)

Сборка релиза не должна подтягивать «любой» архив с движком без проверки.

Файл [`tools/engine-lock.json`](../tools/engine-lock.json) фиксирует:

- репозиторий и тег Flowseal;
- имя ассета и URL;
- SHA256 zip-архива;
- SHA256 `bin/winws.exe` (для локального `-EnginePath`).

## Обновление lock

1. Выбрать новый тег на [Flowseal/zapret-discord-youtube releases](https://github.com/Flowseal/zapret-discord-youtube/releases).
2. Взять digest zip (GitHub API / страница релиза) или посчитать локально:
   `Get-FileHash .\zapret-discord-youtube-X.Y.Z.zip -Algorithm SHA256`
3. Распаковать и посчитать хеш `bin\winws.exe`.
4. Обновить `tools/engine-lock.json` и при необходимости `presets/presets.json` → `engineVersion`.
5. Прогнать `tools/Build-Release.ps1` — mismatch хеша валит сборку.
