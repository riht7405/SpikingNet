namespace SpikingNet.Core;

/// <summary>
/// Одномерный мир. Агент перемещается влево/вправо. Еда — цель.
/// Среда не награждает. Она просто существует. Реакция сети на неё —
/// это уже поведение, не оптимизация.
/// </summary>
public sealed class Environment
{
    public double AgentX { get; private set; }
    public double FoodX { get; private set; }

    public double WorldMin { get; set; } = -50;
    public double WorldMax { get; set; } = 50;
    public double EatRadius { get; set; } = 2.0;

    public long FoodEaten { get; private set; }
    public long Steps { get; private set; }

    private readonly Random _rng;

    public Environment(int seed = 0)
    {
        _rng = new Random(seed);
        AgentX = 0;
        FoodX = 20;
    }

    /// <summary>Двигает агента. Возвращает true, если он наступил на еду.</summary>
    public bool Move(int direction)
    {
        if (direction == 0) return false;

        AgentX += direction;
        if (AgentX < WorldMin) AgentX = WorldMin;
        if (AgentX > WorldMax) AgentX = WorldMax;

        Steps++;

        if (Math.Abs(AgentX - FoodX) < EatRadius)
        {
            FoodEaten++;
            // Еда в новом случайном месте
            FoodX = _rng.NextDouble() * (WorldMax - WorldMin) + WorldMin;
            return true;
        }
        return false;
    }

    public double FoodDistance => Math.Abs(FoodX - AgentX);
    public double FoodDirection => Math.Sign(FoodX - AgentX);  // +1, -1, 0
    public double AgentNormalized => (AgentX - WorldMin) / (WorldMax - WorldMin);
    public double FoodNormalized => (FoodX - WorldMin) / (WorldMax - WorldMin);
}