namespace SpikingNet.Core;

public sealed class Network
{
    // ============================================================
    // НЕЙРОНЫ — плоские массивы (SoA)
    // ============================================================
    internal double[] _potential = Array.Empty<double>();
    internal double[] _threshold = Array.Empty<double>();
    internal double[] _baseThreshold = Array.Empty<double>();
    internal int[] _refractory = Array.Empty<int>();
    internal int[] _refractoryPeriod = Array.Empty<int>();
    internal byte[] _spiked = Array.Empty<byte>();
    internal double[] _trace = Array.Empty<double>();
    internal double[] _decay = Array.Empty<double>();
    internal double[] _traceDecay = Array.Empty<double>();
    internal double[] _lastInput = Array.Empty<double>();
    internal long[] _totalSpikes = Array.Empty<long>();

    internal int NeuronCount { get; private set; }

    private readonly List<Neuron> _neuronRefs = new();
    public IReadOnlyList<Neuron> Neurons => _neuronRefs;

    // ============================================================
    // СИНАПСЫ — плоские массивы (SoA) + flat ring buffer
    // ============================================================
    internal int[] _pre = Array.Empty<int>();
    internal int[] _post = Array.Empty<int>();
    internal double[] _weight = Array.Empty<double>();
    internal double[] _initialWeight = Array.Empty<double>();
    internal int[] _delay = Array.Empty<int>();
    internal double[] _ring = Array.Empty<double>();
    internal int[] _readCursor = Array.Empty<int>();
    internal int[] _writeCursor = Array.Empty<int>();
    internal int _maxDelay = 1;

    internal int SynapseCount { get; private set; }

    private readonly List<Synapse> _synapseRefs = new();
    public IReadOnlyList<Synapse> Synapses => _synapseRefs;

    // ============================================================
    // РЕГИОНЫ
    // ============================================================
    private readonly List<Region> _regions = new();
    private readonly Dictionary<string, Region> _regionByName = new();
    public IReadOnlyList<Region> Regions => _regions;

    public Region AddRegion(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Region name empty", nameof(name));
        if (_regionByName.ContainsKey(name))
            throw new ArgumentException($"Region '{name}' exists");

        var r = new Region(name);
        _regions.Add(r);
        _regionByName[name] = r;
        return r;
    }

    public Region? GetRegion(string name)
        => _regionByName.TryGetValue(name, out var r) ? r : null;

    // ============================================================
    // ДОБАВЛЕНИЕ
    // ============================================================
    public Neuron AddNeuron(Region region, string name)
    {
        if (!_regions.Contains(region))
            throw new ArgumentException("Region not part of network");

        // Расширяем массивы
        if (NeuronCount >= _potential.Length) GrowNeurons();

        int idx = NeuronCount++;
        _potential[idx] = 0;
        _threshold[idx] = 1.0;
        _baseThreshold[idx] = 1.0;
        _refractory[idx] = 0;
        _refractoryPeriod[idx] = 2;
        _spiked[idx] = 0;
        _trace[idx] = 0;
        _decay[idx] = 0.9;
        _traceDecay[idx] = 0.9;
        _lastInput[idx] = 0;
        _totalSpikes[idx] = 0;

        var n = new Neuron(this, idx, name) { Region = region };
        _neuronRefs.Add(n);
        region.RegisterNeuron(n);
        return n;
    }

    public Synapse Connect(Neuron pre, Neuron post, double weight, int delay = 1)
    {
        if (delay < 1) throw new ArgumentOutOfRangeException(nameof(delay));
        if (delay > _maxDelay) ExpandRingBuffer(delay);

        if (SynapseCount >= _pre.Length) GrowSynapses();

        int idx = SynapseCount++;
        _pre[idx] = pre.Index;
        _post[idx] = post.Index;
        _weight[idx] = weight;
        _initialWeight[idx] = weight;
        _delay[idx] = delay;
        _readCursor[idx] = 0;
        _writeCursor[idx] = 0;
        // ring slots уже обнулены (при расширении)

        var s = new Synapse(this, idx) { OwnerRegion = pre.Region };
        _synapseRefs.Add(s);
        pre.Region?.RegisterSynapse(s);
        return s;
    }

    // ============================================================
    // РАСШИРЕНИЕ МАССИВОВ
    // ============================================================
    private void GrowNeurons()
    {
        int cap = Math.Max(8, _potential.Length * 2);
        Array.Resize(ref _potential, cap);
        Array.Resize(ref _threshold, cap);
        Array.Resize(ref _baseThreshold, cap);
        Array.Resize(ref _refractory, cap);
        Array.Resize(ref _refractoryPeriod, cap);
        Array.Resize(ref _spiked, cap);
        Array.Resize(ref _trace, cap);
        Array.Resize(ref _decay, cap);
        Array.Resize(ref _traceDecay, cap);
        Array.Resize(ref _lastInput, cap);
        Array.Resize(ref _totalSpikes, cap);
    }

    private void GrowSynapses()
    {
        int cap = Math.Max(8, _pre.Length * 2);
        Array.Resize(ref _pre, cap);
        Array.Resize(ref _post, cap);
        Array.Resize(ref _weight, cap);
        Array.Resize(ref _initialWeight, cap);
        Array.Resize(ref _delay, cap);
        Array.Resize(ref _readCursor, cap);
        Array.Resize(ref _writeCursor, cap);
        // ring buffer пересоздаём целиком (данные старого не сохраняем —
        // это ок, т.к. ring содержит только сигналы в полёте)
        RebuildRing(cap, _maxDelay);
    }

    private void ExpandRingBuffer(int newMaxDelay)
    {
        _maxDelay = newMaxDelay;
        RebuildRing(Math.Max(8, _pre.Length), newMaxDelay);
    }

    private void RebuildRing(int synapseCapacity, int maxDelay)
    {
        // Копируем существующие данные если возможно
        var oldRing = _ring;
        var oldStride = _ring.Length > 0 && _pre.Length > 0
            ? _ring.Length / Math.Max(1, _synapseRefs.Capacity > 0 ? _synapseRefs.Capacity : _synapseRefs.Count + 1)
            : 0;

        // Проще: пересоздать с нуля. Сигналы в полёте потеряются,
        // но это единичное событие при добавлении синапса.
        _ring = new double[synapseCapacity * maxDelay];
    }

    // ============================================================
    // ДОСТУП
    // ============================================================
    public Neuron this[int i] => _neuronRefs[i];
}