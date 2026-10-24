# Universum — хаб приложений

Одно окно, из которого ставятся и обновляются все мои приложения (Volumix, Orbit и следующие).

- Версии берутся из **GitHub Releases** (`releases/latest`), ответы кэшируются с ETag — без сети показываются последние известные данные.
- Установленная версия читается из реестра по `AppId` установщика Inno Setup (`…\Uninstall\{AppId}_is1`).
- Установка и обновление — тихий запуск установщика (`/VERYSILENT`). Если приложение запущено, Universum закроет его и запустит снова после обновления.
- Если в релизе есть `SHA256SUMS.txt`, скачанный файл сверяется с ним.
- Для Windows 10 выбирается отдельная сборка, если она есть (`win10AssetPattern`).

## Добавить приложение

Допишите запись в `catalog.json` и пересоберите. Или положите свой `catalog.json` рядом с `Universum.exe` — он будет важнее встроенного.

```json
{
  "id": "myapp",
  "name": "MyApp",
  "tagline": "Короткая подпись",
  "description": "Пара предложений о приложении.",
  "repo": "kolyaka6969-ops/MyApp",
  "appId": "{GUID-из-AppId-в-.iss}",
  "exe": "MyApp.exe",
  "assetPattern": "^MyApp-Setup-[\d.]+\.exe$",
  "icon": "src/MyApp/Assets/app.ico",
  "accent": "#F97316"
}
```

## Сборка

```powershell
.\build.ps1      # publish\Universum.exe, один файл без зависимостей
```
