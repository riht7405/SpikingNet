namespace SpikingNet.Core;

public interface ISimulationObserver
{
    void OnTickStart(long tick, Network network);
    void OnTickEnd(long tick, Network network);
    void OnFinished(long totalTicks, Network network);
}