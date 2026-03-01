using DecodingGif.Core.Models;

namespace DecodingGif.UI.Tutorial;

public sealed class TutorialEngine
{
    private readonly IReadOnlyList<TutorialScenario> _scenarios = BuildScenarios();

    public IReadOnlyList<TutorialScenario> Scenarios => _scenarios;
    public TutorialScenario? CurrentScenario { get; private set; }
    public int CurrentStepIndex { get; private set; } = -1;
    public TutorialStep? CurrentStep => IsRunning ? CurrentScenario!.Steps[CurrentStepIndex] : null;
    public int TotalSteps => CurrentScenario?.Steps.Count ?? 0;
    public bool IsRunning => CurrentScenario is not null && CurrentStepIndex >= 0;

    public bool Start(string scenarioId)
    {
        var scenario = _scenarios.FirstOrDefault(s => string.Equals(s.Id, scenarioId, StringComparison.OrdinalIgnoreCase));
        if (scenario is null || scenario.Steps.Count == 0)
            return false;

        CurrentScenario = scenario;
        CurrentStepIndex = 0;
        return true;
    }

    public bool MoveNext()
    {
        if (!IsRunning || CurrentScenario is null)
            return false;

        int next = CurrentStepIndex + 1;
        if (next >= CurrentScenario.Steps.Count)
            return false;

        CurrentStepIndex = next;
        return true;
    }

    public bool MovePrevious()
    {
        if (!IsRunning)
            return false;

        int previous = CurrentStepIndex - 1;
        if (previous < 0)
            return false;

        CurrentStepIndex = previous;
        return true;
    }

    public void Exit()
    {
        CurrentScenario = null;
        CurrentStepIndex = -1;
    }

    private static IReadOnlyList<TutorialScenario> BuildScenarios()
    {
        return
        [
            new TutorialScenario(
                Id: "fundamentals",
                Name: "Complete GIF Anatomy",
                Steps:
                [
                    new TutorialStep(
                        Title: "1. Добро пожаловать в анатомию GIF",
                        Description: "GIF файл — это не просто картинка, а сложная многоуровневая структура данных. Мы пошагово изучим каждый байт от заголовка до завершающего маркера. Откройте любой GIF файл для начала анализа.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.EnsureFileLoadedHint]),
                    new TutorialStep(
                        Title: "2. Сигнатура GIF89a — ключ к формату",
                        Description: "Первые 6 байт определяют тип и версию файла. Байты [47 49 46 38 39 61] в ASCII читаются как 'GIF89a'. Версия 89a поддерживает анимацию и расширения, версия 87a — только статические изображения.",
                        HighlightRange: new GifByteRange(GifBlockKind.Header, "Header", 0, 6),
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "3. Размеры виртуального холста",
                        Description: "Следующие 7 байт задают 'холст' для анимации: ширина и высота в пикселях (little-endian), флаги глобальной палитры, цветовое разрешение, индекс цвета фона и коэффициент соотношения сторон.",
                        HighlightRange: new GifByteRange(GifBlockKind.LogicalScreenDescriptor, "Logical Screen Descriptor", 6, 7),
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "4. RGB палитра для всей анимации",
                        Description: "Глобальная таблица цветов (GCT) содержит до 256 RGB цветов по 3 байта каждый. Кадры ссылаются на цвета через индексы 0-255 вместо хранения полных RGB значений. Это основа экономии места в GIF.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.NavigateToGlobalColorTable, TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "5. Используемые vs доступные цвета",
                        Description: "Система автоматически анализирует реальное использование цветов в изображении. Обратите внимание на статистику: сколько из 256 доступных цветов фактически используется. Неиспользуемые цвета — потенциал для оптимизации размера файла.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "6. Управление воспроизведением",
                        Description: "Application Extension с подписью 'NETSCAPE2.0' управляет зацикливанием анимации. Этот блок изобретен компанией Netscape в 1995 году для веб-анимаций. Содержит количество повторений цикла (0 = бесконечно).",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "7. Поведение анимационного кадра",
                        Description: "GCE предшествует каждому кадру и задает: задержку в сотых долях секунды, метод disposal (что делать с предыдущим кадром), флаг прозрачности и индекс прозрачного цвета.",
                        HighlightRange: null,
                        TabToShow: 6,
                        Actions: [TutorialActionType.NavigateToFirstGraphicControlExtension]),
                    new TutorialStep(
                        Title: "8. Позиция и размер изображения",
                        Description: "Image Descriptor описывает размещение кадра на виртуальном холсте: координаты левого верхнего угла (left, top), размеры (width, height), флаги локальной палитры и чересстрочности.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.SelectFirstFrame]),
                    new TutorialStep(
                        Title: "9. Индивидуальные цвета кадра",
                        Description: "Некоторые кадры имеют собственную локальную таблицу цветов (LCT), которая временно переопределяет глобальную. Это позволяет разным кадрам использовать разные цветовые схемы в одной анимации.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.NavigateToFirstLocalColorTable, TutorialActionType.SwitchPaletteToLocalMode]),
                    new TutorialStep(
                        Title: "10. LZW-сжатые данные изображения",
                        Description: "Блок Image Data содержит пиксели кадра, сжатые алгоритмом LZW. Данные разбиты на sub-блоки по 1-255 байт каждый. Первый байт — LZW minimum code size, определяющий параметры декомпрессии.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.NavigateToFirstImageData]),
                    new TutorialStep(
                        Title: "11. Иерархическая организация блоков",
                        Description: "Tree View показывает логическую структуру: как блоки связаны друг с другом. Заметьте группировку по кадрам и вложенность расширений. Каждый элемент имеет точное положение в файле.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "12. Связи между структурными элементами",
                        Description: "Graph View демонстрирует 5 типов связей: Sequential (порядок), Dependency (зависимость), SharedResource (общий ресурс), Temporal (временная) и DataFlow (поток данных). Это помогает понять логику формата.",
                        HighlightRange: null,
                        TabToShow: 1,
                        Actions: []),
                    new TutorialStep(
                        Title: "13. Пространственное распределение данных",
                        Description: "Memory Layout визуализирует 'географию' файла: какие типы блоков занимают сколько места. Цветовое кодирование соответствует типам блоков. Пропорции показывают эффективность структуры.",
                        HighlightRange: null,
                        TabToShow: 2,
                        Actions: []),
                    new TutorialStep(
                        Title: "14. Временная структура кадров",
                        Description: "Если файл содержит анимацию, каждый кадр состоит из связанной группы блоков: GCE -> Image Descriptor -> (опционально LCT) -> Image Data. Порядок блоков определяет последовательность воспроизведения.",
                        HighlightRange: null,
                        TabToShow: 6,
                        Actions: []),
                    new TutorialStep(
                        Title: "15. Финальный байт структуры",
                        Description: "GIF файл обязательно завершается байтом 0x3B (ASCII ';'). Этот Trailer сигнализирует парсеру об окончании данных. Файлы без Trailer считаются поврежденными. Поздравляем — теперь вы знаете полную анатомию GIF!",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [])
                ]),
            new TutorialScenario(
                Id: "lzw",
                Name: "Мастерство алгоритма LZW",
                Steps:
                [
                    new TutorialStep(
                        Title: "Теория LZW - словарная компрессия",
                        Description: "LZW — это алгоритм сжатия без потерь, который строит словарь повторяющихся последовательностей. Изобретен Лемпелем, Зивом и Велчем в 1984 году. Используется в GIF, TIFF, PDF и ZIP архивах.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.EnsureFileLoadedHint, TutorialActionType.StartLzwVisualization]),
                    new TutorialStep(
                        Title: "Подготовка к декомпрессии",
                        Description: "Первый байт Image Data содержит LZW minimum code size. Определяет размер начального словаря: 2^(min_size+1) записей. Для min_size=7 получаем словарь 0-255 плюс служебные коды Clear(256) и End(257).",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.StartLzwVisualization]),
                    new TutorialStep(
                        Title: "Битовый поток и упаковка",
                        Description: "Коды переменной длины упакованы в битовый поток с порядком LSB-first (младшие биты справа). Начальная длина кода = min_size+1 бит. Автоматически увеличивается при росте словаря до максимума 12 бит.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "Clear Code - инициализация словаря",
                        Description: "Код 256 (Clear) сбрасывает словарь к начальному состоянию и устанавливает длину кода в min_size+1. Обычно идет первым в потоке данных. После Clear все динамические коды (>257) становятся недействительными.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "Первый реальный пиксель",
                        Description: "После Clear читаем первый код изображения. Если код 0-255 — это прямая ссылка на цвет в палитре. Выводим соответствующий байт в результат и запоминаем как предыдущую строку для построения нового кода словаря.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "Рождение первого составного кода",
                        Description: "Создаем код 258 = комбинация предыдущей строки + первый символ текущей строки. Каждый новый код в словаре представляет найденный паттерн повторений. Это основа эффективности LZW.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "Динамическое увеличение длины кода",
                        Description: "При достижении 2^current_size кодов длина увеличивается на 1 бит. Например: 512 кодов -> переход с 9 на 10 бит, 1024 кода -> переход с 10 на 11 бит. Максимум для GIF: 4095 кодов (12 бит).",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "Эффект сжатия в действии",
                        Description: "Когда встречается ранее созданный код, один код заменяет несколько байтов. Коэффициент сжатия улучшается по мере роста словаря и нахождения более длинных повторяющихся последовательностей.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "Сложные паттерны и адаптация",
                        Description: "Алгоритм адаптируется к содержимому: для изображений с большими однородными областями создаются длинные коды, представляющие 5-10 пикселей. Словарь 'изучает' структуру конкретного изображения.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "Анализ статистики компрессии",
                        Description: "Наблюдаем ключевые метрики: входные байты, выходные пиксели, коэффициент сжатия, размер словаря, текущую длину кода. Эти данные показывают эффективность алгоритма для данного изображения.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "Завершение потока - End Code",
                        Description: "Код 257 (End-of-Information) сигнализирует окончание сжатых данных. Без End Code декомпрессор не знает где остановиться. После End Code могут следовать завершающие биты или padding.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.CompleteLzwDecompression]),
                    new TutorialStep(
                        Title: "Практическое применение знаний",
                        Description: "Теперь вы понимаете: как LZW адаптируется к данным, почему некоторые изображения сжимаются лучше других, как диагностировать проблемы потока (обрывы, неверный code size, аномальный рост словаря). Это основа для анализа любых LZW-сжатых форматов.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [])
                ]),
            new TutorialScenario(
                Id: "color",
                Name: "Наука o цвете и оптимизация палитр",
                Steps:
                [
                    new TutorialStep(
                        Title: "1. Цветовые модели в цифровой графике",
                        Description: "GIF использует индексированную цветовую модель: вместо хранения полных 24-битных RGB значений (3 байта на пиксель), каждый пиксель содержит 8-битный индекс (1 байт), который ссылается на цвет в палитре. Это обеспечивает значительную экономию места при ограничении в 256 цветов.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.NavigateToGlobalColorTable, TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "2. Реальное использование палитры",
                        Description: "Система автоматически анализирует Image Data через LZW-декомпрессию, определяя какие индексы палитры фактически используются в изображении. Это позволяет выявить неэффективность: часто файлы содержат полную палитру в 256 цветов при использовании лишь небольшой части.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "3. Автоматический анализ оптимизации",
                        Description: "Optimization Analyzer сканирует структуру файла и выявляет потенциал для улучшений. Основные паттерны неэффективности: избыточные цвета в палитре, дублирующие локальные таблицы, неоптимальные timing параметры анимации.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "4. Прямая модификация палитры",
                        Description: "Система позволяет изменять RGB значения цветов с мгновенным обновлением всех связанных представлений. Изменения затрагивают только байты палитры, оставляя LZW-сжатые данные изображения неизменными — это ключевое преимущество индексированной модели.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "5. Массовая обработка цветов",
                        Description: "Batch операции применяют математические преобразования к группам цветов: изменение яркости через RGB скалирование, настройка контраста относительно средних значений, сдвиг оттенка в HSV пространстве. Все операции поддерживают Undo/Redo для безопасного экспериментирования.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "6. RGB ↔ HSV для интуитивного управления",
                        Description: "RGB модель (Red, Green, Blue) удобна для хранения, но HSV (Hue, Saturation, Value) интуитивнее для художественных правок. Hue = оттенок (цветовой тон), Saturation = насыщенность, Value = яркость. Преобразования выполняются через математические формулы с сохранением точности.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: []),
                    new TutorialStep(
                        Title: "7. Семантический анализ цветовой близости",
                        Description: "Система вычисляет евклидово расстояние между цветами в RGB пространстве для поиска визуально похожих оттенков. Threshold фильтрация позволяет находить практически дублирующие цвета, которые можно объединить для упрощения палитры без визуальной потери качества.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: []),
                    new TutorialStep(
                        Title: "8. Автоматическое сокращение палитры",
                        Description: "Color quantization использует методы кластеризации (k-means) для группировки похожих цветов и выбора оптимальных представителей каждой группы. K-means++ инициализация обеспечивает лучшее качество результата. Алгоритм минимизирует цветовые искажения при заданном ограничении на количество цветов.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: []),
                    new TutorialStep(
                        Title: "9. 1-битная прозрачность в GIF",
                        Description: "GIF поддерживает binary transparency: один индекс палитры может быть помечен как прозрачный в Graphic Control Extension. Прозрачные пиксели не отображаются, позволяя видеть фон или предыдущие кадры анимации. Это создает эффект наложения без альфа-канала.",
                        HighlightRange: null,
                        TabToShow: 6,
                        Actions: []),
                    new TutorialStep(
                        Title: "10. Применение оптимизаций с метриками",
                        Description: "Финальный этап: применение всех рекомендаций оптимизатора с количественной оценкой улучшений. Система показывает экономию в байтах и процентах, сохраняя оригинальное качество изображения. Сравнение 'до/после' демонстрирует эффективность цветовой оптимизации.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [])
                ])
        ];
    }
}
