namespace SpikingNet.Core;

/// <summary>
/// Периодический стимул: подаёт ток Duration тактов, затем молчит
/// Period−Duration тактов, и так вечно.
/// </summary>
public sealed class PeriodicStimulus : IStimulus
{
    private readonly int _targetIndex;
    private readonly double _current;
    private readonly long _period;
    private readonly long _duration;

    public PeriodicStimulus(int targetIndex, double current, long period, long duration)
    {
        if (period <= 0 || duration <= 0 || duration > period)
            throw new ArgumentException("Требуется 0 < duration <= period");
        _targetIndex = targetIndex;
        _current = current;
        _period = period;
        _duration = duration;
    }

    public void Apply(long tick, Span<double> inputCurrents)
    {
        if (tick % _period < _duration)
            inputCurrents[_targetIndex] += _current;
    }
}