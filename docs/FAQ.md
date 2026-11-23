# FAQ — ZPRT NEXT GEN

## Что это?

Графическая оболочка для `winws.exe` из проекта
[Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube).
Управляет пресетами обхода, показывает статус и проверяет Discord / YouTube.

## Системные требования

- Windows 10 или 11, x64
- Права администратора (нужны для драйвера WinDivert)
- .NET у пользователя **не** нужен — релизы self-contained

## Как установить?

1. Откройте [последний релиз](https://github.com/Benny4ip/ZPRT_NEXT_GEN/releases/latest).
2. Скачайте **standard** zip (~68 МБ, папка с `engine/`) или **portable** (exe ~74 МБ / zip ~68 МБ).
   Хеши — в `SHA256SUMS.txt`.
3. Распакуйте (для zip) и запустите `ZprtNextGen.exe` от имени администратора.
4. Нажмите «Включить».

Сайт: [Benny4ip.github.io/ZPRT_NEXT_GEN](https://benny4ip.github.io/ZPRT_NEXT_GEN/)
(после включения GitHub Pages).

## Антивирус ругается. Это вирус?

Нет, это типичное ложное срабатывание для инструментов с WinDivert
(`WinDivert.dll` / `WinDivert64.sys`) и `winws.exe`. Добавьте папку приложения
в исключения. Подробнее: [CODE_SIGNING.md](CODE_SIGNING.md).

## Как обновить?

**В приложении:** Настройки → Обновления → «Проверить обновление» /
«Обновить программу». Перед заменой файла сверяется SHA256 с `SHA256SUMS.txt`
(или digest ассета на GitHub). При старте, если вышла новая версия, придёт уведомление в трей.

**Вручную:** скачайте новый архив с Releases и замените файлы (движок можно
не трогать в portable — он встроен).

## Winget / Scoop

Манифесты готовы в [`packaging/`](../packaging/README.md). Пока пакеты не в
официальных репозиториях Microsoft / community bucket — ставьте из Releases
или локальным манифестом.

## Не поднимается обход / нет интернета в проверке

- Запущено ли приложение **от администратора**?
- Не блокирует ли антивирус `winws.exe` / драйвер?
- Попробуйте другой пресет или режим «Авто».
- Закройте другой экземпляр zapret / winws, если он уже запущен извне.

## Где настройки и логи?

- Настройки: `%APPDATA%\ZPRT_NEXT_GEN\settings.json` (и `config.json`)
- Логи: `%APPDATA%\ZPRT_NEXT_GEN\logs`

## Лицензия

Оболочка — MIT. Движок и стратегии — условия upstream zapret / Flowseal.
См. [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).
