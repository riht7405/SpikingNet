namespace SpikingNet.Core;

/// <summary>
/// Контейнер регионов, нейронов и синапсов.
/// Владеет глобальными списками и индексами.
/// Отвечает только за структуру — правила времени живут в Simulation.
/// </summary>
public sealed class Network
{
    private readonly List<Region> _regions = new();
    private readonly Dictionary<string, Region> _regionByName = new();
    private readonly List<Neuron> _neurons = new();
    private readonly List<Synapse> _synapses = new();
    private readonly List<List<int>> _outgoing = new();
    private readonly List<List<int>> _incoming = new();

    public IReadOnlyList<Region> Regions => _regions;
    public IReadOnlyList<Neuron> Neurons => _neurons;
    public IReadOnlyList<Synapse> Synapses => _synapses;

    // ------------------------------------------------------------------
    // Регионы
    // ------------------------------------------------------------------

    public Region AddRegion(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Region name must not be empty", nameof(name));
        if (_regionByName.ContainsKey(name))
            throw new ArgumentException($"Region '{name}' already exists", nameof(name));

        var r = new Region(name);
        _regions.Add(r);
        _regionByName[name] = r;
        return r;
    }

    public Region? GetRegion(string name)
        => _regionByName.TryGetValue(name, out var r) ? r : null;

    // ------------------------------------------------------------------
    // Нейроны
    // ------------------------------------------------------------------

    public Neuron AddNeuron(Region region, string name)
    {
        if (!_regions.Contains(region))
            throw new ArgumentException("Region is not part of this network", nameof(region));

        var n = new Neuron(name) { Index = _neurons.Count };
        _neurons.Add(n);
        _outgoing.Add(new List<int>());
        _incoming.Add(new List<int>());
        region.RegisterNeuron(n);
        return n;
    }

    // ------------------------------------------------------------------
    // Синапсы (внутрирегиональные и межрегиональные — одинаково)
    // ------------------------------------------------------------------

    public Synapse Connect(Neuron pre, Neuron post, double weight, int delay = 1)
    {
        var s = new Synapse(pre.Index, post.Index, weight, delay)
        {
            Index = _synapses.Count
        };
        _synapses.Add(s);
        _outgoing[pre.Index].Add(s.Index);
        _incoming[post.Index].Add(s.Index);

        // Синапс регистрируется в регионе-отправителе.
        // Межрегиональный синапс принадлежит тому региону, из которого ИСХОДИТ.
        pre.Region?.RegisterSynapse(s);

        return s;
    }

    // ------------------------------------------------------------------
    // Быстрые индексы (для горячего цикла)
    // ------------------------------------------------------------------

    public IReadOnlyList<int> OutgoingOf(int neuronIndex) => _outgoing[neuronIndex];
    public IReadOnlyList<int> IncomingOf(int neuronIndex) => _incoming[neuronIndex];

    public Neuron this[int i] => _neurons[i];
    public Synapse SynapseAt(int i) => _synapses[i];
}