namespace SpikingNet.Core;

/// <summary>
/// Сенсорный вход из среды. Каждый такт подаёт ток в сенсорные нейроны
/// в зависимости от состояния мира. Не "награда" — просто информация.
/// </summary>
public sealed class EnvironmentStimulus : IStimulus
{
    public Environment Env { get; }
    public Homeostasis Homeostasis { get; }

    public Neuron FoodRight { get; }
    public Neuron FoodLeft { get; }
    public Neuron Satiety { get; }

    public double FoodSensorCurrent { get; set; } = 0.65;
    public double SatietyCurrent { get; set; } = 0.55;

    public EnvironmentStimulus(Environment env, Homeostasis homeostasis,
                               Neuron foodRight, Neuron foodLeft, Neuron satiety)
    {
        Env = env;
        Homeostasis = homeostasis;
        FoodRight = foodRight;
        FoodLeft = foodLeft;
        Satiety = satiety;
    }

    public void Apply(long tick, Span<double> inputCurrents)
    {
        // FoodRight активен, если еда правее агента
        if (Env.FoodX > Env.AgentX)
            inputCurrents[FoodRight.Index] += FoodSensorCurrent;

        // FoodLeft активен, если еда левее
        if (Env.FoodX < Env.AgentX)
            inputCurrents[FoodLeft.Index] += FoodSensorCurrent;

        // Satiety — линейно от энергии: голоден → молчит, сыт → активен
        // При Energy 0.5 → ток 0
        // При Energy 0.9 → ток 0.8 (выше порога 0.5)
        double satietyCurrent = Math.Max(0.0, Homeostasis.Energy - 0.5) * 2.0;
        if (satietyCurrent > 0)
            inputCurrents[Satiety.Index] += satietyCurrent;
    }
}