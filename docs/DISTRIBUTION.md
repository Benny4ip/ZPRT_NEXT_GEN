# Распространение и тексты для постов

Краткий набор готовых формулировок. Ссылка на скачивание всегда ведите на
[Releases](https://github.com/Benny4ip/ZPRT_NEXT_GEN/releases/latest) или на
лендинг GitHub Pages — не на сторонние зеркала без проверки хеша.

## Короткий пост (Telegram / Discord)

```
ZPRT NEXT GEN — графическая оболочка для zapret на Windows.

• один клик: подбор пресета и проверка Discord / YouTube
• standard zip или portable exe
• обновления из Настроек

Скачать: https://github.com/Benny4ip/ZPRT_NEXT_GEN/releases/latest
Сайт: https://benny4ip.github.io/ZPRT_NEXT_GEN/
FAQ: https://github.com/Benny4ip/ZPRT_NEXT_GEN/blob/main/docs/FAQ.md

Нужны права администратора. Антивирус может ругаться на WinDivert — это
ложное срабатывание, добавьте папку в исключения.
```

## Чуть длиннее (форум / комментарий)

```
Сделал русскоязычную WPF-оболочку вокруг winws из Flowseal/zapret-discord-youtube:
пресеты, статус, трей, автозапуск, проверка связи.

Релиз self-contained (без установки .NET). Portable и standard сборки на GitHub.
Код MIT: https://github.com/Benny4ip/ZPRT_NEXT_GEN
```

## Чеклист релиза (перед анонсом)

1. Собрать артефакты (`tools/Build-Release.ps1` / workflow `release`).
2. Загрузить на GitHub Release с понятными именами `*-standard-*` / `*-portable-*`.
3. Обновить хеши в `packaging/winget` и `packaging/scoop`.
4. Прописать changelog в теле релиза.
5. Прогнать `--portable-check` на portable-сборке.
6. Опубликовать пост + закрепить ссылку на FAQ и AV-гайд.

## Каналы (по приоритету)

1. GitHub Releases + сайт (`site/`)
2. Свой Telegram / Discord — с FAQ и предупреждением про AV
3. Scoop (свой бакет) → позже community
4. PR в winget-pkgs (после стабильных релизов и желательно code signing)
5. Упоминания в обсуждениях upstream — без спама, только по делу

## Чего избегать

- Непроверенные репаки и «скачать с зеркала»
- Обещаний «100% обход навсегда»
- Скрытой установки / отключения AV скриптом
