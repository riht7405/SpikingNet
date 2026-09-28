namespace SpikingNet.Core;

/// <summary>
/// Регион — именованная группа нейронов и синапсов.
/// Аналог отдела мозга: "сенсорная кора", "гиппокамп", "базальные ганглии".
///
/// Регион хранит ТОЛЬКО свои нейроны и ВНУТРИрегиональные синапсы.
/// Глобальные списки и межрегиональные связи — ответственность Network.
/// Регион нельзя создать вручную — только через Network.AddRegion().
/// </summary>
public sealed class Region
{
    public string Name { get; }

    public IReadOnlyList<Neuron> Neurons => _neurons;
    public IReadOnlyList<Synapse> Synapses => _synapses;

    /// Нейромодулятор региона (пока не используется Simulation, но место зарезервировано).
    public Neuromodulator? Modulator { get; set; }

    private readonly List<Neuron> _neurons = new();
    private readonly List<Synapse> _synapses = new();

    internal Region(string name) => Name = name;

    internal void RegisterNeuron(Neuron n)
    {
        n.Region = this;
        n.LocalIndex = _neurons.Count;
        _neurons.Add(n);
    }

    internal void RegisterSynapse(Synapse s) => _synapses.Add(s);

    public override string ToString() =>
        $"{Name}({_neurons.Count}N, {_synapses.Count}S)";
}