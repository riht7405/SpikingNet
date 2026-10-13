using System.IO;

namespace SpikingNet.Core;

public static class Checkpoint
{
    private const uint Magic = 0x4E4B5053;
    private const int Version = 2;  // было 1, теперь 2 — новый формат

    public static void Save(string path, Network net, Simulation sim, Neuromodulator? mod)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        w.Write(Magic);
        w.Write(Version);
        w.Write(sim.Tick);

        w.Write(net.Regions.Count);
        foreach (var r in net.Regions)
        {
            w.Write(r.Name);
            w.Write(r.Neurons.Count);
            w.Write(r.Synapses.Count);
        }

        w.Write(net.NeuronCount);
        for (int i = 0; i < net.NeuronCount; i++)
        {
            w.Write(net._potential[i]);
            w.Write(net._threshold[i]);
            w.Write(net._baseThreshold[i]);
            w.Write(net._refractory[i]);
            w.Write(net._spiked[i]);
            w.Write(net._trace[i]);
            w.Write(net._totalSpikes[i]);
        }

        w.Write(net.SynapseCount);
        for (int s = 0; s < net.SynapseCount; s++)
            w.Write(net._weight[s]);

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
            throw new InvalidDataException($"Unsupported version: {version}");

        long tick = r.ReadInt64();

        int regionCount = r.ReadInt32();
        if (regionCount != net.Regions.Count)
            throw new InvalidDataException("Region count mismatch");

        for (int i = 0; i < regionCount; i++)
        {
            string name = r.ReadString();
            int nc = r.ReadInt32();
            int sc = r.ReadInt32();
            var region = net.Regions[i];
            if (region.Name != name || region.Neurons.Count != nc || region.Synapses.Count != sc)
                throw new InvalidDataException($"Region {i} mismatch");
        }

        int neuronCount = r.ReadInt32();
        if (neuronCount != net.NeuronCount)
            throw new InvalidDataException("Neuron count mismatch");

        for (int i = 0; i < neuronCount; i++)
        {
            net._potential[i] = r.ReadDouble();
            net._threshold[i] = r.ReadDouble();
            net._baseThreshold[i] = r.ReadDouble();
            net._refractory[i] = r.ReadInt32();
            net._spiked[i] = r.ReadByte();
            net._trace[i] = r.ReadDouble();
            net._totalSpikes[i] = r.ReadInt64();
        }

        int synapseCount = r.ReadInt32();
        if (synapseCount != net.SynapseCount)
            throw new InvalidDataException("Synapse count mismatch");

        for (int s = 0; s < synapseCount; s++)
            net._weight[s] = r.ReadDouble();

        bool hasMod = r.ReadBoolean();
        if (hasMod)
        {
            string name = r.ReadString();
            double level = r.ReadDouble();
            long total = r.ReadInt64();
            if (mod == null)
                throw new InvalidDataException("Checkpoint has modulator, none provided");
            if (mod.Name != name)
                throw new InvalidDataException("Modulator name mismatch");
            mod.RestoreState(level, total);
        }

        sim.Tick = tick;
    }
}