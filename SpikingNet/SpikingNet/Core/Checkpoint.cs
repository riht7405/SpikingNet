using System.IO;

namespace SpikingNet.Core;

/// <summary>
/// Сериализация состояния сети в бинарный файл.
/// Сохраняем ТОЛЬКО состояние (potential, weights, tick) — структура
/// всегда строится кодом. При загрузке проверяем, что структура совпадает.
/// </summary>
public static class Checkpoint
{
    private const uint Magic = 0x4E4B5053;   // "SPKN" little-endian
    private const int Version = 1;

    public static void Save(string path, Network net, Simulation sim, Neuromodulator? mod)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        w.Write(Magic);
        w.Write(Version);
        w.Write(sim.Tick);

        // Регионы — только метаданные для валидации
        w.Write(net.Regions.Count);
        foreach (var r in net.Regions)
        {
            w.Write(r.Name);
            w.Write(r.Neurons.Count);
            w.Write(r.Synapses.Count);
        }

        // Нейроны
        w.Write(net.Neurons.Count);
        foreach (var n in net.Neurons)
        {
            w.Write(n.Potential);
            w.Write(n.RefractoryRemaining);
            w.Write(n.SpikedThisTick);
            w.Write(n.Trace);
            w.Write(n.TotalSpikes);
        }

        // Синапсы
        w.Write(net.Synapses.Count);
        foreach (var s in net.Synapses)
            w.Write(s.Weight);

        // Нейромодулятор
        w.Write(mod != null);
        if (mod != null)
        {
            w.Write(mod.Name);
            w.Write(mod.Level);
            w.Write(mod.TotalReleases);
        }
    }

    public static void Load(string path, Network net, Simulation sim, Neuromodulator? mod)
    {
        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs);

        if (r.ReadUInt32() != Magic)
            throw new InvalidDataException("Not a SpikingNet checkpoint");

        int version = r.ReadInt32();
        if (version != Version)
            throw new InvalidDataException($"Unsupported checkpoint version: {version}");

        long tick = r.ReadInt64();

        // Валидация регионов
        int regionCount = r.ReadInt32();
        if (regionCount != net.Regions.Count)
            throw new InvalidDataException(
                $"Region count mismatch: file={regionCount}, net={net.Regions.Count}");

        for (int i = 0; i < regionCount; i++)
        {
            string name = r.ReadString();
            int nc = r.ReadInt32();
            int sc = r.ReadInt32();
            var region = net.Regions[i];
            if (region.Name != name || region.Neurons.Count != nc || region.Synapses.Count != sc)
                throw new InvalidDataException(
                    $"Region {i} mismatch: file=({name},{nc},{sc}) net=({region.Name},{region.Neurons.Count},{region.Synapses.Count})");
        }

        // Нейроны
        int neuronCount = r.ReadInt32();
        if (neuronCount != net.Neurons.Count)
            throw new InvalidDataException(
                $"Neuron count mismatch: file={neuronCount}, net={net.Neurons.Count}");

        for (int i = 0; i < neuronCount; i++)
        {
            var n = net.Neurons[i];
            n.Potential = r.ReadDouble();
            n.RefractoryRemaining = r.ReadInt32();
            n.SpikedThisTick = r.ReadBoolean();
            n.Trace = r.ReadDouble();
            n.TotalSpikes = r.ReadInt64();
        }

        // Синапсы
        int synapseCount = r.ReadInt32();
        if (synapseCount != net.Synapses.Count)
            throw new InvalidDataException(
                $"Synapse count mismatch: file={synapseCount}, net={net.Synapses.Count}");

        for (int i = 0; i < synapseCount; i++)
            net.Synapses[i].Weight = r.ReadDouble();

        // Нейромодулятор
        bool hasMod = r.ReadBoolean();
        if (hasMod)
        {
            string name = r.ReadString();
            double level = r.ReadDouble();
            long total = r.ReadInt64();

            if (mod == null)
                throw new InvalidDataException(
                    $"Checkpoint has modulator '{name}', but no modulator provided");

            if (mod.Name != name)
                throw new InvalidDataException(
                    $"Modulator name mismatch: file='{name}', net='{mod.Name}'");

            mod.RestoreState(level, total);
        }

        // Tick — присваиваем в самом конце, когда всё уже загружено
        sim.Tick = tick;
    }
}