namespace SpikingNet.Core;

public sealed class Synapse
{
    public int Index { get; internal set; } = -1;
    public int PreIndex { get; }
    public int PostIndex { get; }
    public double Weight { get; set; }
    public double InitialWeight { get; }
    public int Delay { get; }

    private readonly double[] _ring;
    private int _readCursor;
    private int _writeCursor;

    public Synapse(int preIndex, int postIndex, double weight, int delay)
    {
        if (delay < 1) throw new ArgumentOutOfRangeException(nameof(delay));
        PreIndex = preIndex;
        PostIndex = postIndex;
        Weight = weight;
        InitialWeight = weight;
        Delay = delay;
        _ring = new double[delay];
    }

    public double Read()
    {
        double v = _ring[_readCursor];
        _readCursor = (_readCursor + 1) % Delay;
        return v;
    }

    public void Write(double value)
    {
        _ring[_writeCursor] = value;
        _writeCursor = (_writeCursor + 1) % Delay;
    }

    public IEnumerable<(float progress, double value)> InFlight()
    {
        for (int i = 0; i < Delay; i++)
        {
            int slot = (_readCursor + i) % Delay;
            double v = _ring[slot];
            if (v == 0.0) continue;
            float progress = Delay <= 1 ? 1f : 1f - (float)i / (Delay - 1);
            yield return (progress, v);
        }
    }

    /// <summary>
    /// STDP-правило. Δw = η·(A+·pre_trace·post_spike − A−·post_trace·pre_spike) − λ·w.
    /// Применяется только к возбуждающим синапсам.
    /// </summary>
    public void ApplyPlasticity(
        double learningRate,
        double aPlus,
        double aMinus,
        double weightDecay,
        double preTrace,
        double postTrace,
        bool preSpiked,
        bool postSpiked)
    {
        if (InitialWeight <= 0) return;

        double ltp = aPlus * preTrace * (postSpiked ? 1.0 : 0.0);
        double ltd = aMinus * postTrace * (preSpiked ? 1.0 : 0.0);
        double dw = learningRate * (ltp - ltd) - weightDecay * Weight;

        Weight += dw;

        if (Weight < 0.05) Weight = 0.05;
        if (Weight > 2.0) Weight = 2.0;
    }

    public override string ToString() =>
        $"{PreIndex}->{PostIndex} w={Weight:F2} d={Delay}";
}