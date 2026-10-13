namespace SpikingNet.Core;

public sealed class Simulation
{
    public Network Network { get; }
    public long Tick { get; internal set; }

    public bool PlasticityEnabled { get; set; } = true;
    public double LearningRate { get; set; } = 0.05;
    public double APlus { get; set; } = 1.0;
    public double AMinus { get; set; } = 0.4;
    public double WeightDecay { get; set; } = 0.0005;

    public Neuromodulator? Modulator { get; set; }
    public Homeostasis? Homeostasis { get; set; }
    public int ModulatorTriggerIndex { get; set; } = -1;
    public double LastPlasticityGain { get; private set; } = 1.0;

    public double NoiseAmplitude { get; set; } = 1.5;
    public int PerturbationNeuron { get; set; } = -1;
    public int PerturbationTicksLeft { get; set; } = 0;
    public double PerturbationCurrent { get; set; } = 2.0;

    private readonly List<IStimulus> _stimuli = new();
    private readonly List<ISimulationObserver> _observers = new();
    private double[] _inputCurrents = Array.Empty<double>();
    private readonly Random _rng = new();

    public Simulation(Network network)
    {
        Network = network;
        _inputCurrents = new double[Math.Max(8, network.NeuronCount)];
    }

    public void AddStimulus(IStimulus s) => _stimuli.Add(s);
    public void AddObserver(ISimulationObserver o) => _observers.Add(o);

    public void Step()
    {
        var net = Network;
        int n = net.NeuronCount;
        if (_inputCurrents.Length < n) Array.Resize(ref _inputCurrents, n);

        foreach (var o in _observers) o.OnTickStart(Tick, net);

        // 1) Внешние токи
        Array.Clear(_inputCurrents, 0, n);
        var span = _inputCurrents.AsSpan(0, n);
        foreach (var s in _stimuli) s.Apply(Tick, span);

        // 1.5) Shot noise
        if (NoiseAmplitude > 0)
        {
            for (int i = 0; i < n; i++)
                if (_rng.NextDouble() < 0.05)
                    _inputCurrents[i] += NoiseAmplitude * (0.5 + _rng.NextDouble());
        }

        // 1.6) Возмущение
        if (PerturbationTicksLeft > 0 && PerturbationNeuron >= 0 && PerturbationNeuron < n)
        {
            _inputCurrents[PerturbationNeuron] += PerturbationCurrent;
            PerturbationTicksLeft--;
        }

        // 2) Синапсы: читаем то, что пришло (сдвигая кольца)
        int stride = net._maxDelay;
        for (int s = 0; s < net.SynapseCount; s++)
        {
            int delay = net._delay[s];
            int read = net._readCursor[s];
            int baseIdx = s * stride;
            _inputCurrents[net._post[s]] += net._ring[baseIdx + read];
            net._readCursor[s] = (read + 1) % delay;
        }

        // 3) Интеграция + спайки
        for (int i = 0; i < n; i++)
        {
            net._lastInput[i] = _inputCurrents[i];

            if (net._refractory[i] > 0)
            {
                net._refractory[i]--;
                net._potential[i] *= net._decay[i];
                net._spiked[i] = 0;
                continue;
            }

            net._potential[i] = net._potential[i] * net._decay[i] + _inputCurrents[i];

            if (net._potential[i] >= net._threshold[i])
            {
                net._potential[i] = 0.0;
                net._refractory[i] = net._refractoryPeriod[i];
                net._spiked[i] = 1;
                net._totalSpikes[i]++;
            }
            else
            {
                net._spiked[i] = 0;
            }
        }

        // 4) Трейсы
        for (int i = 0; i < n; i++)
            net._trace[i] = net._trace[i] * net._traceDecay[i]
                          + (net._spiked[i] != 0 ? 1.0 : 0.0);

        // 4.4) Гомеостаз
        if (Homeostasis != null)
        {
            int spikesThisTick = 0;
            for (int i = 0; i < n; i++)
                if (net._spiked[i] != 0) spikesThisTick++;
            Homeostasis.Tick(net, spikesThisTick);
        }

        // 4.5) Нейромодулятор
        if (Modulator != null)
        {
            if (Homeostasis != null && Homeostasis.Improved)
                Modulator.Release();
            else if (Homeostasis == null
                     && ModulatorTriggerIndex >= 0
                     && ModulatorTriggerIndex < n
                     && net._spiked[ModulatorTriggerIndex] != 0)
                Modulator.Release();

            Modulator.Tick();
            LastPlasticityGain = Modulator.PlasticityGain;
        }
        else LastPlasticityGain = 1.0;

        // 5) Пластичность (STDP)
        if (PlasticityEnabled)
        {
            double effectiveLr = LearningRate * LastPlasticityGain;
            double effectiveWd = WeightDecay * (Homeostasis?.WeightDecayMultiplier ?? 1.0);

            for (int s = 0; s < net.SynapseCount; s++)
            {
                if (net._initialWeight[s] <= 0) continue;

                int preIdx = net._pre[s];
                int postIdx = net._post[s];
                bool preS = net._spiked[preIdx] != 0;
                bool postS = net._spiked[postIdx] != 0;
                double preT = net._trace[preIdx];
                double postT = net._trace[postIdx];

                double ltp = APlus * preT * (postS ? 1.0 : 0.0);
                double ltd = AMinus * postT * (preS ? 1.0 : 0.0);
                double dw = effectiveLr * (ltp - ltd) - effectiveWd * net._weight[s];

                net._weight[s] += dw;
                if (net._weight[s] < 0.05) net._weight[s] = 0.05;
                if (net._weight[s] > 2.0) net._weight[s] = 2.0;
            }
        }

        // 5.5) Применяем пороговый множитель гомеостаза
        if (Homeostasis != null)
        {
            double mult = Homeostasis.ThresholdMultiplier;
            for (int i = 0; i < n; i++)
                net._threshold[i] = net._baseThreshold[i] * mult;
        }

        // 6) Запись спайков в кольцевые буферы
        for (int s = 0; s < net.SynapseCount; s++)
        {
            int preIdx = net._pre[s];
            double val = net._spiked[preIdx] != 0 ? net._weight[s] : 0.0;
            int write = net._writeCursor[s];
            int baseIdx = s * stride;
            net._ring[baseIdx + write] = val;
            net._writeCursor[s] = (write + 1) % net._delay[s];
        }

        foreach (var o in _observers) o.OnTickEnd(Tick, net);
        Tick++;
    }

    public void Run(long totalTicks)
    {
        for (long t = 0; t < totalTicks; t++) Step();
        foreach (var o in _observers) o.OnFinished(Tick, Network);
    }
}