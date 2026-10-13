namespace SpikingNet.Core;

public static class DebugDump
{
    public static string DumpWeights(Network net)
    {
        var sb = new System.Text.StringBuilder();
        double sum = 0;
        foreach (var s in net.Synapses)
        {
            double w = s.Weight;
            sum += Math.Abs(w);
            sb.AppendLine($"  {net[s.PreIndex].Name,-10} → {net[s.PostIndex].Name,-10} " +
                          $"w={w:F4}  |w|={Math.Abs(w):F4}");
        }
        sb.AppendLine($"  ─────────────────────────────");
        sb.AppendLine($"  Σ |w| = {sum:F4}");
        return sb.ToString();
    }
}