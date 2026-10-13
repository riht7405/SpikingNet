namespace SpikingNet.Core;

/// <summary>
/// Фасад над плоскими массивами Network для синапсов.
/// </summary>
public sealed class Synapse
{
    internal Network Net { get; }
    public int Index { get; }
    public Region? OwnerRegion { get; internal set; }

    internal Synapse(Network net, int index) { Net = net; Index = index; }

    public int PreIndex => Net._pre[Index];
    public int PostIndex => Net._post[Index];
    public int Delay => Net._delay[Index];

    public double Weight
    {
        get => Net._weight[Index];
        set => Net._weight[Index] = value;
    }

    public double InitialWeight => Net._initialWeight[Index];

    /// <summary>Все сигналы, летящие по этой трубе. Прогресс 0 (у входа) → 1 (у выхода).</summary>
    public IEnumerable<(float progress, double value)> InFlight()
    {
        int stride = Net._maxDelay;
        int baseIdx = Index * stride;
        int read = Net._readCursor[Index];
        int delay = Net._delay[Index];

        for (int i = 0; i < delay; i++)
        {
            int slot = read + i;
            if (slot >= delay) slot -= delay;
            double v = Net._ring[baseIdx + slot];
            if (v == 0.0) continue;

            float progress = delay <= 1 ? 1f : 1f - (float)i / (delay - 1);
            yield return (progress, v);
        }
    }

    public override string ToString() =>
        $"{PreIndex}->{PostIndex} w={Weight:F2} d={Delay}";
}