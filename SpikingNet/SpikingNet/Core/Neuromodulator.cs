namespace SpikingNet.Core;

/// <summary>
/// Нейромодулятор (дофамин). Глобальный сигнал, который временно
/// усиливает пластичность всей сети. Level ∈ [Baseline, 1.0].
/// </summary>
public sealed class Neuromodulator
{
    public string Name { get; }
    public double Level { get; private set; }
    public double Decay { get; set; } = 0.93;
    public double ReleaseAmount { get; set; } = 1.0;
    public double Baseline { get; set; } = 0.05;
    public long TotalReleases { get; private set; }

    public Neuromodulator(string name)
    {
        Name = name;
        Level = Baseline;
    }

    public void Release()
    {
        Level = Math.Min(1.0, Level + ReleaseAmount);
        TotalReleases++;
    }

    public void Tick()
    {
        Level = Math.Max(Baseline, Level * Decay);
    }

    /// <summary>Множитель для LearningRate. На baseline ≈ 0.14, на пике = 1.0.</summary>
    public double PlasticityGain => 0.1 + 0.9 * Level;
}