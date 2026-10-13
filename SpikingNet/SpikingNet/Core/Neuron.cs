namespace SpikingNet.Core;

/// <summary>
/// Фасад над плоскими массивами Network. Не хранит данные сам.
/// Для UI и стат-анализа. В горячем цикле Simulation работаем с массивами напрямую.
/// </summary>
public sealed class Neuron
{
    internal Network Net { get; }
    public int Index { get; }
    public string Name { get; }
    public Region? Region { get; internal set; }

    internal Neuron(Network net, int index, string name)
    {
        Net = net;
        Index = index;
        Name = name;
    }

    public double Potential
    {
        get => Net._potential[Index];
        set => Net._potential[Index] = value;
    }

    public double Threshold
    {
        get => Net._threshold[Index];
        set => Net._threshold[Index] = value;
    }

    public double BaseThreshold
    {
        get => Net._baseThreshold[Index];
        set => Net._baseThreshold[Index] = value;
    }

    public int RefractoryRemaining
    {
        get => Net._refractory[Index];
        set => Net._refractory[Index] = value;
    }

    public int RefractoryPeriod
    {
        get => Net._refractoryPeriod[Index];
        set => Net._refractoryPeriod[Index] = value;
    }

    public bool SpikedThisTick
    {
        get => Net._spiked[Index] != 0;
        set => Net._spiked[Index] = value ? (byte)1 : (byte)0;
    }

    public double Trace
    {
        get => Net._trace[Index];
        set => Net._trace[Index] = value;
    }

    public double Decay
    {
        get => Net._decay[Index];
        set => Net._decay[Index] = value;
    }

    public double TraceDecay
    {
        get => Net._traceDecay[Index];
        set => Net._traceDecay[Index] = value;
    }

    public double LastInput
    {
        get => Net._lastInput[Index];
        set => Net._lastInput[Index] = value;
    }

    public long TotalSpikes
    {
        get => Net._totalSpikes[Index];
        set => Net._totalSpikes[Index] = value;
    }

    public bool InRefractory => Net._refractory[Index] > 0;

    public override string ToString() =>
        $"{Name}[V={Potential:F3}, spikes={TotalSpikes}]";
}