namespace SpikingNet.Core;

public sealed class Homeostasis
{
    // --- Цели ---
    public double ActivityTarget { get; set; } = 0.12;
    public double WeightNormTarget { get; set; } = 5.0;

    // --- Текущие значения ---
    public double ActivityLevel { get; private set; }
    public double WeightNorm { get; private set; }
    public double Discomfort { get; private set; }
    public double PreviousDiscomfort { get; private set; }
    public double Smoothing { get; set; } = 0.95;

    // --- Выходы регулятора (множители, применяются в Simulation) ---
    public double WeightDecayMultiplier { get; private set; } = 1.0;
    public double ThresholdMultiplier { get; private set; } = 1.0;

    private readonly int _windowSize;
    private readonly Queue<int> _spikeBuffer;
    private int _ticksSinceUpdate;

    public Homeostasis(int windowSize = 100)
    {
        _windowSize = windowSize;
        _spikeBuffer = new Queue<int>(windowSize);
        Discomfort = 1.0;
        PreviousDiscomfort = 1.0;
    }

    public void Tick(Network net, int spikesThisTick)
    {
        _spikeBuffer.Enqueue(spikesThisTick);
        while (_spikeBuffer.Count > _windowSize) _spikeBuffer.Dequeue();
        _ticksSinceUpdate++;
        if (_ticksSinceUpdate < _windowSize) return;
        _ticksSinceUpdate = 0;

        double avgSpikes = 0;
        foreach (var s in _spikeBuffer) avgSpikes += s;
        avgSpikes /= _windowSize;
        double newActivity = avgSpikes / Math.Max(1, net.Neurons.Count);

        double newWeightNorm = 0;
        foreach (var syn in net.Synapses) newWeightNorm += Math.Abs(syn.Weight);

        ActivityLevel = ActivityLevel * Smoothing + newActivity * (1 - Smoothing);
        WeightNorm = WeightNorm * Smoothing + newWeightNorm * (1 - Smoothing);

        PreviousDiscomfort = Discomfort;
        double activityErr = Math.Abs(ActivityLevel - ActivityTarget) / ActivityTarget;
        double weightErr = Math.Abs(WeightNorm - WeightNormTarget) / WeightNormTarget;
        Discomfort = activityErr + 0.5 * weightErr;

        // === ПРЯМОЕ УПРАВЛЕНИЕ ===
        // Вес: если слишком много — ускоряем забывание; если мало — замедляем
        if (WeightNorm > WeightNormTarget * 1.15)
            WeightDecayMultiplier = 3.0;
        else if (WeightNorm > WeightNormTarget * 1.05)
            WeightDecayMultiplier = 1.5;
        else if (WeightNorm < WeightNormTarget * 0.85)
            WeightDecayMultiplier = 0.3;
        else
            WeightDecayMultiplier = 1.0;

        // Активность: если слишком много — поднимаем порог; если мало — опускаем
        if (ActivityLevel > ActivityTarget * 1.25)
            ThresholdMultiplier = 1.1;
        else if (ActivityLevel > ActivityTarget * 1.1)
            ThresholdMultiplier = 1.03;
        else if (ActivityLevel < ActivityTarget * 0.75)
            ThresholdMultiplier = 0.9;
        else
            ThresholdMultiplier = 1.0;
    }

    public bool Improved => Discomfort < PreviousDiscomfort;
}