namespace SpikingNet.Core;

/// <summary>
/// Связка среда ↔ сеть. Реализует ISimulationObserver: каждый такт
/// читает спайки моторных нейронов и двигает агента в мире.
/// Если агент поел — восстанавливает энергию в гомеостазе.
/// </summary>
public sealed class EnvironmentObserver : ISimulationObserver
{
    public Environment Env { get; }
    public Homeostasis Homeostasis { get; }
    public Neuron MoveRight { get; }
    public Neuron MoveLeft { get; }

    public EnvironmentObserver(Environment env, Homeostasis homeostasis,
                               Neuron moveRight, Neuron moveLeft)
    {
        Env = env;
        Homeostasis = homeostasis;
        MoveRight = moveRight;
        MoveLeft = moveLeft;
    }

    public void OnTickStart(long tick, Network net) { }

    public void OnTickEnd(long tick, Network net)
    {
        int direction = 0;
        if (MoveRight.SpikedThisTick) direction += 1;
        if (MoveLeft.SpikedThisTick) direction -= 1;

        if (direction != 0)
        {
            bool ate = Env.Move(direction);
            if (ate) Homeostasis.Eat();
        }
    }

    public void OnFinished(long totalTicks, Network net) { }
}