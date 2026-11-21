# Упаковка для менеджеров пакетов

Манифесты для распространения ZPRT NEXT GEN через winget и scoop.
Официальные репозитории принимают пакеты отдельно — эти файлы готовы к PR.

## Scoop (сейчас)

Из корня любого scoop-бакета или локально:

```powershell
scoop install .\packaging\scoop\zprt-next-gen.json
```

Или добавьте свой бакет и скопируйте `zprt-next-gen.json`.

## Winget (после мержа в winget-pkgs)

Локальная проверка манифеста:

```powershell
winget validate packaging\winget\Benny4ip.ZPRTNextGen
winget install --manifest packaging\winget\Benny4ip.ZPRTNextGen
```

Публикация: PR в [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)
по пути `manifests/b/Benny4ip/ZPRTNextGen/<version>/`.

После нового релиза обновите `PackageVersion`, URL и `InstallerSha256`
(SHA-256 portable exe / zip из GitHub Releases).

## Какой артефакт

| Канал   | Файл |
|----------|------|
| winget   | `*-portable-win-x64.exe` |
| scoop    | `*-portable-win-x64.zip` |
| сайт/ручная установка | standard zip или portable |
