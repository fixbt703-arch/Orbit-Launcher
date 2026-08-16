# Orbit Launcher

> Современный Minecraft Java Launcher для Windows с отдельными сборками, каталогом Modrinth и CurseForge и космическим интерфейсом.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![Windows x64](https://img.shields.io/badge/Windows-x64-0078D4?logo=windows11&logoColor=white)
![License MIT](https://img.shields.io/badge/license-MIT-7C5CFC)

Orbit Launcher помогает создавать изолированные сборки Minecraft, устанавливать загрузчики и контент, управлять модами и запускать игру из одного аккуратного интерфейса. Регистрация для просмотра и настройки лаунчера не требуется — Microsoft-аккаунт можно подключить позднее.

## Возможности

### Сборки Minecraft

- отдельные папки для версий, модов, настроек и миров;
- создание, редактирование, переименование, копирование и удаление сборок;
- импорт и экспорт сборок в ZIP;
- собственная иконка, объём памяти и путь к Java для каждой сборки;
- Vanilla, Fabric, Forge, NeoForge и Quilt;
- отдельный выбор версии загрузчика после выбора версии Minecraft;
- автоматическая загрузка Minecraft и подходящей Java через CmlLib.

### Контент

- переключение каталога между Modrinth и CurseForge;
- моды, шейдеры, ресурспаки, модпаки и датапаки;
- поиск, категории, сортировка, версия Minecraft и загрузчик;
- подробная страница проекта с описанием, галереей и зависимостями;
- автоматическая установка обязательных или всех зависимостей;
- автоматическая установка совместимого Fabric API при создании Fabric-сборки;
- проверка контрольных сумм загруженных файлов;
- включение, отключение и удаление установленного контента.

### Интерфейс и аккаунт

- тёмная космическая тема и единый набор элементов управления;
- Microsoft/Xbox-вход через системный браузер;
- восстановление сессии и отображение головы скина;
- доступ к библиотеке и настройкам до входа в аккаунт;
- офлайн-режим и имя локального игрока в обычных настройках.

## Системные требования

| Компонент | Требование |
|---|---|
| ОС | Windows 10 1809 или новее, x64 |
| Сборка из исходников | .NET 8 SDK |
| Готовый EXE/Installer | .NET устанавливать не требуется |
| Интернет | Нужен для входа, установки игры, загрузчиков и контента |
| Minecraft | Для обычного запуска требуется лицензионная Minecraft Java Edition |

## Быстрый старт

1. Скачайте `OrbitLauncher-installer.exe` со страницы Releases.
2. Запустите установщик и выберите папку установки.
3. При желании создайте ярлык на рабочем столе.
4. Откройте Orbit Launcher и создайте первую сборку.
5. Для лицензионного запуска войдите в Microsoft в интерфейсе лаунчера.

> Если неподписанная тестовая сборка показывает SmartScreen, проверьте источник и контрольную сумму файла. Для публичных релизов рекомендуется Authenticode-подпись или публикация через Microsoft Store.

## Конфигурация сервисов

Служебные адреса API находятся в `Utilities/AppConfiguration.cs` и не показываются обычному пользователю. Client ID и CurseForge API key подключаются через локальный файл `Utilities/AppConfiguration.Local.cs`, который компилируется в EXE, но исключён из Git.

1. Скопируйте `Utilities/AppConfiguration.Local.cs.example` в `Utilities/AppConfiguration.Local.cs`.
2. Укажите свои значения в созданном файле.
3. Соберите EXE или Installer.

> Не удаляйте `Utilities/AppConfiguration.Local.cs` на компьютере, где собираете рабочий релиз. Не добавляйте этот файл в публичный репозиторий.

### Microsoft Client ID

Для лицензионного входа нужен идентификатор публичного приложения Microsoft Entra:

1. зарегистрируйте приложение по [инструкции CmlLib](https://cmllib.github.io/CmlLib.Core-wiki/en/auth.microsoft/xboxauthnet.game.msal/clientid/);
2. разрешите public client flow;
3. настройте redirect URI `http://localhost`;
4. вставьте Application (client) ID в `Utilities/AppConfiguration.Local.cs`.

Client ID идентифицирует приложение, но не содержит Microsoft-сессию пользователя. Каждый игрок входит под собственным аккаунтом.

### CurseForge API

Modrinth работает без ключа. Для CurseForge требуется API key, выданный для приложения или стороннего лаунчера. Укажите его в `Utilities/AppConfiguration.Local.cs` перед сборкой.

Не публикуйте рабочий API key в открытом репозитории. Секрет, помещённый в настольное приложение, технически можно извлечь; для публичного проекта безопаснее использовать собственный серверный прокси с ограничениями и квотами.

## Сборка переносимого EXE

Установите [.NET 8 SDK для Windows x64](https://dotnet.microsoft.com/download/dotnet/8.0), затем запустите:

```bat
build.bat
```

Результат:

```text
dist\OrbitLauncher.exe
```

Это self-contained single-file сборка: пользователю не нужен установленный .NET Runtime.

## Сборка установщика

Установите:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0);
- [Inno Setup 7 x64](https://jrsoftware.org/isdl.php) — также поддерживается Inno Setup 6.

Inno Setup можно установить через `winget`:

```powershell
winget install --id JRSoftware.InnoSetup.7 -e -s winget -i
```

После этого запустите:

```bat
installer-build.bat
```

Результат:

```text
dist\OrbitLauncher-installer.exe
```

Установщик:

- использует современный светлый или тёмный стиль Windows;
- поддерживает русский и английский языки;
- устанавливает приложение без обязательного запроса UAC;
- позволяет выбрать установку для текущего или всех пользователей;
- создаёт ярлык в меню «Пуск»;
- предлагает ярлык на рабочем столе;
- показывает лицензию MIT;
- поддерживает обновление поверх предыдущей версии;
- добавляет штатное удаление через «Установленные приложения» Windows;
- не удаляет сборки, миры, аккаунты и настройки пользователя при деинсталляции.

### Цифровая подпись

`installer-build.bat` создаёт неподписанный установщик. Для публичного GitHub-релиза подпишите итоговый EXE сертификатом Authenticode после сборки:

```powershell
signtool sign /fd SHA256 /td SHA256 /tr <timestamp-url> /a dist\OrbitLauncher-installer.exe
```

Самоподписанный сертификат подходит только для локального тестирования и не устраняет предупреждение SmartScreen у других пользователей.

## Где хранятся данные

Локальные данные не встраиваются в EXE или Installer:

```text
%LOCALAPPDATA%\OrbitLauncher\
├─ accounts.json    сессия Microsoft
├─ settings.json    настройки лаунчера
├─ cache\           кэш изображений
└─ instances\       сборки Minecraft
```

Перед публикацией проекта не добавляйте эту папку в Git и не копируйте её в `dist`.

## Структура проекта

```text
OrbitLauncher\
├─ Assets\                     иконки приложения и установщика
├─ Dialogs\                    диалоговые окна
├─ Models\                     модели данных
├─ Services\                   Minecraft, авторизация и API каталогов
├─ Themes\OrbitTheme.xaml      дизайн-система
├─ Utilities\                  общие вспомогательные классы
│  ├─ AppConfiguration.cs      адреса API и конфигурация сборки
│  └─ AppConfiguration.Local.cs.example  пример локальных ключей
├─ installer\OrbitLauncher.iss сценарий Inno Setup
├─ MainWindow.xaml             главное окно
├─ OrbitLauncher.csproj
├─ build.bat                   переносимый EXE
└─ installer-build.bat         установщик
```

## Зависимости

- [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) — установка и запуск Minecraft;
- CmlLib.Core.Auth.Microsoft и XboxAuthNet — Microsoft/Xbox-аутентификация;
- CmlLib.Core.Installer.Forge — установка Forge;
- [SkiaSharp](https://github.com/mono/SkiaSharp) — декодирование и обработка изображений;
- [Inno Setup](https://jrsoftware.org/isinfo.php) — сборка установщика.

Зависимости распространяются на условиях собственных лицензий.

## Безопасность

- не публикуйте Microsoft-токены, `accounts.json` и API keys;
- загружайте моды только из доверенных источников;
- проверяйте контрольные суммы релизов;
- подписывайте публичные сборки одним постоянным сертификатом;
- сообщайте об уязвимостях приватно владельцу репозитория, а не в публичном issue.

## Лицензия

Исходный код Orbit Launcher распространяется по лицензии [MIT](LICENSE).

Orbit Launcher не связан с Mojang Studios, Microsoft, Modrinth или CurseForge. Minecraft является товарным знаком Microsoft Corporation.
