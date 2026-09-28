namespace SpikingNet.Core;

public sealed class Simulation
{
    public Network Network { get; }
    public long Tick { get; private set; }

    // --- Пластичность ---
    public bool PlasticityEnabled { get; set; } = true;
    public double LearningRate { get; set; } = 0.008;
    public double APlus { get; set; } = 1.0;
    public double AMinus { get; set; } = 0.5;
    public double WeightDecay { get; set; } = 0.005;

    // --- Нейромодулятор ---
    public Neuromodulator? Modulator { get; set; }
    public int ModulatorTriggerIndex { get; set; } = -1;
    public double LastPlasticityGain { get; private set; } = 1.0;

    private readonly List<IStimulus> _stimuli = new();
    private readonly List<ISimulationObserver> _observers = new();
    private readonly double[] _inputCurrents;

    public Simulation(Network network)
    {
        Network = network;
        _inputCurrents = new double[network.Neurons.Count];
    }

    public void AddStimulus(IStimulus s) => _stimuli.Add(s);
    public void AddObserver(ISimulationObserver o) => _observers.Add(o);

    public void Step()
    {
        foreach (var o in _observers) o.OnTickStart(Tick, Network);

        int n = Network.Neurons.Count;

        // 1) Внешние токи
        Array.Clear(_inputCurrents);
        foreach (var s in _stimuli) s.Apply(Tick, _inputCurrents);

        // 2) Синаптические входы (читаем ДО записи)
        foreach (var syn in Network.Synapses)
            _inputCurrents[syn.PostIndex] += syn.Read();

        // 3) Интеграция и спайки
        for (int i = 0; i < n; i++)
        {
            var neuron = Network[i];
            neuron.LastInput = _inputCurrents[i];

            if (neuron.InRefractory)
            {
                neuron.RefractoryRemaining--;
                neuron.Potential *= neuron.Decay;
                neuron.SpikedThisTick = false;
                continue;
            }

            neuron.Potential = neuron.Potential * neuron.Decay + _inputCurrents[i];

            if (neuron.Potential >= neuron.Threshold)
            {
                neuron.Potential = 0.0;
                neuron.RefractoryRemaining = neuron.RefractoryPeriod;
                neuron.SpikedThisTick = true;
                neuron.TotalSpikes++;
            }
            else
            {
                neuron.SpikedThisTick = false;
            }
        }

        // 4) Трейсы
        for (int i = 0; i < n; i++)
        {
            var neuron = Network[i];
            neuron.Trace = neuron.Trace * neuron.TraceDecay
                         + (neuron.SpikedThisTick ? 1.0 : 0.0);
        }

        // 4.5) Нейромодулятор
        if (Modulator != null)
        {
            if (ModulatorTriggerIndex >= 0 &&
                Network[ModulatorTriggerIndex].SpikedThisTick)
            {
                Modulator.Release();
            }
            Modulator.Tick();
            LastPlasticityGain = Modulator.PlasticityGain;
        }
        else
        {
            LastPlasticityGain = 1.0;
        }

        // 5) Пластичность (STDP), усиленная нейромодулятором
        if (PlasticityEnabled)
        {
            double effectiveLr = LearningRate * LastPlasticityGain;
            foreach (var syn in Network.Synapses)
            {
                var pre = Network[syn.PreIndex];
                var post = Network[syn.PostIndex];
                syn.ApplyPlasticity(
                    effectiveLr, APlus, AMinus, WeightDecay,
                    pre.Trace, post.Trace,
                    pre.SpikedThisTick, post.SpikedThisTick);
            }
        }

        // 6) Запись спайков в трубы
        foreach (var syn in Network.Synapses)
        {
            var pre = Network[syn.PreIndex];
            syn.Write(pre.SpikedThisTick ? syn.Weight : 0.0);
        }

        foreach (var o in _observers) o.OnTickEnd(Tick, Network);
        Tick++;
    }

    public void Run(long totalTicks)
    {
        for (long t = 0; t < totalTicks; t++) Step();
        foreach (var o in _observers) o.OnFinished(Tick, Network);
    }
}