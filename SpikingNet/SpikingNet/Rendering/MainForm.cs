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

    private const string CheckpointPath = "checkpoints/state.bin";
    private long _lastAutoSave = 0;
    private string _statusMessage = "";
    private long _statusUntil = 0;

    // Метрики baseline
    private readonly Queue<int> _spikeWindow = new();
    private const int SpikeWindowSize = 100;
    private long[] _neuronSpikesSnapshot = Array.Empty<long>();
    private long _snapshotTick = 0;

    // Быстрый прогон
    private long _fastForwardTarget = 0;

    public MainForm()
    {
        Text = "Spiking Network — 5 neurons + homeostasis";
        ClientSize = new Size(1100, 700);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(10, 14, 26);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += OnKeyDown;

        // ==================== СЕТЬ ====================
        _net = new Network();

        var reflex = _net.AddRegion("Reflex");
        var modulatory = _net.AddRegion("Modulatory");

        var sensor = _net.AddNeuron(reflex, "Sensor");
        var inter1 = _net.AddNeuron(reflex, "Inter-1");
        var inter2 = _net.AddNeuron(reflex, "Inter-2");
        var output = _net.AddNeuron(reflex, "Output");
        var dopamine = _net.AddNeuron(modulatory, "Dopamine");

        // Пониженный порог — чтобы shot noise мог пробить
        foreach (var n in _net.Neurons)
        {
            n.Threshold = 0.6;
            n.BaseThreshold = 0.6;
        }

        // Внутрирегиональные связи
        _net.Connect(sensor, inter1, 0.6, 1);
        _net.Connect(sensor, inter2, 0.4, 2);
        _net.Connect(inter1, inter2, 0.5, 1);
        _net.Connect(inter2, output, 0.7, 1);
        _net.Connect(output, inter1, -0.3, 2);

        // Межрегиональная связь
        _net.Connect(output, dopamine, 0.9, 1);

        // ==================== СИМУЛЯЦИЯ ====================
        _sim = new Simulation(_net);

        // Шум — единственный источник "жизни"
        _sim.NoiseAmplitude = 1.5;

        // Параметры обучения
        _sim.PlasticityEnabled = true;
        _sim.LearningRate = 0.05;
        _sim.APlus = 1.0;
        _sim.AMinus = 0.4;
        _sim.WeightDecay = 0.0005;

        // Нейромодулятор
        var dopa = new Neuromodulator("Dopamine")
        {
            Decay = 0.93,
            Baseline = 0.05,
            ReleaseAmount = 1.0,
        };
        modulatory.Modulator = dopa;
        _sim.Modulator = dopa;

        // Гомеостаз — регулятор
        _sim.Homeostasis = new Homeostasis(windowSize: 100)
        {
            ActivityTarget = 0.12,
            WeightNormTarget = 5.0,
        };

        // Старый триггер дофамина по спайку — отключаем
        _sim.ModulatorTriggerIndex = -1;

        // ==================== КООРДИНАТЫ ====================
        _positions = new PointF[]
        {
            new(220, 300),   // Sensor
            new(430, 150),   // Inter-1
            new(640, 300),   // Inter-2
            new(430, 450),   // Output
            new(430, 590),   // Dopamine
        };

        // ==================== СНАПШОТ ====================
        _neuronSpikesSnapshot = new long[_net.Neurons.Count];
        for (int i = 0; i < _net.Neurons.Count; i++)
            _neuronSpikesSnapshot[i] = _net[i].TotalSpikes;

        // ==================== ТАЙМЕР ====================
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) =>
        {
            if (!_paused)
            {
                // Быстрый прогон: делаем пачку тактов без перерисовки
                int stepsThisFrame = 1;
                if (_fastForwardTarget > 0)
                {
                    stepsThisFrame = (int)Math.Min(500, _fastForwardTarget);
                    _fastForwardTarget -= stepsThisFrame;
                }

                for (int s = 0; s < stepsThisFrame; s++)
                {
                    _sim.Step();

                    int spikesThisTick = 0;
                    for (int i = 0; i < _net.Neurons.Count; i++)
                        if (_net[i].SpikedThisTick) spikesThisTick++;
                    _spikeWindow.Enqueue(spikesThisTick);
                    while (_spikeWindow.Count > SpikeWindowSize) _spikeWindow.Dequeue();

                    if (_sim.Tick - _lastAutoSave >= 1000)
                    {
                        TrySave();
                        _lastAutoSave = _sim.Tick;
                    }
                }
            }
            Invalidate();
        };
        _timer.Start();

        // Автосохранение при закрытии
        FormClosing += (_, _) => TrySave();
    }

    // ============================================================
    // КЛАВИШИ
    // ============================================================
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Space: _paused = !_paused; break;
            case Keys.Escape: Close(); break;

            case Keys.F5: TrySave(); break;
            case Keys.F9: TryLoad(); break;

            case Keys.T:
                _sim.PerturbationNeuron = new Random().Next(_net.Neurons.Count);
                _sim.PerturbationTicksLeft = 10;
                _statusMessage = $"perturbation → {_net[_sim.PerturbationNeuron].Name}";
                _statusUntil = _sim.Tick + 40;
                break;

            case Keys.R:
                _spikeWindow.Clear();
                for (int i = 0; i < _net.Neurons.Count; i++)
                    _neuronSpikesSnapshot[i] = _net[i].TotalSpikes;
                _snapshotTick = _sim.Tick;
                break;

            // Быстрый прогон
            case Keys.D1: _fastForwardTarget += 5_000; break;
            case Keys.D2: _fastForwardTarget += 20_000; break;
            case Keys.D3: _fastForwardTarget += 100_000; break;

            // LearningRate
            case Keys.OemOpenBrackets:
                _sim.LearningRate = Math.Max(0.001, _sim.LearningRate / 1.3); break;
            case Keys.OemCloseBrackets:
                _sim.LearningRate = Math.Min(0.5, _sim.LearningRate * 1.3); break;

            // WeightDecay
            case Keys.OemSemicolon:
                _sim.WeightDecay = Math.Max(0.00001, _sim.WeightDecay / 1.5); break;
            case Keys.OemQuotes:
                _sim.WeightDecay = Math.Min(0.05, _sim.WeightDecay * 1.5); break;

            // AMinus
            case Keys.Oemcomma:
                _sim.AMinus = Math.Max(0.0, _sim.AMinus - 0.1); break;
            case Keys.OemPeriod:
                _sim.AMinus = Math.Min(2.0, _sim.AMinus + 0.1); break;

            case Keys.W:
                System.Diagnostics.Debug.WriteLine($"=== tick {_sim.Tick} ===");
                System.Diagnostics.Debug.WriteLine(DebugDump.DumpWeights(_net));
                _statusMessage = "weights dumped to Debug Output";
                _statusUntil = _sim.Tick + 40;
                break;
        }
    }

    // ============================================================
    // СОХРАНЕНИЕ / ЗАГРУЗКА
    // ============================================================
    private void TrySave()
    {
        try
        {
            Checkpoint.Save(CheckpointPath, _net, _sim, _sim.Modulator);
            _statusMessage = $"saved @ tick {_sim.Tick}";
            _statusUntil = _sim.Tick + 40;
        }
        catch (Exception ex)
        {
            _statusMessage = $"SAVE FAILED: {ex.Message}";
            _statusUntil = _sim.Tick + 200;
        }
    }

    private void TryLoad()
    {
        try
        {
            Checkpoint.Load(CheckpointPath, _net, _sim, _sim.Modulator);
            _lastAutoSave = _sim.Tick;
            _statusMessage = $"loaded @ tick {_sim.Tick}";
            _statusUntil = _sim.Tick + 40;
        }
        catch (Exception ex)
        {
            _statusMessage = $"LOAD FAILED: {ex.Message}";
            _statusUntil = _sim.Tick + 200;
        }
    }

    // ============================================================
    // ОТРИСОВКА
    // ============================================================
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 1. Трубы
        foreach (var syn in _net.Synapses)
            DrawSynapse(g, syn);

        // 2. Нейроны
        for (int i = 0; i < _net.Neurons.Count; i++)
            DrawNeuron(g, _net[i], _positions[i]);

        // 3. HUD
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
        int alpha = (int)(90 + 130 * Math.Min(wAbs, 2.0) / 2.0);
        Color color = excit
            ? Color.FromArgb(alpha, 80, 170, 255)
            : Color.FromArgb(alpha, 255, 90, 90);
        float thickness = 2f + wAbs * 4f;

        using (var pen = new Pen(color, thickness)
        { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, p1, p2);

        PointF arrowAt = new(p1.X + (p2.X - p1.X) * 0.82f,
                             p1.Y + (p2.Y - p1.Y) * 0.82f);
        DrawArrowHead(g, arrowAt, ux, uy, color);

        // Подпись веса — показываем и старт, и текущий, если разошлись
        bool drifted = Math.Abs(syn.Weight - syn.InitialWeight) > 0.01;
        string label = drifted
            ? $"w={syn.InitialWeight:+0.00;-0.00}→{syn.Weight:+0.00;-0.00} d={syn.Delay}"
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
            float dotR = 4f + (float)Math.Abs(val) * 4f;

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
        float t = (float)Math.Clamp(n.Potential / Math.Max(0.01, n.Threshold), 0, 1);
        bool isModulator = n.Name == "Dopamine";

        // Свечение при спайке
        if (n.SpikedThisTick)
        {
            for (int ring = 3; ring >= 1; ring--)
            {
                int alpha = 60 / ring;
                float r = Radius * (1 + ring * 0.35f);
                Color glowColor = isModulator
                    ? Color.FromArgb(alpha, 220, 120, 255)
                    : Color.FromArgb(alpha, 255, 70, 70);
                using var gb = new SolidBrush(glowColor);
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
            using var pen = new Pen(Color.FromArgb(240, 255, 200, 60), 4)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, c.X - r, c.Y - r, r * 2, r * 2, -90, sweep);
        }

        // Рефрактерность
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
                ? Color.FromArgb(255, 220, 120, 255)
                : Color.FromArgb(255, 255, 120, 120))
            : Color.FromArgb(180, 100, 140, 200);
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

        // Информация снизу
        using (var font = new Font("Consolas", 8.5f))
        {
            string info = isModulator && _sim.Modulator != null
                ? $"V={n.Potential:F2}  \U0001F4A7{_sim.Modulator.Level:F2}"
                : $"V={n.Potential:F2}  \u26A1{n.TotalSpikes}";
            var size = g.MeasureString(info, font);
            using var tb = new SolidBrush(Color.FromArgb(220, 200, 220, 240));
            g.DrawString(info, font, tb,
                c.X - size.Width / 2, c.Y + Radius + 8);
        }
    }

    // ============================================================
    // HUD
    // ============================================================
    private void DrawHud(Graphics g)
    {
        using var fontMono = new Font("Consolas", 10);
        using var fontSmall = new Font("Consolas", 8.5f);
        using var brushText = new SolidBrush(Color.FromArgb(220, 200, 220, 240));
        using var brushDim = new SolidBrush(Color.FromArgb(160, 180, 200, 230));

        // ============ ЛЕВАЯ КОЛОНКА ============
        float y = 10;

        g.DrawString($"Tick: {_sim.Tick}", fontMono, brushText, 12, y); y += 18;
        string status = _paused
            ? "\u23F8 PAUSED   [Space] play   [Esc] exit"
            : "\u25B6 RUNNING  [Space] pause  [Esc] exit";
        g.DrawString(status, fontMono, brushText, 12, y); y += 18;

        if (_fastForwardTarget > 0)
        {
            using var bFF = new SolidBrush(Color.FromArgb(255, 255, 200, 100));
            g.DrawString($"FAST-FORWARD: {_fastForwardTarget} ticks left",
                fontMono, bFF, 12, y);
            y += 18;
        }

        // Дофамин + Discomfort
        if (_sim.Modulator != null && _sim.Homeostasis != null)
        {
            double lvl = _sim.Modulator.Level;
            double gain = _sim.Modulator.PlasticityGain;
            double disc = _sim.Homeostasis.Discomfort;

            using var brushDopa = new SolidBrush(Color.FromArgb(
                (int)(80 + 175 * lvl), 220, 120, 255));
            g.DrawString($"Dopamine: {lvl:F2}  (×{gain:F2})",
                fontMono, brushDopa, 12, y); y += 16;

            using var brushDisc = new SolidBrush(Color.FromArgb(255, 200, 230, 255));
            g.DrawString($"Discomfort: {disc:F3}", fontMono, brushDisc, 12, y);
            y += 16;

            // Полоски
            int barW = 180, barH = 5;
            using var bgBar = new SolidBrush(Color.FromArgb(60, 60, 60, 90));
            using var fillDopa = new SolidBrush(Color.FromArgb(
                (int)(150 + 105 * lvl), 220, 120, 255));
            using var fillDisc = new SolidBrush(Color.FromArgb(230, 255, 120, 120));

            g.FillRectangle(bgBar, 12, (int)y, barW, barH);
            g.FillRectangle(fillDopa, 12, (int)y, (int)(barW * lvl), barH);
            y += 8;

            g.FillRectangle(bgBar, 12, (int)y, barW, barH);
            g.FillRectangle(fillDisc, 12, (int)y,
                (int)(barW * Math.Min(1.0, disc / 2.0)), barH);
            y += 12;
        }

        // Разделитель
        using (var sepPen = new Pen(Color.FromArgb(80, 100, 140, 200)))
            g.DrawLine(sepPen, 12, y, 260, y);
        y += 8;

        // Параметры обучения
        g.DrawString($"η={_sim.LearningRate:F4}  λ={_sim.WeightDecay:F5}  " +
                     $"A−={_sim.AMinus:F1}", fontMono, brushText, 12, y);
        y += 16;
        g.DrawString("[ ] η   ; ' λ   , . A−   R reset",
            fontSmall, brushDim, 12, y);
        y += 18;

        // Регионы
        g.DrawString("Regions:", fontSmall, brushDim, 12, y); y += 14;
        foreach (var r in _net.Regions)
        {
            g.DrawString($"  {r.Name,-12} {r.Neurons.Count,3}N {r.Synapses.Count,3}S",
                fontSmall, brushDim, 12, y);
            y += 14;
        }

        // Статус сохранения
        if (_sim.Tick < _statusUntil)
        {
            using var brushStatus = new SolidBrush(Color.FromArgb(220, 255, 220, 100));
            g.DrawString(_statusMessage, fontMono, brushStatus, 12, y);
            y += 18;
        }

        // ============ ПРАВАЯ КОЛОНКА — метрики ============
        int rightX = ClientSize.Width - 260;
        float yR = 10;

        using var fontMetric = new Font("Consolas", 10, FontStyle.Bold);
        using var brushMetric = new SolidBrush(Color.FromArgb(255, 200, 230, 255));

        g.DrawString("─── METRICS ───", fontSmall, brushDim, rightX, yR); yR += 16;

        double activityLevel = _spikeWindow.Count == 0
            ? 0
            : _spikeWindow.Average() / _net.Neurons.Count;

        double weightNorm = 0;
        foreach (var s in _net.Synapses) weightNorm += Math.Abs(s.Weight);

        g.DrawString($"ActivityLevel: {activityLevel:F4}",
            fontMetric, brushMetric, rightX, yR); yR += 16;
        g.DrawString("  (target 0.05–0.20)",
            fontSmall, brushDim, rightX, yR); yR += 14;

        g.DrawString($"WeightNorm: {weightNorm:F3}",
            fontMetric, brushMetric, rightX, yR); yR += 16;

        if (_sim.Homeostasis != null)
        {
            g.DrawString($"Discomfort: {_sim.Homeostasis.Discomfort:F3}",
                fontMetric, brushMetric, rightX, yR); yR += 16;

            g.DrawString($"λ×{_sim.Homeostasis.WeightDecayMultiplier:F1}  " +
                         $"θ×{_sim.Homeostasis.ThresholdMultiplier:F2}",
                fontSmall, brushDim, rightX, yR);
            yR += 16;
        }

        g.DrawString($"Snapshot @ tick {_snapshotTick}",
            fontSmall, brushDim, rightX, yR); yR += 14;
        g.DrawString("(press R to reset counters)",
            fontSmall, brushDim, rightX, yR); yR += 18;

        // Распределение спайков
        g.DrawString("Spike distribution:", fontSmall, brushDim, rightX, yR);
        yR += 14;
        foreach (var n in _net.Neurons)
        {
            long delta = n.TotalSpikes - _neuronSpikesSnapshot[n.Index];
            g.DrawString($"  {n.Name,-10} {delta,6}",
                fontSmall, brushDim, rightX, yR);
            yR += 13;
        }

        // ============ НИЗ ============
        g.DrawString("[F5] save  [F9] load  [T] perturb  [R] reset  [1/2/3] fast-forward 5k/20k/100k [W] dump weights",
            fontSmall, brushDim, 12, ClientSize.Height - 40);
        g.DrawString("blue = excitation (+)   red = inhibition (−)   purple = neuromodulator",
            fontSmall, brushDim, 12, ClientSize.Height - 22);
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