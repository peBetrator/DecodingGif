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
                Name: "Основы формата GIF",
                Steps:
                [
                    new TutorialStep(
                        Title: "1. Структура файла целиком",
                        Description: "Этот сценарий покажет, как GIF хранит метаданные, палитры, кадры и сжатые данные.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.EnsureFileLoadedHint]),
                    new TutorialStep(
                        Title: "2. Сигнатура и версия",
                        Description: "Первые 6 байт: сигнатура GIF + версия (обычно GIF89a).",
                        HighlightRange: new GifByteRange(GifBlockKind.Header, "Header", 0, 6),
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "3. Логический экран",
                        Description: "Следующие 7 байт: размер канвы, флаги палитры, цветовое разрешение и индекс фона.",
                        HighlightRange: new GifByteRange(GifBlockKind.LogicalScreenDescriptor, "Logical Screen Descriptor", 6, 7),
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "4. Глобальная палитра (GCT)",
                        Description: "GCT хранит RGB-цвета, к которым обращаются индексы пикселей в кадрах.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.NavigateToGlobalColorTable, TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "5. Graphic Control Extension",
                        Description: "GCE задает поведение кадра: задержку, прозрачность и disposal method.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.SelectFirstFrame, TutorialActionType.NavigateToFirstGraphicControlExtension]),
                    new TutorialStep(
                        Title: "6. Дескриптор изображения",
                        Description: "Image Descriptor определяет область кадра: позицию, размер и флаги локальной палитры.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.SelectFirstFrame]),
                    new TutorialStep(
                        Title: "7. Блок Image Data",
                        Description: "Здесь лежит LZW-сжатый поток, разбитый на sub-block’и переменной длины.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.NavigateToFirstImageData]),
                    new TutorialStep(
                        Title: "8. Memory Layout",
                        Description: "Переключитесь на Memory Layout, чтобы увидеть, какие блоки занимают место в файле.",
                        HighlightRange: null,
                        TabToShow: 2,
                        Actions: []),
                    new TutorialStep(
                        Title: "9. Итог по основам",
                        Description: "Теперь вы видите цепочку: Header -> LSD -> палитры/расширения -> кадры -> Image Data -> Trailer.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [])
                ]),
            new TutorialScenario(
                Id: "lzw",
                Name: "Погружение в LZW",
                Steps:
                [
                    new TutorialStep(
                        Title: "1. Где лежит сжатый поток",
                        Description: "Сначала подсветим первый Image Data блок. Это исходный LZW-поток для выбранного кадра.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: [TutorialActionType.SelectFirstFrame, TutorialActionType.NavigateToFirstImageData]),
                    new TutorialStep(
                        Title: "2. Автопереход в LZW",
                        Description: "Открываем вкладку LZW и запускаем сессию анализа: формируется стартовое состояние декомпрессора.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.StartLzwVisualization]),
                    new TutorialStep(
                        Title: "3. Минимальный размер кода",
                        Description: "Первый байт Image Data задает LZW minimum code size. От него зависит стартовая ширина кодов.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: []),
                    new TutorialStep(
                        Title: "4. Базовый словарь",
                        Description: "На старте словарь содержит базовые односимвольные значения и служебные коды Clear/EOI.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: []),
                    new TutorialStep(
                        Title: "5. Первый шаг декодирования",
                        Description: "Выполним один шаг: читаем код, получаем выходные байты, обновляем историю.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "6. Второй шаг: контекст",
                        Description: "Еще один шаг помогает увидеть, как предыдущая строка влияет на добавление нового элемента в словарь.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "7. Рост словаря",
                        Description: "Продолжаем шаги и наблюдаем рост количества записей в таблице кодов.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "8. Изменение code size",
                        Description: "Когда словарь достигает порога, рабочая ширина кода увеличивается. Это видно в статистике.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.AdvanceLzwStep]),
                    new TutorialStep(
                        Title: "9. Анализ метрик",
                        Description: "Смотрите на показатели: Input bytes, Output bytes, Progress, Dictionary size, Current code size.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: []),
                    new TutorialStep(
                        Title: "10. Декодирование до конца",
                        Description: "Запускаем выполнение до завершения и оцениваем итоговые параметры распаковки.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [TutorialActionType.CompleteLzwDecompression]),
                    new TutorialStep(
                        Title: "11. Что это дает в практике",
                        Description: "Теперь вы можете диагностировать проблемы потока: обрывы sub-block, неверный code size, аномальный рост словаря.",
                        HighlightRange: null,
                        TabToShow: 3,
                        Actions: [])
                ]),
            new TutorialScenario(
                Id: "color",
                Name: "Магия цвета",
                Steps:
                [
                    new TutorialStep(
                        Title: "1. Общая палитра (GCT)",
                        Description: "Откроем глобальную палитру и посмотрим, как индексы отображаются в RGB-цвета.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.NavigateToGlobalColorTable, TutorialActionType.SwitchPaletteToGlobalMode]),
                    new TutorialStep(
                        Title: "2. Формат записи цвета",
                        Description: "Один цвет = 3 байта: R, G, B. Выделите байты и сравните со значением в панели информации.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.NavigateToGlobalColorTable]),
                    new TutorialStep(
                        Title: "3. Индексация пикселей",
                        Description: "Кадры не хранят полноценный RGB на пиксель, а используют индексы в таблице цветов.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "4. Локальная палитра (LCT)",
                        Description: "Некоторые кадры имеют свою палитру, которая временно переопределяет глобальную.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [TutorialActionType.SwitchPaletteToLocalMode, TutorialActionType.NavigateToFirstLocalColorTable]),
                    new TutorialStep(
                        Title: "5. GCT vs LCT",
                        Description: "Сравните один и тот же индекс цвета в GCT и LCT: визуальный результат может отличаться.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: []),
                    new TutorialStep(
                        Title: "6. Цвет и память",
                        Description: "Перейдем в Memory Layout и посмотрим, сколько места занимают таблицы цветов.",
                        HighlightRange: null,
                        TabToShow: 2,
                        Actions: []),
                    new TutorialStep(
                        Title: "7. Связь с кадрами",
                        Description: "Вернитесь в Tree View и проследите, какие кадры используют локальные таблицы.",
                        HighlightRange: null,
                        TabToShow: 0,
                        Actions: []),
                    new TutorialStep(
                        Title: "8. Итог по цвету",
                        Description: "Вы знаете, где лежит палитра, как читаются RGB-байты и как палитры влияют на кадры.",
                        HighlightRange: null,
                        TabToShow: 5,
                        Actions: [])
                ])
        ];
    }
}
