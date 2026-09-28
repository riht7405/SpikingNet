namespace SpikingNet.Core;

public sealed class Neuron
{
    public int Index { get; internal set; } = -1;
    public string Name { get; }

    // --- Состояние ---
    public double Potential;
    public int RefractoryRemaining;
    public bool SpikedThisTick;
    /// <summary>Eligibility trace — «память» о недавних спайках.</summary>
    public double Trace;

    // --- Параметры ---
    public double Threshold { get; set; } = 1.0;
    public double Decay { get; set; } = 0.9;
    public int RefractoryPeriod { get; set; } = 2;
    public double TraceDecay { get; set; } = 0.9;

    // --- Статистика ---
    public long TotalSpikes { get; internal set; }
    public double LastInput { get; internal set; }

    public Neuron(string name) => Name = name;

    public bool InRefractory => RefractoryRemaining > 0;

    public override string ToString() =>
        $"{Name}[V={Potential:F3}, spikes={TotalSpikes}, trace={Trace:F2}]";
}