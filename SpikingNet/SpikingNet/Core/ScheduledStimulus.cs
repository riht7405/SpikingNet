namespace SpikingNet.Core;

public sealed class ScheduledStimulus : IStimulus
{
    private readonly int _targetIndex;
    private readonly double _current;
    private readonly List<(long start, long end)> _windows = new();

    public ScheduledStimulus(int targetIndex, double current)
    {
        _targetIndex = targetIndex;
        _current = current;
    }

    public ScheduledStimulus Window(long start, long end)
    {
        _windows.Add((start, end));
        return this;
    }

    public void Apply(long tick, Span<double> inputCurrents)
    {
        foreach (var (s, e) in _windows)
        {
            if (tick >= s && tick < e)
            {
                inputCurrents[_targetIndex] += _current;
                return;
            }
        }
    }
}