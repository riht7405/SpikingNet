namespace SpikingNet.Core;

/// <summary>
/// Именованная группа нейронов и синапсов. Данные — в Network (SoA).
/// Region хранит только ссылки на фасады + метаданные.
/// </summary>
public sealed class Region
{
    public string Name { get; }
    public Neuromodulator? Modulator { get; set; }

    private readonly List<Neuron> _neurons = new();
    private readonly List<Synapse> _synapses = new();

    public IReadOnlyList<Neuron> Neurons => _neurons;
    public IReadOnlyList<Synapse> Synapses => _synapses;

    internal Region(string name) => Name = name;

    internal void RegisterNeuron(Neuron n) => _neurons.Add(n);
    internal void RegisterSynapse(Synapse s) => _synapses.Add(s);

    public override string ToString() =>
        $"{Name}({_neurons.Count}N, {_synapses.Count}S)";
}