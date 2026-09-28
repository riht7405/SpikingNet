using System.Drawing.Drawing2D;
using SpikingNet.Core;

namespace SpikingNet.Rendering;

public sealed class MainForm : Form
{
    private readonly Network _net;
    private readonly Simulation _sim;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PointF[] _positions;

    private const float Radius = 42f;
    private bool _paused;

    public MainForm()
    {
        Text = "Spiking Network — 5 neurons + Dopamine";
        ClientSize = new Size(900, 700);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(10, 14, 26);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += OnKeyDown;

        // --- Строим сеть ---
        _net = new Network();
        var sensor = _net.AddNeuron("Sensor");
        var inter1 = _net.AddNeuron("Inter-1");
        var inter2 = _net.AddNeuron("Inter-2");
        var output = _net.AddNeuron("Output");
        var dopamine = _net.AddNeuron("Dopamine");

        _net.Connect(sensor.Index, inter1.Index, 0.6, 1);
        _net.Connect(sensor.Index, inter2.Index, 0.4, 2);
        _net.Connect(inter1.Index, inter2.Index, 0.5, 1);
        _net.Connect(inter2.Index, output.Index, 0.7, 1);
        _net.Connect(output.Index, inter1.Index, -0.3, 2);
        _net.Connect(output.Index, dopamine.Index, 0.9, 1);

        // --- Стимул: бесконечный периодический ---
        _sim = new Simulation(_net);
        var stim = new PeriodicStimulus(sensor.Index, current: 0.5, period: 20, duration: 10);
        _sim.AddStimulus(stim);

        _sim.PlasticityEnabled = true;
        _sim.LearningRate = 0.05;
        _sim.APlus = 1.0;
        _sim.AMinus = 0.4;
        _sim.WeightDecay = 0.0005;

        // --- Нейромодулятор ---
        _sim.Modulator = new Neuromodulator("Dopamine")
        {
            Decay = 0.93,
            Baseline = 0.05,
            ReleaseAmount = 1.0,
        };
        _sim.ModulatorTriggerIndex = dopamine.Index;

        // --- Координаты ---
        _positions = new PointF[]
        {
            new(140, 260),   // Sensor
            new(410, 110),   // Inter-1
            new(720, 260),   // Inter-2
            new(410, 410),   // Output
            new(410, 570),   // Dopamine
        };

        // --- Таймер ---
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) =>
        {
            if (!_paused) _sim.Step();
            Invalidate();
        };
        _timer.Start();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Space: _paused = !_paused; break;
            case Keys.Escape: Close(); break;

            case Keys.OemOpenBrackets:
                _sim.LearningRate = Math.Max(0.001, _sim.LearningRate / 1.3);
                break;
            case Keys.OemCloseBrackets:
                _sim.LearningRate = Math.Min(0.5, _sim.LearningRate * 1.3);
                break;

            case Keys.OemSemicolon:
                _sim.WeightDecay = Math.Max(0.00001, _sim.WeightDecay / 1.5);
                break;
            case Keys.OemQuotes:
                _sim.WeightDecay = Math.Min(0.05, _sim.WeightDecay * 1.5);
                break;

            case Keys.Oemcomma:
                _sim.AMinus = Math.Max(0.0, _sim.AMinus - 0.1);
                break;
            case Keys.OemPeriod:
                _sim.AMinus = Math.Min(2.0, _sim.AMinus + 0.1);
                break;

            case Keys.D:
                _sim.Modulator?.Release();
                break;

            case Keys.R:
                foreach (var s in _net.Synapses) s.Weight = s.InitialWeight;
                foreach (var n in _net.Neurons)
                {
                    n.TotalSpikes = 0;
                    n.Trace = 0;
                    n.Potential = 0;
                    n.RefractoryRemaining = 0;
                }
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        foreach (var syn in _net.Synapses)
            DrawSynapse(g, syn);

        for (int i = 0; i < _net.Neurons.Count; i++)
            DrawNeuron(g, _net[i], _positions[i]);

        DrawHud(g);
    }

    private void DrawSynapse(Graphics g, Synapse syn)
    {
        PointF a = _positions[syn.PreIndex];
        PointF b = _positions[syn.PostIndex];

        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float ux = dx / len, uy = dy / len;

        PointF p1 = new(a.X + ux * Radius, a.Y + uy * Radius);
        PointF p2 = new(b.X - ux * Radius, b.Y - uy * Radius);

        bool excit = syn.Weight > 0;
        float wAbs = (float)Math.Abs(syn.Weight);
        int alpha = (int)(90 + 130 * Math.Min(wAbs, 1.0));
        Color color = excit
            ? Color.FromArgb(alpha, 80, 170, 255)
            : Color.FromArgb(alpha, 255, 90, 90);
        float thickness = 2f + wAbs * 6f;

        using (var pen = new Pen(color, thickness)
        { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, p1, p2);

        PointF arrowAt = new(p1.X + (p2.X - p1.X) * 0.82f,
                             p1.Y + (p2.Y - p1.Y) * 0.82f);
        DrawArrowHead(g, arrowAt, ux, uy, color);

        // Подпись веса (с историей если дрейфанул)
        bool drifted = Math.Abs(syn.Weight - syn.InitialWeight) > 0.01;
        string label = drifted
            ? $"w={syn.InitialWeight:+0.00;-0.00}\u2192{syn.Weight:+0.00;-0.00} d={syn.Delay}"
            : $"w={syn.Weight:+0.00;-0.00} d={syn.Delay}";

        using (var font = new Font("Consolas", 8.5f))
        using (var lbrush = new SolidBrush(Color.FromArgb(210, 210, 230, 250)))
        {
            var size = g.MeasureString(label, font);
            float lpT = 0.35f;
            float lx = p1.X + (p2.X - p1.X) * lpT;
            float ly = p1.Y + (p2.Y - p1.Y) * lpT;
            float px = -uy, py = ux;
            float fx = lx + px * 16 - size.Width / 2;
            float fy = ly + py * 16 - size.Height / 2;

            using (var bg = new SolidBrush(Color.FromArgb(180, 10, 14, 26)))
                g.FillRectangle(bg, fx - 3, fy - 1, size.Width + 6, size.Height + 2);

            g.DrawString(label, font, lbrush, fx, fy);
        }

        // Летящие посылки
        foreach (var (prog, val) in syn.InFlight())
        {
            float x = p1.X + (p2.X - p1.X) * prog;
            float y = p1.Y + (p2.Y - p1.Y) * prog;
            float dotR = 4f + (float)Math.Abs(val) * 7f;

            Color dotColor = excit
                ? Color.FromArgb(240, 160, 230, 255)
                : Color.FromArgb(240, 255, 160, 160);

            using var db = new SolidBrush(dotColor);
            g.FillEllipse(db, x - dotR, y - dotR, dotR * 2, dotR * 2);
        }
    }

    private static void DrawArrowHead(Graphics g, PointF tip, float ux, float uy, Color color)
    {
        float size = 12;
        PointF left = new(tip.X - ux * size + uy * size * 0.5f,
                          tip.Y - uy * size - ux * size * 0.5f);
        PointF right = new(tip.X - ux * size - uy * size * 0.5f,
                           tip.Y - uy * size + ux * size * 0.5f);
        using var b = new SolidBrush(color);
        g.FillPolygon(b, new[] { tip, left, right });
    }

    private void DrawNeuron(Graphics g, Neuron n, PointF c)
    {
        bool isModulator = n.Name == "Dopamine";
        float t = (float)Math.Clamp(n.Potential / n.Threshold, 0, 1);

        // Свечение при спайке
        if (n.SpikedThisTick)
        {
            Color glow = isModulator
                ? Color.FromArgb(255, 200, 120, 255)
                : Color.FromArgb(255, 255, 70, 70);
            for (int ring = 3; ring >= 1; ring--)
            {
                int alpha = 60 / ring;
                float r = Radius * (1 + ring * 0.35f);
                using var gb = new SolidBrush(Color.FromArgb(
                    alpha, glow.R, glow.G, glow.B));
                g.FillEllipse(gb, c.X - r, c.Y - r, r * 2, r * 2);
            }
        }

        // Заливка
        Color fill = isModulator
            ? Lerp(Color.FromArgb(40, 20, 60), Color.FromArgb(220, 120, 255), t)
            : Lerp(Color.FromArgb(28, 36, 54), Color.FromArgb(255, 217, 61), t);
        using (var b = new SolidBrush(fill))
            g.FillEllipse(b, c.X - Radius, c.Y - Radius, Radius * 2, Radius * 2);

        // Дуга прогресса
        if (n.Potential > 0.001 && !n.SpikedThisTick)
        {
            float r = Radius + 5;
            float sweep = 360f * t;
            Color arcCol = isModulator
                ? Color.FromArgb(240, 220, 140, 255)
                : Color.FromArgb(240, 255, 200, 60);
            using var pen = new Pen(arcCol, 4)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, c.X - r, c.Y - r, r * 2, r * 2, -90, sweep);
        }

        // Рефрактерный период
        if (n.InRefractory)
        {
            float r = Radius + 3;
            using var pen = new Pen(Color.FromArgb(180, 130, 130, 160), 2)
            { DashStyle = DashStyle.Dot };
            g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
        }

        // Контур
        Color outlineCol = n.SpikedThisTick
            ? (isModulator
                ? Color.FromArgb(255, 220, 150, 255)
                : Color.FromArgb(255, 255, 120, 120))
            : (isModulator
                ? Color.FromArgb(180, 160, 90, 220)
                : Color.FromArgb(180, 100, 140, 200));
        using (var pen = new Pen(outlineCol, 2))
            g.DrawEllipse(pen, c.X - Radius, c.Y - Radius, Radius * 2, Radius * 2);

        // Имя
        using (var font = new Font("Segoe UI", 10.5f, FontStyle.Bold))
        {
            var size = g.MeasureString(n.Name, font);
            using var tb = new SolidBrush(Color.White);
            g.DrawString(n.Name, font, tb,
                c.X - size.Width / 2, c.Y - size.Height / 2);
        }

        // Информация
        using (var font = new Font("Consolas", 8.5f))
        {
            string info = isModulator
                ? $"V={n.Potential:F2}  \U0001F4A7{_sim.Modulator?.Level:F2}"
                : $"V={n.Potential:F2}  \u26A1{n.TotalSpikes}";
            var size = g.MeasureString(info, font);
            using var tb = new SolidBrush(Color.FromArgb(220, 200, 220, 240));
            g.DrawString(info, font, tb,
                c.X - size.Width / 2, c.Y + Radius + 8);
        }
    }

    private void DrawHud(Graphics g)
    {
        using var font = new Font("Consolas", 10);
        using var b = new SolidBrush(Color.FromArgb(220, 200, 220, 240));

        g.DrawString($"Tick: {_sim.Tick}", font, b, 12, 10);

        string status = _paused
            ? "\u23F8 PAUSED   [Space] play/pause   [Esc] exit"
            : "\u25B6 RUNNING  [Space] pause      [Esc] exit";
        g.DrawString(status, font, b, 12, 30);

        if (_sim.Modulator != null)
        {
            double lvl = _sim.Modulator.Level;
            double gain = _sim.Modulator.PlasticityGain;
            using var fontM = new Font("Consolas", 10);
            using var bM = new SolidBrush(Color.FromArgb(
                (int)(80 + 175 * lvl), 220, 120, 255));
            g.DrawString($"Dopamine: {lvl:F2}  (plasticity \u00D7{gain:F2})",
                fontM, bM, 12, 50);

            int barW = 200, barX = 12, barY = 72;
            using var bgBar = new SolidBrush(Color.FromArgb(60, 60, 60, 90));
            using var fillBar = new SolidBrush(Color.FromArgb(
                (int)(150 + 105 * lvl), 220, 120, 255));
            g.FillRectangle(bgBar, barX, barY, barW, 8);
            g.FillRectangle(fillBar, barX, barY, (int)(barW * lvl), 8);
        }

        using var fontSmall = new Font("Consolas", 8.5f);
        using var b2 = new SolidBrush(Color.FromArgb(160, 180, 200, 230));
        g.DrawString(
            $"η={_sim.LearningRate:F4}  λ={_sim.WeightDecay:F5}  A−={_sim.AMinus:F1}   " +
            "[ ] η   ; ' λ   , . A−   D dopamine   R reset",
            fontSmall, b2, 12, 92);

        g.DrawString(
            "blue = excitation (+)   red = inhibition (−)   purple = neuromodulator   dot = signal",
            fontSmall, b2, 12, ClientSize.Height - 22);
    }

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}