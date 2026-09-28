namespace SpikingNet.Core;

public sealed class Network
{
    private readonly List<Neuron> _neurons = new();
    private readonly List<Synapse> _synapses = new();
    private readonly List<List<int>> _outgoing = new();
    private readonly List<List<int>> _incoming = new();

    public IReadOnlyList<Neuron> Neurons => _neurons;
    public IReadOnlyList<Synapse> Synapses => _synapses;

    public Neuron AddNeuron(string name)
    {
        var n = new Neuron(name) { Index = _neurons.Count };
        _neurons.Add(n);
        _outgoing.Add(new List<int>());
        _incoming.Add(new List<int>());
        return n;
    }

    public Synapse Connect(int preIndex, int postIndex, double weight, int delay = 1)
    {
        var s = new Synapse(preIndex, postIndex, weight, delay)
        {
            Index = _synapses.Count
        };
        _synapses.Add(s);
        _outgoing[preIndex].Add(s.Index);
        _incoming[postIndex].Add(s.Index);
        return s;
    }

    public IReadOnlyList<int> OutgoingOf(int neuronIndex) => _outgoing[neuronIndex];
    public IReadOnlyList<int> IncomingOf(int neuronIndex) => _incoming[neuronIndex];

    public Neuron this[int i] => _neurons[i];
    public Synapse SynapseAt(int i) => _synapses[i];
}