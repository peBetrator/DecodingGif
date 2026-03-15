using DecodingGif.Core.Models;

namespace DecodingGif.Core.Services;

public sealed class AnimationFPSAnalyzer
{
    private const double MinimumDelayMs = 10.0;

    public double CalculateAverageFPS(IEnumerable<GifByteRange> frames)
    {
        var delays = GetNormalizedDelays(frames);
        if (delays.Count == 0)
            return 0.0;

        double averageDelay = delays.Average();
        return averageDelay <= 0 ? 0.0 : 1000.0 / averageDelay;
    }

    public (double MinFPS, double MaxFPS) CalculateFPSRange(IEnumerable<GifByteRange> frames)
    {
        var delays = GetNormalizedDelays(frames);
        if (delays.Count == 0)
            return (0.0, 0.0);

        double minDelay = delays.Min();
        double maxDelay = delays.Max();
        return (1000.0 / maxDelay, 1000.0 / minDelay);
    }

    public double CalculateFPSConsistency(IEnumerable<GifByteRange> frames)
    {
        var fpsValues = GetFpsValues(frames);
        if (fpsValues.Count <= 1)
            return 0.0;

        double mean = fpsValues.Average();
        double variance = fpsValues.Sum(fps => Math.Pow(fps - mean, 2)) / fpsValues.Count;
        return Math.Sqrt(variance);
    }

    public List<string> GenerateFPSRecommendations(double avgFPS, double variance)
    {
        var recommendations = new List<string>();

        if (avgFPS <= 0)
        {
            recommendations.Add("Недостаточно данных FPS для анализа анимации.");
            return recommendations;
        }

        if (avgFPS < 6)
            recommendations.Add("Сильно увеличьте частоту кадров: уменьшите задержки или уберите чрезмерные паузы между кадрами.");
        else if (avgFPS < 12)
            recommendations.Add("Анимация выглядит рвано. Попробуйте снизить среднюю задержку до диапазона 40-80 мс.");
        else if (avgFPS < 24)
            recommendations.Add("Для более плавного веб-воспроизведения можно приблизиться к 18-24 FPS.");
        else
            recommendations.Add("Частота кадров уже выглядит плавно. Проверяйте, что высокий FPS действительно нужен визуально.");

        if (variance > 4.0)
            recommendations.Add("Разброс FPS высокий. Выровняйте задержки соседних кадров, чтобы убрать заметные рывки.");
        else if (variance >= 2.0)
            recommendations.Add("Есть ощутимая неравномерность тайминга. Проверьте кадры с самыми длинными и короткими задержками.");
        else if (variance >= 1.0)
            recommendations.Add("Консистентность хорошая, но небольшое выравнивание задержек сделает анимацию стабильнее.");

        if (recommendations.Count == 0)
            recommendations.Add("FPS и консистентность находятся в хорошем диапазоне.");

        return recommendations;
    }

    public FPSAnalysisResult Analyze(IEnumerable<GifByteRange> frames)
    {
        var frameList = frames
            .Where(frame => frame.DelayMs.HasValue)
            .ToList();

        double averageFps = CalculateAverageFPS(frameList);
        var (minFps, maxFps) = CalculateFPSRange(frameList);
        double variance = CalculateFPSConsistency(frameList);

        return new FPSAnalysisResult
        {
            AverageFPS = averageFps,
            MinFPS = minFps,
            MaxFPS = maxFps,
            FPSVariance = variance,
            ConsistencyRating = ClassifyConsistency(variance),
            PerformanceRating = ClassifyPerformance(averageFps),
            Recommendations = GenerateFPSRecommendations(averageFps, variance)
        };
    }

    private static List<double> GetNormalizedDelays(IEnumerable<GifByteRange> frames) =>
        frames
            .Select(frame => frame.DelayMs ?? 0)
            .Where(delay => delay >= 0)
            .Select(delay => Math.Max(MinimumDelayMs, delay))
            .ToList();

    private static List<double> GetFpsValues(IEnumerable<GifByteRange> frames) =>
        GetNormalizedDelays(frames)
            .Select(delay => 1000.0 / delay)
            .ToList();

    private static FPSPerformanceRating ClassifyPerformance(double averageFps) =>
        averageFps switch
        {
            > 24.0 => FPSPerformanceRating.Smooth,
            >= 12.0 => FPSPerformanceRating.Acceptable,
            >= 6.0 => FPSPerformanceRating.Choppy,
            _ => FPSPerformanceRating.VeryChoppy
        };

    private static FPSConsistencyRating ClassifyConsistency(double variance) =>
        variance switch
        {
            < 1.0 => FPSConsistencyRating.Excellent,
            < 2.0 => FPSConsistencyRating.Good,
            < 4.0 => FPSConsistencyRating.Fair,
            _ => FPSConsistencyRating.Poor
        };
}
