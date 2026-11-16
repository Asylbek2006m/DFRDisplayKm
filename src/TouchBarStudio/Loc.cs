using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace TouchBarStudio
{
    /// <summary>
    /// Runtime localization. Texts are looked up by their English source string, so a missing
    /// translation simply shows the English text. Supported: en (fallback), ru, ky.
    /// The language can be changed at any time; <see cref="Changed"/> tells the UI to refresh.
    /// </summary>
    public static class Loc
    {
        public static event Action Changed;

        static string _code = "en";
        static string _setting = "auto";

        /// <summary>"auto", "en", "ru" or "ky".</summary>
        public static string Setting => _setting;

        /// <summary>The language actually in use: "en", "ru" or "ky".</summary>
        public static string Code => _code;

        public static void Set(string setting)
        {
            _setting = string.IsNullOrEmpty(setting) ? "auto" : setting;
            string code = _setting;
            if (code == "auto")
            {
                code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            }

            _code = code == "ru" || code == "ky" ? code : "en";
            Changed?.Invoke();
        }

        public static string T(string english)
        {
            if (_code == "en" || string.IsNullOrEmpty(english)) return english;
            if (Table.TryGetValue(english, out var tr))
            {
                string text = _code == "ru" ? tr[0] : tr[1];
                if (!string.IsNullOrEmpty(text)) return text;
            }

            return english;
        }

        public static string F(string english, params object[] args)
        {
            return string.Format(T(english), args);
        }

        static void A(Dictionary<string, string[]> d, string en, string ru, string ky)
        {
            d[en] = new[] { ru, ky };
        }

        static readonly Dictionary<string, string[]> Table = Build();

        static Dictionary<string, string[]> Build()
        {
            var d = new Dictionary<string, string[]>(StringComparer.Ordinal);

            // header, buttons
            A(d, "Tap a button in the preview to try it. Drop a program onto the window to add it.",
                "Нажмите кнопку в предпросмотре, чтобы проверить её. Перетащите программу в окно, чтобы добавить.",
                "Сынап көрүү үчүн алдын ала көрүүдөгү баскычты басыңыз. Кошуу үчүн программаны терезеге сүйрөңүз.");
            A(d, "Settings", "Настройки", "Жөндөөлөр");
            A(d, "Send to Touch Bar", "Отправить на Touch Bar", "Touch Bar'га жөнөтүү");
            A(d, "Clear", "Очистить", "Тазалоо");
            A(d, "Add item", "Добавить", "Кошуу");
            A(d, "App...", "Программа...", "Программа...");
            A(d, "Up", "Вверх", "Жогору");
            A(d, "Down", "Вниз", "Төмөн");
            A(d, "Remove", "Удалить", "Өчүрүү");
            A(d, "OK", "ОК", "ОК");
            A(d, "Cancel", "Отмена", "Жокко чыгаруу");

            // tray and menus
            A(d, "Open editor", "Открыть редактор", "Редакторду ачуу");
            A(d, "Restart as administrator", "Перезапустить от имени администратора", "Администратор катары кайра иштетүү");
            A(d, "Exit", "Выход", "Чыгуу");
            A(d, "Theme", "Тема", "Тема");
            A(d, "Follow Windows", "Как в Windows", "Windows сыяктуу");
            A(d, "Light", "Светлая", "Жарык");
            A(d, "Dark", "Тёмная", "Караңгы");
            A(d, "Language", "Язык", "Тил");
            A(d, "Automatic", "Автоматически", "Автоматтык");
            A(d, "Start with Windows", "Запускать вместе с Windows", "Windows менен бирге иштетүү");
            A(d, "Start minimized to tray", "Запускать свёрнутым в трей", "Трейге жыйылган абалда иштетүү");
            A(d, "Switch pages by active program", "Менять страницу по активной программе", "Активдүү программа боюнча барактарды алмаштыруу");
            A(d, "Bar background color...", "Цвет фона панели...", "Панелдин фонунун түсү...");
            A(d, "Add preset pages", "Добавить готовые страницы", "Даяр барактарды кошуу");
            A(d, "Open config folder", "Открыть папку настроек", "Жөндөөлөр папкасын ачуу");
            A(d, "About", "О программе", "Программа жөнүндө");
            A(d, "Driver: DFRDisplayKm by Bingxing Wang (MIT license).", "Драйвер: DFRDisplayKm, автор Bingxing Wang (лицензия MIT).", "Драйвер: DFRDisplayKm, автору Bingxing Wang (MIT лицензиясы).");
            A(d, "This fork and app: Aibek Malikov.", "Этот форк и приложение: Айбек Маликов.", "Бул форк жана тиркеме: Айбек Маликов.");
            A(d, "Rename...", "Переименовать...", "Атын өзгөртүү...");
            A(d, "Delete", "Удалить", "Өчүрүү");
            A(d, "Auto-switch for programs...", "Авто-переход для программ...", "Программалар үчүн авто-которуу...");

            // add item menu
            A(d, "Program or app", "Программа или приложение", "Программа же тиркеме");
            A(d, "Media key (play, volume...)", "Медиаклавиша (пуск, громкость...)", "Медиа баскыч (ойнотуу, үн...)");
            A(d, "Keyboard shortcut", "Сочетание клавиш", "Ыкчам баскычтар");
            A(d, "Command", "Команда", "Команда");
            A(d, "Website", "Сайт", "Сайт");
            A(d, "Clock", "Часы", "Саат");
            A(d, "Text label", "Текстовая метка", "Тексттик белги");
            A(d, "Switch to page", "Переход на страницу", "Баракка өтүү");
            A(d, "Lock screen", "Блокировка экрана", "Экранды кулпулоо");
            A(d, "Brightness slider", "Ползунок яркости", "Жарыктык жылдырмасы");
            A(d, "Volume slider", "Ползунок громкости", "Үн жылдырмасы");
            A(d, "Media timeline (scrubber)", "Таймлайн медиа (перемотка)", "Медиа таймлайн (алдыга-артка)");
            A(d, "Microphone mute", "Отключение микрофона", "Микрофонду өчүрүү");
            A(d, "Quick actions", "Быстрые действия", "Тез аракеттер");
            A(d, "Copy", "Копировать", "Көчүрүү");
            A(d, "Paste", "Вставить", "Чаптоо");
            A(d, "Cut", "Вырезать", "Кесүү");
            A(d, "Undo", "Отменить", "Артка кайтаруу");
            A(d, "Redo", "Повторить", "Кайталоо");
            A(d, "Select all", "Выделить всё", "Баарын тандоо");
            A(d, "Clipboard history", "История буфера", "Буфер таржымалы");
            A(d, "Screenshot", "Скриншот", "Скриншот");
            A(d, "Task view", "Обзор задач", "Тапшырмаларды көрүү");
            A(d, "Show desktop", "Рабочий стол", "Иш такта");
            A(d, "Notifications settings", "Настройки уведомлений", "Эскертме жөндөөлөрү");

            // labels created for new items / presets
            A(d, "App", "Программа", "Программа");
            A(d, "Play/Pause", "Пуск/Пауза", "Ойнотуу/Тыныгуу");
            A(d, "Shortcut", "Сочетание", "Ыкчам баскыч");
            A(d, "Web", "Сайт", "Сайт");
            A(d, "Text", "Текст", "Текст");
            A(d, "Page", "Страница", "Барак");
            A(d, "Lock", "Блок", "Кулпу");
            A(d, "Brightness", "Яркость", "Жарыктык");
            A(d, "Volume", "Громкость", "Үн");
            A(d, "Mic", "Микрофон", "Микрофон");
            A(d, "Mic off", "Микрофон выкл.", "Микрофон өчүк");
            A(d, "Main", "Главная", "Башкы");
            A(d, "Media", "Медиа", "Медиа");
            A(d, "Edit", "Правка", "Түзөтүү");
            A(d, "System", "Система", "Система");
            A(d, "Back", "Назад", "Артка");
            A(d, "Prev", "Назад", "Мурунку");
            A(d, "Next", "Далее", "Кийинки");
            A(d, "Mute", "Без звука", "Үнсүз");
            A(d, "Vol -", "Тише", "Акырын");
            A(d, "Vol +", "Громче", "Катуураак");
            A(d, "Nothing is playing", "Ничего не играет", "Эч нерсе ойнолбойт");
            A(d, "Not supported", "Не поддерживается", "Колдоого алынбайт");

            // dialogs
            A(d, "New page", "Новая страница", "Жаңы барак");
            A(d, "Rename page", "Переименовать страницу", "Баракты кайра атоо");
            A(d, "Page name", "Название страницы", "Барактын аталышы");
            A(d, "A page with this name already exists.", "Страница с таким названием уже существует.", "Мындай аталыштагы барак мурунтан бар.");
            A(d, "At least one page is required.", "Нужна хотя бы одна страница.", "Жок дегенде бир барак керек.");
            A(d, "Delete page \"{0}\"?", "Удалить страницу «{0}»?", "«{0}» барагын өчүрөсүзбү?");
            A(d, "Choose programs", "Выберите программы", "Программаларды тандаңыз");
            A(d, "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd|All files|*.*", "Программы и ярлыки|*.exe;*.lnk;*.bat;*.cmd|Все файлы|*.*", "Программалар жана ярлыктар|*.exe;*.lnk;*.bat;*.cmd|Бардык файлдар|*.*");
            A(d, "Program names separated by commas, for example: chrome, spotify. Leave empty to turn off.",
                "Имена программ через запятую, например: chrome, spotify. Оставьте пустым, чтобы отключить.",
                "Программалардын аттары үтүр менен, мисалы: chrome, spotify. Өчүрүү үчүн бош калтырыңыз.");
            A(d, "Programs for this page", "Программы для этой страницы", "Бул барак үчүн программалар");
            A(d, "No page named \"{0}\"", "Нет страницы с названием «{0}»", "«{0}» аттуу барак жок");

            // describe
            A(d, "Program: not set", "Программа: не задана", "Программа: берилген эмес");
            A(d, "Media: {0}", "Медиа: {0}", "Медиа: {0}");
            A(d, "Shortcut: {0}", "Сочетание: {0}", "Ыкчам баскыч: {0}");
            A(d, "Command: {0}", "Команда: {0}", "Команда: {0}");
            A(d, "Website: not set", "Сайт: не задан", "Сайт: берилген эмес");
            A(d, "Clock: {0}", "Часы: {0}", "Саат: {0}");
            A(d, "Open page: {0}", "Открыть страницу: {0}", "Баракты ачуу: {0}");
            A(d, "not set", "не задано", "берилген эмес");

            // status
            A(d, "Display: {0}", "Дисплей: {0}", "Дисплей: {0}");
            A(d, "connected", "подключён", "туташкан");
            A(d, "Touch: {0}", "Сенсор: {0}", "Сенсор: {0}");
            A(d, "active", "работает", "иштеп жатат");
            A(d, "not started", "не запущен", "иштетилген эмес");
            A(d, "Ran: {0}", "Выполнено: {0}", "Аткарылды: {0}");
            A(d, "Error: {0}", "Ошибка: {0}", "Ката: {0}");
            A(d, "Page: {0}", "Страница: {0}", "Барак: {0}");
            A(d, "Administrator restart was cancelled", "Перезапуск от администратора отменён", "Администратор катары кайра иштетүү жокко чыгарылды");
            A(d, "Brightness control is not available on this display", "Управление яркостью на этом дисплее недоступно", "Бул дисплейде жарыктыкты башкаруу жеткиликсиз");

            // property grid: categories, names, descriptions
            A(d, "1. General", "1. Основное", "1. Негизги");
            A(d, "2. Action", "2. Действие", "2. Аракет");
            A(d, "3. Look", "3. Вид", "3. Көрүнүшү");
            A(d, "Type", "Тип", "Түрү");
            A(d, "Label", "Подпись", "Жазуу");
            A(d, "Width", "Ширина", "Туурасы");
            A(d, "Align", "Выравнивание", "Тегиздөө");
            A(d, "Path", "Путь", "Жол");
            A(d, "Arguments", "Аргументы", "Аргументтер");
            A(d, "Hotkey", "Горячая клавиша", "Ыкчам баскыч");
            A(d, "Url", "Адрес", "Дарек");
            A(d, "TargetPage", "Целевая страница", "Максаттуу барак");
            A(d, "ClockFormat", "Формат часов", "Саат форматы");
            A(d, "Background", "Фон", "Фон");
            A(d, "ForeColor", "Цвет текста", "Тексттин түсү");
            A(d, "AccentColor", "Цвет акцента", "Акцент түсү");
            A(d, "IconPath", "Значок", "Сүрөтчө");
            A(d, "What this item does when tapped.", "Что делает элемент при нажатии.", "Басканда элемент эмне кылат.");
            A(d, "Text shown on the item.", "Текст на элементе.", "Элементтеги текст.");
            A(d, "Width in pixels. The whole bar is 2170 x 60.", "Ширина в пикселях. Вся панель 2170 x 60.", "Туурасы пикселдерде. Бүт панель 2170 x 60.");
            A(d, "Left items start at the left edge, Right items are packed to the right edge.", "Левые элементы идут от левого края, правые прижаты к правому.", "Сол элементтер сол четтен башталат, оңдору оң четке жыйналат.");
            A(d, "Program to start (type App).", "Запускаемая программа (тип App).", "Иштетүүчү программа (App түрү).");
            A(d, "Command-line arguments for the program (type App).", "Аргументы командной строки (тип App).", "Программанын буйрук саптагы аргументтери (App түрү).");
            A(d, "Media key to press (type Media).", "Медиаклавиша (тип Media).", "Басыла турган медиа баскыч (Media түрү).");
            A(d, "Keyboard shortcut (type Hotkey). Examples: ctrl+shift+esc, win+d, alt+f4, ctrl+c", "Сочетание клавиш (тип Hotkey). Примеры: ctrl+shift+esc, win+d, alt+f4, ctrl+c", "Ыкчам баскычтар (Hotkey түрү). Мисалдар: ctrl+shift+esc, win+d, alt+f4, ctrl+c");
            A(d, "Command line run with cmd.exe /c (type Command).", "Команда, выполняемая через cmd.exe /c (тип Command).", "cmd.exe /c аркылуу аткарылуучу команда (Command түрү).");
            A(d, "Address to open (type Url).", "Адрес для открытия (тип Url).", "Ачыла турган дарек (Url түрү).");
            A(d, "Name of the page to switch to (type Page).", "Название страницы для перехода (тип Page).", "Өтүүчү барактын аталышы (Page түрү).");
            A(d, ".NET date format for type Clock, e.g. HH:mm or ddd d MMM HH:mm", "Формат даты .NET для типа Clock, например HH:mm или ddd d MMM HH:mm", "Clock түрү үчүн .NET дата форматы, мисалы HH:mm же ddd d MMM HH:mm");
            A(d, "Image (png, jpg, bmp, ico) or program file used as the icon. Optional; App items use the program's own icon.", "Картинка (png, jpg, bmp, ico) или программа для значка. Необязательно; программы используют собственный значок.", "Сүрөтчө катары колдонулган сүрөт (png, jpg, bmp, ico) же программа. Милдеттүү эмес; App өзүнүн сүрөтчөсүн колдонот.");
            A(d, "Colour of the filled part of sliders and of the timeline.", "Цвет заполненной части ползунков и таймлайна.", "Жылдырмалардын жана таймлайндын толгон бөлүгүнүн түсү.");

            A(d, "Slot", "Ячейка", "Уяча");
            A(d, "Which clipboard history entry the item shows: 0 is the newest (type Clipboard).", "Какую запись истории буфера показывает элемент: 0 — самая новая (тип Clipboard).", "Элемент буфер таржымалынын кайсы жазуусун көрсөтөт: 0 — эң жаңысы (Clipboard түрү).");
            A(d, "Battery", "Батарея", "Батарея");
            A(d, "No battery", "Нет батареи", "Батарея жок");
            A(d, "Sleep", "Сон", "Уйку");
            A(d, "More", "Ещё", "Дагы");
            A(d, "Calculator", "Калькулятор", "Калькулятор");
            A(d, "Clipboard", "Буфер", "Буфер");
            A(d, "Search", "Поиск", "Издөө");
            A(d, "Power", "Питание", "Кубат");
            A(d, "selection", "выделенное", "тандалган");
            A(d, "clipboard", "буфер", "буфер");
            A(d, "Battery level", "Заряд батареи", "Батарея деңгээли");
            A(d, "Clipboard entry", "Запись буфера", "Буфер жазуусу");
            A(d, "Calculator key", "Клавиша калькулятора", "Калькулятор баскычы");
            A(d, "Calculator display (tap to copy)", "Дисплей калькулятора (нажмите — копировать)", "Калькулятор дисплейи (көчүрүү үчүн басыңыз)");
            A(d, "Search selection (Google)", "Поиск выделенного (Google)", "Тандалганды издөө (Google)");
            A(d, "Search clipboard (Bing)", "Поиск из буфера (Bing)", "Буферден издөө (Bing)");
            A(d, "Clear clipboard history", "Очистить историю буфера", "Буфер таржымалын тазалоо");
            A(d, "Clipboard is empty", "Буфер пуст", "Буфер бош");

            return d;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class LocCategoryAttribute : CategoryAttribute
    {
        public LocCategoryAttribute(string english) : base(english) { }
        protected override string GetLocalizedString(string value) => Loc.T(value);
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class LocDescriptionAttribute : DescriptionAttribute
    {
        readonly string _english;
        public LocDescriptionAttribute(string english) : base(english) { _english = english; }
        public override string Description => Loc.T(_english);
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class LocNameAttribute : DisplayNameAttribute
    {
        readonly string _english;
        public LocNameAttribute(string english) : base(english) { _english = english; }
        public override string DisplayName => Loc.T(_english);
    }
}
