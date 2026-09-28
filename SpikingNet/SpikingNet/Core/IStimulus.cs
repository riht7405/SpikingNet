namespace SpikingNet.Core;

/// <summary>
/// Источник внешнего сигнала. Может подавать ток в любой нейрон.
/// </summary>
public interface IStimulus
{
    void Apply(long tick, Span<double> inputCurrents);
}