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
                        Title: "1. Введение: GIF как объект структурного анализа",
                        Description: "Данный сценарий представляет собой вводный обзор возможностей системы DecodingGif применительно к исследованию формата GIF. Его цель состоит в том, чтобы показать, что GIF-файл может рассматриваться одновременно как бинарный поток данных, как иерархически организованная совокупность блоков, как временная последовательность кадров и как сжатое изображение с палитровой цветовой моделью. Откройте GIF-файл, чтобы проследить указанные уровни представления на конкретном примере.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.EnsureFileLoadedHint]),
                    new TutorialStep(
                        Title: "2. Множественные представления одного файла",
                        Description: "Система синхронно предоставляет несколько представлений одного и того же GIF-файла: Hex View отражает исходные байты, File Overview показывает компактную схему расположения данных, Preview демонстрирует результат декодирования, а информационная панель интерпретирует выбранный байт, цвет или служебное поле. Такой подход позволяет соотнести низкоуровневую организацию файла с его визуальным и семантическим содержанием.",
                        HighlightRange: new GifByteRange(GifBlockKind.Header, "Header", 0, 6),
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "3. Hex View и байтовая основа формата",
                        Description: "Hex View представляет файл в его базовой форме, то есть как последовательность байтов с указанием смещения, шестнадцатеричного значения и ASCII-интерпретации. Данное представление принципиально важно, поскольку любой структурный элемент GIF в конечном счёте задаётся конкретной байтовой последовательностью. Подсветка блоков и связанная информационная панель обеспечивают переход от исходного представления к содержательной интерпретации без потери точности.",
                        HighlightRange: new GifByteRange(GifBlockKind.LogicalScreenDescriptor, "Logical Screen Descriptor", 6, 7),
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "4. Preview и временное поведение анимации",
                        Description: "Preview и элементы управления воспроизведением позволяют соотнести структуру файла с его наблюдаемым поведением. Для анимированного GIF это особенно существенно, поскольку исследователь получает возможность анализировать не только итоговое изображение, но и последовательность кадров, изменение скорости воспроизведения, цикличность и влияние временных параметров. Тем самым бинарные данные рассматриваются в контексте результата декодирования.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.SelectFirstFrame]),
                    new TutorialStep(
                        Title: "5. Tree View и иерархия блоков GIF",
                        Description: "Tree View преобразует байтовую последовательность в логическую иерархию блоков: Header, Logical Screen Descriptor, палитры, расширения, Image Descriptor и Image Data. Это позволяет показать, что GIF обладает строгой внутренней организацией и не является неделимым массивом данных. Каждый узел дерева соответствует определённому участку файла и выполняет конкретную функциональную роль в общей структуре формата.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "6. Graph View и моделирование зависимостей",
                        Description: "Graph View представляет структуру GIF в форме графа зависимостей. В этом представлении отображаются последовательные связи, зависимости между блоками, использование общих ресурсов, временные отношения и поток данных. Подобная форма анализа полезна на более высоком уровне абстракции, когда файл рассматривается не только как перечень блоков, но и как система взаимосвязанных сущностей.",
                        HighlightRange: null,
                        TabToShow: 1,
                        Actions: []),
                    new TutorialStep(
                        Title: "7. Memory Layout и пространственное распределение данных",
                        Description: "Memory Layout визуализирует распределение структурных элементов по объёму файла. Это позволяет оценить, какие части GIF занимают основную долю памяти: заголовочные структуры, палитры, расширения или Image Data. В исследовательском контексте такое представление удобно для обсуждения эффективности хранения, плотности размещения данных и вклада различных компонентов в общий размер файла.",
                        HighlightRange: null,
                        TabToShow: 2,
                        Actions: []),
                    new TutorialStep(
                        Title: "8. Graphic Control Extension и параметры кадра",
                        Description: "Graphic Control Extension определяет параметры отдельного кадра, включая delay, transparency, disposal method и связанные служебные флаги. В системе эти параметры могут анализироваться одновременно через timeline, информационную панель и структурные блоки файла. Это позволяет проследить, каким образом конкретные служебные поля влияют на временное и визуальное поведение анимации.",
                        HighlightRange: null,
                        TabToShow: 6,
                        Actions: [TutorialActionType.NavigateToFirstGraphicControlExtension]),
                    new TutorialStep(
                        Title: "9. Глобальная палитра и индексированная цветовая модель",
                        Description: "Palette Editor позволяет исследовать глобальную таблицу цветов и тем самым наглядно продемонстрировать палитровую природу GIF. Пиксели в GIF, как правило, хранят не полный RGB-цвет, а индекс палитры, указывающий на конкретную запись в GCT. Такой способ представления обеспечивает экономию памяти и делает палитру самостоятельным объектом анализа.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.NavigateToGlobalColorTable, TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "10. Локальная палитра и вариативность кадров",
                        Description: "Некоторые кадры используют собственную локальную таблицу цветов, временно переопределяющую глобальную палитру. Это позволяет различным кадрам анимации использовать отличающиеся наборы цветов в рамках одного файла. Сопоставление global и local palette modes позволяет объяснить, каким образом формат GIF сочетает компактность хранения и гибкость представления.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.NavigateToFirstLocalColorTable, TutorialActionType.SwitchPaletteToLocalMode]),
                    new TutorialStep(
                        Title: "11. LZW Decompression как анализ внутреннего кодирования",
                        Description: "Вкладка LZW Decompression показывает, как Image Data преобразуется из сжатого битового потока в последовательность индексов палитры. Здесь можно проследить структуру битового потока, рост словаря, изменение code size, накопление output buffer и состояние пошагового декомпрессора. Это связывает теоретическое описание GIF-компрессии с наблюдаемым процессом декодирования.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.StartLzwVisualization]),
                    new TutorialStep(
                        Title: "12. Animation Properties и параметрическое редактирование",
                        Description: "Animation Properties показывает, что структура GIF может быть объектом не только анализа, но и контролируемого параметрического редактирования. Изменение свойств анимации, параметров кадров и связанных значений позволяет установить, какие именно поля файла отвечают за итоговое поведение результата. Это полезно как для практической работы, так и для экспериментальной проверки понимания формата.",
                        HighlightRange: null,
                        TabToShow: 4,
                        Actions: []),
                    new TutorialStep(
                        Title: "13. Статистика, оптимизация и цифровая форензика",
                        Description: "Дополнительные аналитические блоки позволяют оценивать GIF не только структурно, но и количественно. Optimization Suggestions выявляют потенциальные неэффективности, memory statistics характеризуют распределение ресурсов, а цифровая форензика позволяет выдвигать предположения о происхождении файла и среде его создания. Тем самым система поддерживает как формальный разбор структуры, так и интерпретацию файла в более широком техническом контексте.",
                        HighlightRange: null,
                        TabToShow: 2,
                        Actions: []),
                    new TutorialStep(
                        Title: "14. Синтез уровней представления",
                        Description: "На данном этапе становится очевидно, что одно и то же содержимое GIF может анализироваться на нескольких уровнях: байтовом, структурном, временном, цветовом и алгоритмическом. Научно-методическая ценность системы состоит в согласовании этих уровней между собой. Пользователь может проследить путь от конкретного байта к конкретному блоку, от блока — к поведению кадра, а от сжатого потока — к декодированному изображению.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "15. Итог: полная анатомия GIF в единой среде",
                        Description: "Итоговый вывод состоит в том, что GIF может изучаться как целостная система взаимосвязанных представлений: от Header и LSD до палитр, кадров, расширений и LZW-сжатых данных. DecodingGif объединяет указанные аспекты в единой среде, что делает возможным последовательный, наглядный и аргументированный анализ формата. После такого обзора можно переходить к более специализированным сценариям, посвящённым LZW, палитрам и вопросам оптимизации.",
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
                        Title: "1. Что показывает вкладка LZW",
                        Description: "Эта вкладка визуализирует декомпрессию Image Data выбранного кадра. На экране есть четыре основные панели: Code Table, Bit Stream, Output Buffer и Step Details, а снизу — статусные строки со steps, progress, input/output, предупреждениями и флагом LZW Active. Смысл режима не только в анимации шагов, но и в том, чтобы связать каждое поле интерфейса с внутренним состоянием декомпрессора.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.EnsureFileLoadedHint, TutorialActionType.StartLzwVisualization]),
                    new TutorialStep(
                        Title: "2. Откуда берутся Input и стартовые параметры",
                        Description: "Input — это не весь GIF и не весь блок Image Data, а только полезная сжатая нагрузка после байта LZW minimum code size. Сначала приложение читает первый байт блока как min code size, затем склеивает все sub-блоки в единый compressed payload. Именно этот массив показывается как входной поток, и его длина попадает в статистику как Input: N B.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.StartLzwVisualization]),
                    new TutorialStep(
                        Title: "3. Minimum code size, Clear и EOI",
                        Description: "LZW minimum code size задаёт базовый алфавит. В GIF clearCode = 2^minCodeSize, endOfInfoCode = clearCode + 1, а стартовый CodeSize равен minCodeSize + 1 бит. Например, при minCodeSize=8 словарь начинается с кодов 0-255, затем идут Clear=256 и EOI=257, а первый размер читаемого кода равен 9 бит.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "4. Как читать Bit Stream",
                        Description: "Bit Stream показывает входные байты как битовую ленту. Коды в GIF читаются в порядке LSB-first, поэтому декомпрессор берёт не целые байты, а окно длиной CodeSize бит, начиная с BitPosition. Подсветка Current window и Range показывает, какие именно биты будут интерпретированы как следующий код.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "5. Что такое Step",
                        Description: "Step — это не количество пикселей и не количество считанных кодов. В приложении Step увеличивается на 1 при каждом ExecuteNextStep, то есть на каждом переходе внутреннего автомата декомпрессии. Поэтому один код из потока может требовать несколько steps: чтение, обработку Clear, вывод последовательности, добавление записи в словарь или завершение потока.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "6. Как считается общее количество steps",
                        Description: "Поле X/Y steps использует две разные величины. X — это фактический текущий шаг из истории состояний. Y — оценка общего числа шагов: totalSteps ≈ (compressedData.Length * 8) / (minCodeSize + 1). То есть приложение делит число входных битов на стартовый размер кода. Это полезная шкала прогресса, но не точный прогноз, потому что CodeSize растёт, а разные коды требуют разного числа внутренних действий.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "7. Что означает Progress",
                        Description: "Progress считается по входному потоку, а не по выходным пикселям. Формула простая: Progress = processedBits / totalBits * 100%. processedBits берётся из текущего BitPosition, totalBits = InputBytes * 8. Поэтому progress показывает, какую долю сжатого битового потока мы уже прочитали, даже если Output Buffer растёт рывками.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "8. Input, Output и их соотношение",
                        Description: "Input в статусе — это число байтов сжатого payload, а Output — текущее число байтов в Output Buffer после декомпрессии. Для GIF эти выходные байты являются индексами палитры, обычно по одному байту на пиксель. Если Output становится заметно больше Input, значит словарь уже заменяет короткие коды более длинными последовательностями. В этом и проявляется эффект сжатия.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "9. Что показывает Code Table",
                        Description: "Code Table — это текущий словарь декомпрессора. Базовые записи указывают на одиночные значения, служебные коды Clear и EOI выделены отдельно, а динамические записи добавляются по мере чтения потока. Поле Next в Step Details показывает следующий свободный код, Dictionary size — текущее число записей, а режим Show only new codes скрывает базовую часть и оставляет только новые элементы словаря.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "10. Что показывает Output Buffer",
                        Description: "Output Buffer содержит уже декодированные байты результата. В контексте GIF это индексы палитры после LZW, а не готовые RGB-цвета. Подпись Bytes показывает текущий объём выходного буфера, а tail view выделяет самые свежие декодированные данные, чтобы было видно, какой код только что добавил новую последовательность.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "11. Как читать Step Details",
                        Description: "Step Details показывает основные поля состояния. Action — текущая стадия автомата. CurrentCode — код, который обрабатывается сейчас, PreviousCode — предыдущий код для построения новой словарной записи. CodeSize — текущая длина кода в битах, BitPosition — сколько бит уже прочитано. Clear, EOI и Next — служебные значения словаря. Dictionary size и Output показывают размеры словаря и выходного буфера. Step description кратко объясняет, что произошло на этом шаге.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "12. Рост словаря и увеличение CodeSize",
                        Description: "Когда в словаре заканчиваются коды, которые помещаются в текущий размер, CodeSize увеличивается. Например, после достижения порога 2^CodeSize следующий код уже требует на 1 бит больше, максимум до 12 бит в GIF. Поэтому поздние участки потока читаются иначе, чем ранние, а оценка общего числа steps остаётся приблизительной.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "13. Warning, LZW Active и завершение",
                        Description: "Строка Warning сообщает о практических проблемах: слишком большой кадр, большой payload, преждевременный конец данных или другие аномалии. LZW Active показывает, запущена ли текущая сессия визуализации. Когда встречается End-of-Information код или больше не хватает бит для полного кода, состояние помечается как Complete, progress останавливается, а историю steps можно продолжать листать назад и вперёд.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.CompleteLzwDecompression]),
                    new TutorialStep(
                        Title: "14. Как читать экран целиком",
                        Description: "Читать экран удобно слева направо и сверху вниз: Code Table отвечает на вопрос «что уже знает словарь», Bit Stream — «какие входные биты читаются сейчас», Output Buffer — «что уже восстановлено», Step Details — «почему это произошло», а статус внизу суммирует количественную картину: steps, input/output, progress, размер словаря, длину кода, предупреждения и активность сессии.",
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
