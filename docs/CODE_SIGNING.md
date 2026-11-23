# Code signing и антивирус

Подпись кода снижает (но не убирает полностью) ложные срабатывания SmartScreen
и части AV на `ZprtNextGen.exe`. Драйвер WinDivert и `winws.exe` из upstream
по-прежнему могут триггерить эвристики — это ожидаемо для DPI/divert-инструментов.

## Что нужно для подписи оболочки

1. **Сертификат** Authenticode:
   - идеален OV/EV Code Signing от доверенного CA;
   - для тестов — самоподписанный (SmartScreen всё равно будет ругаться).
2. **Инструмент** `signtool.exe` из Windows SDK.
3. Секрет в CI (не коммитить): PFX + пароль или облачный KSP (Azure Trusted Signing и т.п.).

## Локальная подпись после publish

```powershell
# Пример: подписать exe перед упаковкой
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
  /f "$env:ZPRT_CERT_PFX" /p "$env:ZPRT_CERT_PASSWORD" `
  publish\app\ZprtNextGen.exe
```

`tools/Pack-Release.ps1` и `tools/Build-Release.ps1` принимают `-Sign` и используют
переменные окружения `ZPRT_CERT_PFX` и `ZPRT_CERT_PASSWORD`. При `-Sign` подписываются
и standard, и portable `ZprtNextGen.exe`. Без секретов сборка остаётся unsigned —
это нормально до появления OV/EV или Azure Trusted Signing.

## GitHub Actions (заготовка)

В workflow `release` можно добавить шаг после `dotnet publish`:

```yaml
- name: Sign executable
  if: ${{ env.ZPRT_CERT_PFX != '' }}
  env:
    ZPRT_CERT_PFX_B64: ${{ secrets.ZPRT_CERT_PFX_B64 }}
    ZPRT_CERT_PASSWORD: ${{ secrets.ZPRT_CERT_PASSWORD }}
  shell: pwsh
  run: |
    $pfx = Join-Path $env:RUNNER_TEMP 'zprt.pfx'
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:ZPRT_CERT_PFX_B64))
    $env:ZPRT_CERT_PFX = $pfx
    tools\Pack-Release.ps1 -EngineDir engine -Version "${{ github.event.inputs.version }}" -Sign
```

Секреты храните только в GitHub Secrets / Trusted Signing. В репозиторий PFX не кладите.

## Что сказать пользователям (AV)

Краткий текст для FAQ и постов:

> Приложение использует WinDivert для работы сетевого обхода. Антивирусы часто
> помечают `winws.exe` и драйвер как подозрительные — это ложное срабатывание,
> типичное для таких инструментов. Добавьте папку с ZPRT NEXT GEN в исключения
> и скачивайте только с официального GitHub Releases.

## Подача в Microsoft / VirusTotal

- После подписи EV/OV имеет смысл отправить образцы в порталы ложных срабатываний
  крупных AV (если конкретный движок стабильно ругается).
- Для winget подпись сильно повышает шанс спокойного ревью.

## Ограничения

- Подпись **оболочки** не подписывает автоматически upstream `winws` / `.sys`.
- Не отключайте защиту Windows скриптом из дистрибутива — только инструкция
  пользователю про исключения.
