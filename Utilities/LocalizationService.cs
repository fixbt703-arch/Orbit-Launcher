using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OrbitLauncher.Utilities;

public static class LocalizationService
{
	private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
	{
        ["Игра с друзьями в своём мире · без аренды сервера"] = "Play in your own world with friends · no server rental",
        ["Подключиться"] = "Connect",
        ["Подключено"] = "Connected",
        ["Отключить"] = "Disconnect",
        ["Подключение…"] = "Connecting…",
        ["Не удалось обновить состояние подключения. Повторяем проверку…"] = "Could not update connection status. Retrying…",
        ["Играйте вместе в своём мире"] = "Share your world with friends",
        ["Ваш мир"] = "Your world",
        ["Запустите сборку, зайдите в мир и нажмите «Открыть для сети». Приглашение появится здесь автоматически."] = "Launch this instance, enter a world and select Open to LAN. Your invitation will appear here automatically.",
        ["Приглашение в ваш мир"] = "Your world invitation",
        ["Скопировать приглашение"] = "Copy invitation",
        ["Указать LAN-порт вручную"] = "Enter LAN port manually",
        ["LAN-порт Minecraft"] = "Minecraft LAN port",
        ["Открыть мир"] = "Share world",
        ["Мир друга"] = "Friend’s world",
        ["Вставьте приглашение Orbit Connect или код Terracotta."] = "Paste an Orbit Connect invitation or a Terracotta code.",
        ["Приглашение друга"] = "Friend’s invitation",
        ["Закрытие окна не прерывает игру"] = "Closing this window keeps your game connected",
        ["Откройте мир для сети или вставьте приглашение друга."] = "Open a world to LAN or paste your friend’s invitation.",
        ["Включите Orbit Connect в параметрах сборки и перезапустите Minecraft."] = "Enable Orbit Connect in instance settings and restart Minecraft.",
        ["Не удалось связаться с Orbit Connect. Закройте и откройте панель для повтора."] = "Could not reach Orbit Connect. Close and reopen this panel to retry.",
        ["Введите LAN-порт, показанный в чате Minecraft."] = "Enter the LAN port shown in Minecraft chat.",
        ["Скопировано."] = "Copied.",
        ["Буфер обмена занят. Выделите текст и нажмите Ctrl+C."] = "Clipboard is busy. Select the text and press Ctrl+C.",
        ["Скопировать адрес"] = "Copy address",
        ["Мир открыт"] = "World shared",
        ["Восстанавливаем маршрут…"] = "Recovering connection…",
        ["Не удалось подключиться"] = "Could not connect",
        ["Подготавливаем подключение…"] = "Preparing connection…",
        ["Готовим подключение к миру…"] = "Preparing your world…",
        ["Готовим резервное подключение…"] = "Preparing backup connection…",
        ["Мир открыт через Terracotta. Скопируйте приглашение."] = "World shared through Terracotta. Copy your invitation.",
        ["Мир открыт. Резерв Terracotta недоступен; резервные узлы EasyTier активны."] = "World shared. Terracotta is unavailable; EasyTier backup nodes are active.",
        ["Мир открыт. Резерв Terracotta готов."] = "World shared. Terracotta backup is ready.",
        ["Ищем путь к миру друга…"] = "Finding a route to your friend’s world…",
        ["Мир доступен. Подключитесь к адресу ниже в Minecraft."] = "World available. Join the address below in Minecraft.",
        ["Подключаемся через Terracotta…"] = "Connecting through Terracotta…",
        ["Мир доступен через Terracotta."] = "World available through Terracotta.",
        ["Мир закрыт. Откройте другой мир для сети."] = "World closed. Open another world to LAN.",
        ["Маршрут восстановлен."] = "Connection recovered.",
        ["Основной маршрут недоступен. Резерв Terracotta готов."] = "Primary route unavailable. Terracotta backup is ready.",
        ["Сетевой компонент остановился. Повторите подключение."] = "Network component stopped. Reconnect to retry.",
        ["Резервное соединение остановилось. Повторите подключение."] = "Backup connection stopped. Reconnect to retry.",
        ["Резерв готов. Если Minecraft отключился, войдите в мир повторно."] = "Backup ready. If Minecraft disconnected, rejoin the world.",
        ["Подключение закрыто."] = "Connection closed.",
        ["Сначала отключите текущее соединение Orbit Connect."] = "Disconnect the current Orbit Connect session first.",
        ["Этот порт не принадлежит запущенной игре."] = "This port does not belong to the running game.",
        ["Подключение заняло слишком много времени. Попробуйте ещё раз."] = "Connection timed out. Please try again.",
        ["Не удалось найти доступный маршрут. Проверьте подключение и повторите попытку."] = "No route available. Check your connection and retry.",
        ["Некорректное приглашение Orbit Connect. Скопируйте код целиком."] = "Invalid Orbit Connect invitation. Copy the entire code.",
        ["PvP HUD и оптимизация · Right Shift"] = "PvP HUD and performance · Right Shift",
		["Выберите образ для своего аккаунта Minecraft"] = "Choose a look for your Minecraft account",
		["Скин и ник вашего аккаунта Ely.by"] = "Your Ely.by skin and player name",
		["Сохраните PNG и примените скин к аккаунту Minecraft"] = "Save a PNG and apply it to your Minecraft account",
		["Где вы играете?"] = "Where do you play?",
		["Подключите аккаунт, к которому хотите применить скин."] = "Connect the account you want to apply a skin to.",
		["Для владельцев Minecraft Java Edition. Сохранённые скины доступны в вашей библиотеке."] = "For Minecraft Java Edition owners. Saved skins are available in your library.",
		["Для аккаунта без лицензии. Используйте ник Ely.by и выбирайте скин на сайте."] = "For an unlicensed account. Use your Ely.by name and choose a skin on the website.",
		["Подключить Ely.by"] = "Connect Ely.by",
		["Ваш скин Minecraft сохраняется. Изменение действует со следующего запуска игры."] = "Your Minecraft skin stays unchanged. This setting takes effect on the next game launch.",
		["Библиотека скинов"] = "Skin library",
		["Добавить PNG"] = "Add PNG",
		["Повторить загрузку библиотеки"] = "Reload library",
		["Применение скина"] = "Applying skin",
		["Нажмите «Добавить PNG», чтобы сохранить первый скин. После сохранения его можно применить к аккаунту Microsoft."] = "Select Add PNG to save your first skin. You can then apply it to your Microsoft account.",
		["Аккаунт Ely.by"] = "Ely.by account",
		["Предпросмотр головы текущего скина"] = "Preview of the current skin’s head",
		["Выбрать на Ely.by"] = "Choose on Ely.by",
		["PNG · 64 × 64 пикселя · до 192 КБ"] = "PNG · 64 × 64 pixels · up to 192 KB",
		["Как применить скин"] = "How to apply a skin",
		["Выберите образ на сайте Ely.by и нажмите «Надеть». Затем вернитесь сюда и запустите Minecraft заново."] = "Choose a skin on Ely.by and select Wear. Then return here and restart Minecraft.",
		["Сайт может попросить отдельный вход в тот же аккаунт Ely.by."] = "The website may ask you to sign in separately to the same Ely.by account.",
		["Подготовка скинов…"] = "Preparing skins…",
		["Не удалось загрузить библиотеку. Файлы скинов сохранены."] = "Could not load the library. Your skin files were kept.",
		["Скин применён к аккаунту Minecraft. Перезапустите игру, чтобы увидеть изменения."] = "Skin applied to your Minecraft account. Restart the game to see the change.",
		["Не удалось применить скин. Он сохранён в библиотеке — повторите попытку."] = "Could not apply the skin. It is saved in your library; please try again.",
		["Предпросмотр"] = "Preview",
		["Выберите PNG, чтобы увидеть скин"] = "Choose a PNG to preview the skin",
		["Не удалось войти в Ely.by. Проверьте логин, пароль и код подтверждения."] = "Could not sign in to Ely.by. Check your login, password and verification code.",
		["Укажите логин и пароль Ely.by."] = "Enter your Ely.by login and password.",
		["Код Ely.by должен содержать 6 цифр."] = "The Ely.by code must contain six digits.",
		["Слишком много попыток входа. Подождите немного и повторите."] = "Too many sign-in attempts. Wait a little and try again.",
		["PNG должен быть меньше 192 КБ."] = "The PNG must be smaller than 192 KB.",
		["Скин должен иметь размер 64×64 пикселя."] = "The skin must be 64×64 pixels.",
		["Выберите изображение скина в формате PNG."] = "Choose a skin image in PNG format.",
		["Журнал запуска"] = "Launch log",
		["Журнал появится после первого запуска этой сборки."] = "A log will appear after this instance’s first launch.",
		["Обновления лаунчера"] = "Launcher updates",
		["Проверяем новые версии при запуске"] = "New versions are checked at startup",
		["Проверить обновления"] = "Check for updates",
		["Обновить"] = "Update",
		["Сбросить поиск"] = "Clear search",
		["Попробуйте другой запрос."] = "Try another search.",
		["Играть"] = "Play",
		["К каталогу"] = "Back to catalog",
		["Миры, моды и настройки хранятся отдельно."] = "Worlds, mods and settings are kept separately.",
		["Быстрый запуск"] = "Quick launch",
		["Запустить сборку"] = "Launch instance",
		["Действия со сборкой"] = "Instance actions",
		["Настройки аккаунта"] = "Account settings",
		["Установленные файлы и каталог дополнений"] = "Installed files and add-on catalog",
		["Свернуть"] = "Minimize",
		["Развернуть"] = "Maximize",
		["НАВИГАЦИЯ"] = "NAVIGATION",
		["Библиотека"] = "Library",
		["Моды"] = "Mods",
		["Исправить"] = "Repair",
		["Проверить версии модов и установить совместимые"] = "Check mod versions and install compatible releases",
		["Исправление модов"] = "Repair mods",
		["Закрыть или остановить"] = "Close or stop",
		["Подготовка к проверке…"] = "Preparing the scan…",
		["Проверяем версии игры, загрузчик, Java и зависимости модов. Замены сохраняют резервные копии."] = "Checking game version, mod loader, Java, and mod dependencies. Replacements are backed up.",
		["Ход проверки"] = "Repair progress",
		["Остановить"] = "Stop",
		["Скины"] = "Skins",
		["Текущий"] = "Current",
		["Сохранённые скины"] = "Saved skins",
		["Новый скин"] = "New skin",
		["Библиотека пока пуста"] = "Your library is empty",
		["Добавьте PNG-скин, чтобы быстро переключаться между образами."] = "Add a PNG skin to quickly switch between looks.",
		["Удалить из библиотеки"] = "Remove from library",
		["Применить"] = "Apply",
		["Сохранён в библиотеке · ещё не применён"] = "Saved to library · not applied yet",
		["Скин из библиотеки · нажмите «Применить» для установки"] = "Library skin · click Apply to use it",
		["Выбран скин из библиотеки."] = "Library skin selected.",
		["Удаление скина"] = "Delete skin",
		["Не удалось удалить скин"] = "Failed to delete the skin",
		["Настройки"] = "Settings",
		["Ваши сборки Minecraft"] = "Your Minecraft instances",
		["Импорт"] = "Import",
		["Новая сборка"] = "New instance",
		["Поиск сборок"] = "Search instances",
		["Библиотека пуста"] = "Your library is empty",
		["Создайте сборку или импортируйте готовую."] = "Create an instance or import an existing one.",
		["Создать сборку"] = "Create instance",
		["Запустить"] = "Launch",
		["Параметры"] = "Options",
		["Изменить иконку"] = "Change icon",
		["Переименовать"] = "Rename",
		["Установить моды"] = "Install mods",
		["Создать копию"] = "Duplicate",
		["Экспортировать"] = "Export",
		["Открыть папку"] = "Open folder",
		["Удалить"] = "Delete",
		["Без аккаунта"] = "No account",
		["Аккаунт не подключён"] = "Account not connected",
		["Аккаунт без лицензии"] = "Unlicensed account",
		["Ely.by • Minecraft"] = "Ely.by • Minecraft",
		["Аккаунт без лицензии · скины Ely.by"] = "Unlicensed account · Ely.by skins",
		["Лицензионная сессия Microsoft активна"] = "Licensed Microsoft session is active",
		["Для запуска требуется Microsoft-аккаунт"] = "A Microsoft account is required to launch",
		["Войти в Microsoft"] = "Sign in with Microsoft",
		["Выйти"] = "Sign out",
		["Просмотр и управление скином Minecraft: Java Edition"] = "Preview and manage your Minecraft: Java Edition skin",
		["Библиотека скинов Minecraft: Java Edition"] = "Minecraft: Java Edition skin library",
		["Нажмите, чтобы применить"] = "Click to apply",
		["Добавьте скин в библиотеку"] = "Add a skin to your library",
		["Укажите название, PNG-файл и модель персонажа."] = "Enter a name, choose a PNG file, and select the character model.",
		["Название скина"] = "Skin name",
		["Файл не выбран"] = "No file selected",
		["Выбрать файл"] = "Choose file",
		["Модель скина"] = "Skin model",
		["Широкий"] = "Wide",
		["Классические руки Steve"] = "Classic Steve arms",
		["Стройный"] = "Slim",
		["Тонкие руки Alex"] = "Slim Alex arms",
		["Отменить"] = "Cancel",
		["Сохранить и применить"] = "Save and apply",
		["Введите название скина."] = "Enter a skin name.",
		["Выберите PNG-файл скина."] = "Choose a skin PNG file.",
		["Скин сохранён в библиотеке."] = "Skin saved to the library.",
		["Обновить"] = "Refresh",
		["Управление официальным скином доступно для аккаунта с Minecraft: Java Edition."] = "Official skin management is available for an account that owns Minecraft: Java Edition.",
		["3D-просмотр"] = "3D preview",
		["Перетаскивайте модель мышью, колёсико меняет масштаб"] = "Drag the model to rotate it; use the mouse wheel to zoom",
		["Загружаем скин…"] = "Loading skin…",
		["Сбросить вид"] = "Reset view",
		["Повернуть влево"] = "Rotate left",
		["Повернуть вправо"] = "Rotate right",
		["Текущий профиль"] = "Current profile",
		["Официальный профиль Microsoft"] = "Official Microsoft profile",
		["Файл скина"] = "Skin file",
		["Поддерживается PNG размером 64×64 пикселя."] = "A 64×64 pixel PNG is supported.",
		["Текущий скин аккаунта"] = "Current account skin",
		["Загружен из Minecraft Services"] = "Loaded from Minecraft Services",
		["Выбрать PNG"] = "Choose PNG",
		["Сохранить PNG"] = "Save PNG",
		["Модель персонажа"] = "Character model",
		["Выберите ширину рук, соответствующую разметке скина."] = "Choose the arm width that matches the skin layout.",
		["Классическая (Steve)"] = "Classic (Steve)",
		["Тонкие руки (Alex)"] = "Slim arms (Alex)",
		["Скин отправляется напрямую в официальную систему Minecraft только после нажатия кнопки «Применить»."] = "The skin is sent directly to the official Minecraft service only after you click Apply.",
		["Применить скин"] = "Apply skin",
		["Скин синхронизирован с Minecraft."] = "Skin synchronized with Minecraft.",
		["Не удалось загрузить текущий скин."] = "Failed to load the current skin.",
		["Выберите скин Minecraft"] = "Choose a Minecraft skin",
		["Локальный файл · ещё не применён"] = "Local file · not applied yet",
		["Проверьте модель в 3D и нажмите «Применить скин»."] = "Check the model in 3D and click Apply skin.",
		["Сначала загрузите скин."] = "Load a skin first.",
		["Сохранить скин"] = "Save skin",
		["Скин сохранён на компьютере."] = "Skin saved to your computer.",
		["Применяем скин…"] = "Applying skin…",
		["Применён через Minecraft Services"] = "Applied through Minecraft Services",
		["Скин успешно применён к аккаунту Minecraft."] = "Skin successfully applied to your Minecraft account.",
		["Не удалось применить скин."] = "Failed to apply the skin.",
		["Не удалось загрузить скин"] = "Failed to load the skin",
		["Не удалось открыть скин"] = "Failed to open the skin",
		["Не удалось сохранить скин"] = "Failed to save the skin",
		["Не удалось применить скин"] = "Failed to apply the skin",
		["Моды и контент"] = "Mods and content",
		["Управляйте файлами сборки или устанавливайте новые из каталога"] = "Manage instance files or install new content from the catalog",
		["Выберите сборку"] = "Select an instance",
		["Открыть каталог"] = "Open catalog",
		["Включить все"] = "Enable all",
		["Отключить все"] = "Disable all",
		["Поиск установленных файлов"] = "Search installed files",
		["Любое состояние"] = "Any state",
		["Только включённые"] = "Enabled only",
		["Только отключённые"] = "Disabled only",
		["По названию"] = "By name",
		["По дате изменения"] = "By modified date",
		["По размеру"] = "By size",
		["Здесь пока пусто"] = "Nothing here yet",
		["По фильтрам ничего не найдено"] = "No items match the filters",
		["Откройте каталог или перетащите подходящие файлы в это окно."] = "Open the catalog or drag supported files into this window.",
		["Перетащите сюда JAR, ZIP или MRPACK — файл попадёт в выбранный раздел"] = "Drop a JAR, ZIP, or MRPACK here to add it to the selected section",
		["Параметры лаунчера и аккаунта"] = "Launcher and account preferences",
		["Аккаунт Minecraft"] = "Minecraft account",
		["Вход нужен только для запуска игры"] = "Sign-in is required only to launch the game",
		["Играйте под своим ником. Подключите Ely.by во вкладке «Скины», чтобы выбрать свой образ."] = "Play with your nickname. Connect Ely.by in Skins to choose your look.",
		["Разрешить запуск без Microsoft-аккаунта"] = "Allow launching without a Microsoft account",
		["Ник и скины Ely.by настраиваются отдельно от Microsoft."] = "Ely.by nicknames and skins are managed separately from Microsoft.",
		["Ник игрока"] = "Player nickname",
		["Настроить скины Ely.by"] = "Set up Ely.by skins",
		["Вход в Ely.by"] = "Sign in to Ely.by",
		["Ваш аккаунт и скины"] = "Your account and skins",
		["Войдите в Ely.by, чтобы использовать свой ник и скин в игре."] = "Sign in to Ely.by to use your nickname and skin in the game.",
		["Ник или почта Ely.by"] = "Ely.by nickname or email",
		["Пароль"] = "Password",
		["Код из приложения аутентификации"] = "Code from your authenticator app",
		["Войти"] = "Sign in",
		["Создать аккаунт"] = "Create account",
		["Скины Ely.by"] = "Ely.by skins",
		["Скины Ely.by у других игроков"] = "Ely.by skins for other players",
		["Ваш скин Minecraft сохраняется"] = "Your Minecraft skin stays unchanged",
		["Не удалось сохранить настройку скинов"] = "Couldn't save the skin setting",
		["Скины Microsoft и Ely.by"] = "Microsoft and Ely.by skins",
		["Выберите аккаунт"] = "Choose account",
		["Скины Microsoft для лицензионного аккаунта или Ely.by для аккаунта без лицензии."] = "Microsoft skins for a licensed account, or Ely.by for an unlicensed account.",
		["Войти в Ely.by"] = "Sign in to Ely.by",
		["Выйти из Ely.by"] = "Sign out of Ely.by",
		["Ваш скин в игре"] = "Your skin in the game",
		["Загрузите свой PNG или выберите готовый скин на Ely.by. Всё управление — здесь, во вкладке «Скины»."] = "Upload your PNG or choose a skin from Ely.by. Manage everything here, in Skins.",
		["Загрузить PNG"] = "Upload PNG",
		["Открыть Ely.by"] = "Open Ely.by",
		["Установить WebView2"] = "Install WebView2",
		["Обновить скин"] = "Refresh skin",
		["К моему скину"] = "Back to my skin",
		["Повторить"] = "Retry",
		["Войдите на сайте в тот же аккаунт Ely.by. Выберите скин и нажмите «Надеть»."] = "Sign in to the same Ely.by account on the website. Choose a skin and select Wear.",
		["Подключите Ely.by, чтобы использовать свой скин."] = "Connect Ely.by to use your skin.",
		["Подключение скинов Ely.by…"] = "Connecting Ely.by skins…",
		["От 3 до 16 символов: латинские буквы, цифры и подчёркивание."] = "3 to 16 characters: Latin letters, numbers, and underscores.",
		["Память по умолчанию"] = "Default memory",
		["Минимум, МБ"] = "Minimum, MB",
		["Максимум, МБ"] = "Maximum, MB",
		["Поведение"] = "Behavior",
		["Показывать снапшоты Minecraft"] = "Show Minecraft snapshots",
		["Сворачивать лаунчер после запуска игры"] = "Minimize the launcher after game start",
		["Язык"] = "Language",
		["Язык интерфейса"] = "Interface language",
		["Русский"] = "Русский",
		["English"] = "English",
		["Оформление"] = "Appearance",
		["Фон лаунчера"] = "Launcher background",
		["Выберите изображение для главного окна. Затемнение сохраняет читаемость интерфейса."] = "Choose an image for the main window. A dark overlay keeps the interface readable.",
		["По умолчанию"] = "Default",
		["Затемнение фона"] = "Background dimming",
		["Прозрачность панелей"] = "Panel transparency",
		["Параметры применяются только к изображениям и отображаются сразу."] = "These settings apply only to image backgrounds and update immediately.",
		["Моды и зависимости"] = "Mods and dependencies",
		["Настройте, какие зависимости устанавливать вместе с выбранным модом."] = "Choose which dependencies are installed with a selected mod.",
		["Устанавливать Fabric API в новые Fabric-сборки"] = "Install Fabric API in new Fabric instances",
		["Совместимая версия будет скачана с Modrinth сразу после создания сборки"] = "A compatible version will be downloaded from Modrinth after instance creation",
		["Автоскачивание зависимых модов"] = "Automatic dependency downloads",
		["Все"] = "All",
		["Только обязательные"] = "Required only",
		["Никакие"] = "None",
		["Режим «Все» включает обязательные и необязательные зависимости. Несовместимые зависимости никогда не устанавливаются."] = "All includes required and optional dependencies. Incompatible dependencies are never installed.",
		["Сохранить настройки"] = "Save settings",
		["Настройки сохранены."] = "Settings saved.",
		["Настройте игру и загрузчик"] = "Configure the game and mod loader",
		["ОФОРМЛЕНИЕ"] = "APPEARANCE",
		["Выбрать иконку"] = "Choose icon",
		["Название сборки"] = "Instance name",
		["Отдельная среда"] = "Isolated environment",
		["PvP HUD, меню на Right Shift и оптимизация"] = "PvP HUD, Right Shift menu and performance",
		["Orbit Client поддерживает только Minecraft 26.2 + Fabric."] = "Orbit Client supports Minecraft 26.2 + Fabric only.",
		["Orbit Client требует Fabric Loader 0.19.3 или новее."] = "Orbit Client requires Fabric Loader 0.19.3 or newer.",
		["Minecraft 26.2 отсутствует в списке версий. Обновите список и попробуйте снова."] = "Minecraft 26.2 is missing from the version list. Refresh the list and try again.",
		["Не удалось найти Fabric Loader 0.19.3 или новее для Minecraft 26.2."] = "Fabric Loader 0.19.3 or newer could not be found for Minecraft 26.2.",
		["Миры, моды и настройки этой сборки будут храниться отдельно."] = "Worlds, mods, and settings for this instance are stored separately.",
		["Версия Minecraft"] = "Minecraft version",
		["Сначала выберите версию игры, затем загрузчик и его версию."] = "Select the game version, then the loader and its version.",
		["Поиск версии игры"] = "Search game versions",
		["Загрузчик"] = "Loader",
		["Без модов"] = "No mods",
		["Лёгкий"] = "Lightweight",
		["Классический"] = "Classic",
		["Современный"] = "Modern",
		["Модульный"] = "Modular",
		["Версия загрузчика"] = "Loader version",
		["Для Vanilla не требуется"] = "Not required for Vanilla",
		["Память"] = "Memory",
		["Файлы игры будут загружены при первом запуске."] = "Game files will be downloaded on first launch.",
		["Отмена"] = "Cancel",
		["Параметры сборки"] = "Instance settings",
		["Основное"] = "General",
		["Название"] = "Name",
		["Дополнительно"] = "Advanced",
		["Путь к Java (необязательно)"] = "Java path (optional)",
		["ID профиля запуска"] = "Launch profile ID",
		["Оставьте пустым для автоматической установки выбранного загрузчика."] = "Leave empty to install the selected loader automatically.",
		["Производительность"] = "Performance",
		["Минимум памяти, МБ"] = "Minimum memory, MB",
		["Максимум памяти, МБ"] = "Maximum memory, MB",
		["Сохранить"] = "Save",
		["Каталог контента"] = "Content catalog",
		["Название, автор или ключевые слова"] = "Name, author, or keywords",
		["Найти"] = "Search",
		["Тип контента"] = "Content type",
		["Категория"] = "Category",
		["Поддержка стороны"] = "Environment",
		["Сортировка"] = "Sort",
		["Сбросить фильтры"] = "Reset filters",
		["Загрузка каталога…"] = "Loading catalog…",
		["Загружаем каталог…"] = "Loading catalog…",
		["Ничего не найдено"] = "Nothing found",
		["Измените запрос или фильтры."] = "Change the query or filters.",
		["Показать ещё"] = "Show more",
		["Закрыть"] = "Close",
		["Закрыть описание"] = "Close details",
		["Обновить список"] = "Refresh list",
		["Открыть текущую папку"] = "Open current folder",
		["Показать файл в папке"] = "Show file in folder",
		["Удалить файл"] = "Delete file",
		["Страница проекта"] = "Project page",
		["Загрузка…"] = "Loading…",
		["Загружаем описание…"] = "Loading description…",
		["О проекте"] = "About",
		["Описание"] = "Description",
		["Зависимости"] = "Dependencies",
		["Галерея"] = "Gallery",
		["Выберите версию"] = "Choose a version",
		["Выбор версии"] = "Choose version",
		["Выбрать версию"] = "Choose version",
		["Все версии"] = "All versions",
		["Все каналы"] = "All channels",
		["Канал"] = "Channel",
		["Бета"] = "Beta",
		["Альфа"] = "Alpha",
		["Установить версию"] = "Install version",
		["Загружаем версии…"] = "Loading versions…",
		["Для этих фильтров файлов нет."] = "No files match these filters.",
		["Ресурспаки"] = "Resource packs",
		["Шейдеры"] = "Shaders",
		["Скачанные наборы"] = "Downloaded packs",
		["Скачанный набор"] = "Downloaded pack",
		["Любая сторона"] = "Any environment",
		["Клиент"] = "Client",
		["Сервер"] = "Server",
		["Модпаки"] = "Modpacks",
		["Датапаки"] = "Data packs",
		["Мод"] = "Mod",
		["Модпак"] = "Modpack",
		["Ресурспак"] = "Resource pack",
		["Шейдер"] = "Shader",
		["Датапак"] = "Data pack",
		["По релевантности"] = "Relevance",
		["По загрузкам"] = "Downloads",
		["По популярности"] = "Popularity",
		["Недавно обновлённые"] = "Recently updated",
		["Сначала новые"] = "Newest first",
		["Все версии Minecraft"] = "All Minecraft versions",
		["Все загрузчики"] = "All loaders",
		["Все категории"] = "All categories",
		["Установить"] = "Install",
		["Скачать"] = "Download",
		["Включён"] = "Enabled",
		["Отключён"] = "Disabled",
		["Выключить"] = "Disable",
		["Включить"] = "Enable",
		["Релиз"] = "Release",
		["Снапшот"] = "Snapshot",
		["Не требуется"] = "Not required",
		["Не указана"] = "Not specified",
		["Нет совместимых версий"] = "No compatible versions",
		["Список недоступен"] = "List unavailable",
		["Готово"] = "Done",
		["Ожидание"] = "Waiting",
		["Подготовка"] = "Preparing",
		["Подготовка…"] = "Preparing…",
		["Запуск отменён"] = "Launch cancelled",
		["Отменено"] = "Cancelled",
		["Ошибка"] = "Error",
		["Установка Fabric API…"] = "Installing Fabric API…",
		["Установка Orbit Client…"] = "Installing Orbit Client…",
		["Fabric API установлен"] = "Fabric API installed",
		["Открываем Microsoft…"] = "Opening Microsoft…",
		["Продолжить с Microsoft"] = "Continue with Microsoft",
		["Войти с Microsoft"] = "Sign in with Microsoft",
		["Резервный список"] = "Fallback list",
		["Загрузка версий…"] = "Loading versions…",
		["Не удалось получить список"] = "Failed to load the list",
		["Поиск совместимого контента…"] = "Searching for compatible content…",
		["Не удалось выполнить поиск"] = "Search failed",
		["Нужен ключ CurseForge API"] = "CurseForge API key required",
		["Ключ не включён в эту сборку Orbit Launcher. Обратитесь к автору лаунчера."] = "This Orbit Launcher build does not include an API key. Contact the launcher author.",
		["Orbit Launcher столкнулся с ошибкой:"] = "Orbit Launcher encountered an error:",
		["Требуется аккаунт"] = "Account required",
		["Для запуска игры войдите в Microsoft-аккаунт, которому принадлежит Minecraft Java Edition. Библиотекой и настройками можно пользоваться без входа."] = "Sign in with the Microsoft account that owns Minecraft Java Edition to launch the game. You can use the library and settings without signing in.",
		["Другая сборка уже готовится к запуску."] = "Another instance is already being prepared for launch.",
		["Сначала выберите сборку."] = "Select an instance first.",
		["Удаление контента"] = "Delete content",
		["Удаление сборки"] = "Delete instance",
		["Microsoft Client ID не задан в конфигурации этой сборки. Обратитесь к автору лаунчера."] = "The Microsoft Client ID is not configured in this build. Contact the launcher author.",
		["Проверьте объём памяти: минимум от 512 МБ, максимум не меньше минимума."] = "Check the memory values: minimum 512 MB, and maximum must not be below minimum.",
		["Ник игрока должен содержать от 3 до 16 латинских букв, цифр или знаков подчёркивания."] = "The player nickname must contain 3 to 16 Latin letters, numbers, or underscores.",
		["Не удалось открыть изображение."] = "Failed to open the image.",
		["Укажите название и выберите версию Minecraft."] = "Enter a name and select a Minecraft version.",
		["Выберите версию загрузчика. Если список пуст, эта версия Minecraft не поддерживается выбранным загрузчиком."] = "Select a loader version. If the list is empty, the selected loader does not support this Minecraft version.",
		["Проверьте объём памяти: минимум 512 МБ, максимум не меньше минимума."] = "Check the memory values: minimum 512 MB, and maximum must not be below minimum.",
		["Название и версия Minecraft не могут быть пустыми."] = "The name and Minecraft version cannot be empty.",
		["Выберите совместимую версию загрузчика."] = "Select a compatible loader version.",
		["Проверьте значения оперативной памяти."] = "Check the memory values.",
		["Сведения о проекте"] = "Project details",
		["Не удалось скачать проект."] = "Failed to download the project.",
		["Не удалось войти в Microsoft"] = "Failed to sign in with Microsoft",
		["Не удалось запустить Minecraft"] = "Failed to launch Minecraft",
		["Не удалось удалить сборку"] = "Failed to delete the instance",
		["Не удалось импортировать сборку"] = "Failed to import the instance",
		["Не удалось экспортировать сборку"] = "Failed to export the instance",
		["Не удалось дублировать сборку"] = "Failed to duplicate the instance",
		["Не удалось изменить состояние файла"] = "Failed to change the file state",
		["Не удалось удалить файл"] = "Failed to delete the file",
		["Загрузка"] = "Downloading",
		["Установлен"] = "Installed",
		["Скачан"] = "Downloaded",
		["Описание отсутствует."] = "No description is available.",
		["Для сборки без загрузчика доступны только ресурспаки и датапаки."] = "Only resource packs and data packs are available for an instance without a mod loader."
	};

	private static readonly Lazy<IReadOnlyDictionary<string, string>> Russian = new Lazy<IReadOnlyDictionary<string, string>>(() => English.GroupBy<KeyValuePair<string, string>, string>((KeyValuePair<string, string> pair) => pair.Value, StringComparer.Ordinal).ToDictionary<IGrouping<string, KeyValuePair<string, string>>, string, string>((IGrouping<string, KeyValuePair<string, string>> group) => group.Key, (IGrouping<string, KeyValuePair<string, string>> group) => group.First().Key, StringComparer.Ordinal));

	public static string CurrentLanguage { get; private set; } = "ru";

	public static bool IsEnglish => CurrentLanguage == "en";

	public static void SetLanguage(string? language)
	{
		CurrentLanguage = (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru");
		CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo((CurrentLanguage == "en") ? "en-US" : "ru-RU");
	}

	public static string Translate(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text ?? string.Empty;
		}
		if (IsEnglish)
		{
			if (English.TryGetValue(text, out var value))
			{
				return value;
			}
			if (text.StartsWith("Доступно: ", StringComparison.Ordinal))
			{
				string text2 = text;
				return "Available: " + text2.Substring(10, text2.Length - 10);
			}
			if (text.StartsWith("Установка в: ", StringComparison.Ordinal))
			{
				string text2 = text;
				return "Installing to: " + text2.Substring(13, text2.Length - 13);
			}
			if (text.StartsWith("Модов: ", StringComparison.Ordinal))
			{
				string text2 = text;
				return "Mods: " + text2.Substring(7, text2.Length - 7);
			}
			if (text.StartsWith("Запуск: ", StringComparison.Ordinal))
			{
				string text2 = text;
				return "Played: " + text2.Substring(8, text2.Length - 8);
			}
			if (text.StartsWith("Обновлён ", StringComparison.Ordinal))
			{
				string text2 = text;
				return "Updated " + text2.Substring(9, text2.Length - 9);
			}
			if (text.StartsWith("Изменён ", StringComparison.Ordinal))
			{
				string text2 = text;
				return "Modified " + text2.Substring(8, text2.Length - 8);
			}
			return text;
		}
		if (!Russian.Value.TryGetValue(text, out var value2))
		{
			return text;
		}
		return value2;
	}

	public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
	{
		return MessageBox.Show(owner, TranslateMessage(message), Translate(caption), buttons, image);
	}

	public static string TranslateMessage(string message)
	{
		if (!IsEnglish)
		{
			return Translate(message);
		}
		if (English.TryGetValue(message, out var value))
		{
			return value;
		}
		int num = message.IndexOf("\n\n", StringComparison.Ordinal);
		if (num >= 0)
		{
			string text = TranslateMessage(message.Substring(0, num));
			string text2 = message;
			int num2 = num;
			return text + text2.Substring(num2, text2.Length - num2);
		}
        const string serviceError = "Minecraft Services вернул ошибку ";
        if (message.StartsWith(serviceError, StringComparison.Ordinal)) return "Minecraft Services returned error " + message[serviceError.Length..];
		if (message.StartsWith("Удалить файл «", StringComparison.Ordinal))
		{
			string text2 = message;
			return "Delete file “" + text2.Substring(14, text2.Length - 14).Replace('»', '”');
		}
		if (message.StartsWith("Удалить скин «", StringComparison.Ordinal))
		{
			string text2 = message;
			return "Delete skin “" + text2.Substring(14, text2.Length - 14).Replace(" из библиотеки?", " from the library?", StringComparison.Ordinal).Replace('»', '”');
		}
		if (message.StartsWith("Не удалось изменить состояние файлов: ", StringComparison.Ordinal))
		{
			string text2 = message;
			return "Failed to change file state: " + text2.Substring(37, text2.Length - 37);
		}
		if (message.StartsWith("Добавлено файлов: ", StringComparison.Ordinal))
		{
			return message.Replace("Добавлено файлов:", "Files added:", StringComparison.Ordinal).Replace("Пропущено:", "Skipped:", StringComparison.Ordinal);
		}
		return Translate(message);
	}

	public static void Apply(DependencyObject root)
	{
		HashSet<DependencyObject> visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
		ApplyCore(root, visited);
	}

	private static void ApplyCore(DependencyObject current, HashSet<DependencyObject> visited)
	{
		if (!visited.Add(current))
		{
			return;
		}
		ContentControl? contentControl;
		if (!(current is Window window))
		{
			if (!(current is TextBlock textBlock))
			{
				if (!(current is TextBox textBox))
				{
					if (!(current is HeaderedContentControl headeredContentControl))
					{
						if (!(current is HeaderedItemsControl headeredItemsControl))
						{
							contentControl = current as ContentControl;
							if (contentControl != null)
							{
								goto IL_0114;
							}
						}
						else if (headeredItemsControl.Header is string text)
						{
							headeredItemsControl.Header = Translate(text);
						}
					}
					else
					{
						if (!(headeredContentControl.Header is string text2))
						{
							contentControl = (ContentControl)current;
							goto IL_0114;
						}
						headeredContentControl.Header = Translate(text2);
					}
				}
				else if (textBox.IsReadOnly && !BindingOperations.IsDataBound(textBox, TextBox.TextProperty))
				{
					textBox.Text = Translate(textBox.Text);
				}
			}
			else if (!BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
			{
				textBlock.Text = Translate(textBlock.Text);
			}
		}
		else
		{
			window.Title = Translate(window.Title);
		}
		goto IL_0142;
		IL_0114:
		if (contentControl.Content is string text3 && !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty))
		{
			contentControl.Content = Translate(text3);
		}
		goto IL_0142;
        IL_0142:
        if (!BindingOperations.IsDataBound(current, System.Windows.Automation.AutomationProperties.NameProperty))
        {
            var name = System.Windows.Automation.AutomationProperties.GetName(current);
            if (!string.IsNullOrEmpty(name)) System.Windows.Automation.AutomationProperties.SetName(current, Translate(name));
        }
		if (current is FrameworkElement frameworkElement)
		{
			if (frameworkElement.ToolTip is string text4)
			{
				frameworkElement.ToolTip = Translate(text4);
			}
			if (frameworkElement.ContextMenu != null)
			{
				ApplyCore(frameworkElement.ContextMenu, visited);
			}
		}
		foreach (DependencyObject item in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
		{
			ApplyCore(item, visited);
		}
		if ((current is Visual || current is Visual3D) ? true : false)
		{
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
			{
				ApplyCore(VisualTreeHelper.GetChild(current, i), visited);
			}
		}
	}
}
