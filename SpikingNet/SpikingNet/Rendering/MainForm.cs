using System.Drawing.Drawing2D;
using SpikingNet.Core;

namespace SpikingNet.Rendering;

public sealed class MainForm : Form
{
    private readonly Network _net;
    private readonly Simulation _sim;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PointF[] _positions;

    private const float Radius = 38f;
    private bool _paused;

    private const string CheckpointPath = "checkpoints/state.bin";
    private long _lastAutoSave = 0;
    private string _statusMessage = "";
    private long _statusUntil = 0;

    // Метрики
    private readonly Queue<int> _spikeWindow = new();
    private const int SpikeWindowSize = 100;
    private long[] _neuronSpikesSnapshot = Array.Empty<long>();
    private long _snapshotTick = 0;

    // Быстрый прогон
    private long _fastForwardTarget = 0;

    // Мир
    private Core.Environment _env = null!;
    private EnvironmentObserver _envObs = null!;

    public MainForm()
    {
        Text = "Spiking Network — agent with environment";
        ClientSize = new Size(1100, 760);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(10, 14, 26);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        KeyDown += OnKeyDown;

        // ==================== СЕТЬ ====================
        _net = new Network();

        var reflex = _net.AddRegion("Reflex");
        var modulatory = _net.AddRegion("Modulatory");
        var sensory = _net.AddRegion("Sensory");
        var motor = _net.AddRegion("Motor");
        var inhibitory = _net.AddRegion("Inhibitory");

        // --- Reflex (старая сеть) ---
        var sensor = _net.AddNeuron(reflex, "Sensor");
        var inter1 = _net.AddNeuron(reflex, "Inter-1");
        var inter2 = _net.AddNeuron(reflex, "Inter-2");
        var output = _net.AddNeuron(reflex, "Output");

        // --- Modulatory ---
        var dopamine = _net.AddNeuron(modulatory, "Dopamine");

        // --- Sensory ---
        var foodRight = _net.AddNeuron(sensory, "FoodRight");
        var foodLeft = _net.AddNeuron(sensory, "FoodLeft");
        var satiety = _net.AddNeuron(sensory, "Satiety");

        // --- Motor ---
        var moveRight = _net.AddNeuron(motor, "MoveRight");
        var moveLeft = _net.AddNeuron(motor, "MoveLeft");

        // --- Inhibitory ---
        var stop = _net.AddNeuron(inhibitory, "Stop");

        // === Пороги ===
        // Старая сеть: порог 0.6 — чтобы шум мог пробить
        foreach (var n in new[] { sensor, inter1, inter2, output, dopamine })
        {
            n.Threshold = 0.6;
            n.BaseThreshold = 0.6;
        }
        // Сенсоры: реагируют на слабый сигнал
        foodRight.Threshold = foodRight.BaseThreshold = 0.5;
        foodLeft.Threshold = foodLeft.BaseThreshold = 0.5;
        satiety.Threshold = satiety.BaseThreshold = 0.5;
        // Motor: нужен согласованный сигнал
        moveRight.Threshold = moveRight.BaseThreshold = 0.9;
        moveLeft.Threshold = moveLeft.BaseThreshold = 0.9;
        // Stop: реагирует на satiety
        stop.Threshold = stop.BaseThreshold = 1.0;

        // === Связи старой сети ===
        _net.Connect(sensor, inter1, 0.6, 1);
        _net.Connect(sensor, inter2, 0.4, 2);
        _net.Connect(inter1, inter2, 0.5, 1);
        _net.Connect(inter2, output, 0.7, 1);
        _net.Connect(output, inter1, -0.3, 2);
        _net.Connect(output, dopamine, 0.9, 1);

        // === Sensory → Motor ===
        _net.Connect(foodRight, moveRight, 1.2, 1);
        _net.Connect(foodLeft, moveLeft, 1.2, 1);

        // === Sensory → Inhibitory ===
        _net.Connect(satiety, stop, 2.0, 1);

        // === Inhibitory → Motor ===
        _net.Connect(stop, moveRight, -1.5, 1);
        _net.Connect(stop, moveLeft, -1.5, 1);

        // === Motor латеральное торможение ===
        _net.Connect(moveRight, moveLeft, -0.5, 1);
        _net.Connect(moveLeft, moveRight, -0.5, 1);

        // === Motor → Reflex.Output (диагностика) ===
        _net.Connect(moveRight, output, 0.3, 1);
        _net.Connect(moveLeft, output, 0.3, 1);

        // ==================== СИМУЛЯЦИЯ ====================
        _sim = new Simulation(_net);
        _sim.NoiseAmplitude = 1.5;

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
        _sim.ModulatorTriggerIndex = -1;

        // Гомеостаз + энергия
        _sim.Homeostasis = new Homeostasis(windowSize: 100)
        {
            ActivityTarget = 0.12,
            WeightNormTarget = 5.0,
            SpikeCost = 0.0008,
            PassiveDrain = 0.00001,
            FoodRestore = 0.4,
        };

        // Среда
        _env = new Core.Environment(seed: 42);
        _envObs = new EnvironmentObserver(_env, _sim.Homeostasis, moveRight, moveLeft);
        _sim.AddObserver(_envObs);

        // Сенсорный вход
        var envStim = new EnvironmentStimulus(_env, _sim.Homeostasis,
            foodRight, foodLeft, satiety);
        _sim.AddStimulus(envStim);

        // ==================== КООРДИНАТЫ ====================
        _positions = new PointF[]
        {
            // Reflex (4)
            new(280, 200),   // Sensor
            new(470, 100),   // Inter-1
            new(660, 200),   // Inter-2
            new(470, 310),   // Output
            // Modulatory (1)
            new(470, 410),   // Dopamine
            // Sensory (3)
            new(120, 460),   // FoodRight
            new(120, 530),   // FoodLeft
            new(120, 600),   // Satiety
            // Motor (2)
            new(830, 460),   // MoveRight
            new(830, 530),   // MoveLeft
            // Inhibitory (1)
            new(830, 600),   // Stop
        };

        // ==================== SNAPSHOT ====================
        _neuronSpikesSnapshot = new long[_net.Neurons.Count];
        for (int i = 0; i < _net.Neurons.Count; i++)
            _neuronSpikesSnapshot[i] = _net[i].TotalSpikes;

        // ==================== ТАЙМЕР ====================
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) =>
        {
            if (!_paused)
            {
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

            case Keys.D1: _fastForwardTarget += 5_000; break;
            case Keys.D2: _fastForwardTarget += 20_000; break;
            case Keys.D3: _fastForwardTarget += 100_000; break;

            case Keys.OemOpenBrackets: _sim.LearningRate = Math.Max(0.001, _sim.LearningRate / 1.3); break;
            case Keys.OemCloseBrackets: _sim.LearningRate = Math.Min(0.5, _sim.LearningRate * 1.3); break;
            case Keys.OemSemicolon: _sim.WeightDecay = Math.Max(0.00001, _sim.WeightDecay / 1.5); break;
            case Keys.OemQuotes: _sim.WeightDecay = Math.Min(0.05, _sim.WeightDecay * 1.5); break;
            case Keys.Oemcomma: _sim.AMinus = Math.Max(0.0, _sim.AMinus - 0.1); break;
            case Keys.OemPeriod: _sim.AMinus = Math.Min(2.0, _sim.AMinus + 0.1); break;

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

        foreach (var syn in _net.Synapses)
            DrawSynapse(g, syn);

        for (int i = 0; i < _net.Neurons.Count; i++)
            DrawNeuron(g, _net[i], _positions[i]);

        DrawWorldStrip(g);
        DrawHud(g);
    }

    private void DrawSynapse(Graphics g, Synapse syn)
    {
        PointF a = _positions[syn.PreIndex];
        PointF b = _positions[syn.PostIndex];

        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.1f) return;
        float ux = dx / len, uy = dy / len;

        PointF p1 = new(a.X + ux * Radius, a.Y + uy * Radius);
        PointF p2 = new(b.X - ux * Radius, b.Y - uy * Radius);

        bool excit = syn.Weight > 0;
        float wAbs = (float)Math.Abs(syn.Weight);
        int alpha = (int)(90 + 130 * Math.Min(wAbs, 2.0) / 2.0);
        Color color = excit
            ? Color.FromArgb(alpha, 80, 170, 255)
            : Color.FromArgb(alpha, 255, 90, 90);
        float thickness = 1.5f + wAbs * 3f;

        using (var pen = new Pen(color, thickness)
        { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, p1, p2);

        PointF arrowAt = new(p1.X + (p2.X - p1.X) * 0.85f,
                             p1.Y + (p2.Y - p1.Y) * 0.85f);
        DrawArrowHead(g, arrowAt, ux, uy, color);

        // Подпись веса — только у крупных связей
        if (wAbs > 0.5 || excit)
        {
            bool drifted = Math.Abs(syn.Weight - syn.InitialWeight) > 0.05;
            string label = drifted
                ? $"{syn.InitialWeight:+0.0;-0.0}→{syn.Weight:+0.0;-0.0}"
                : $"{syn.Weight:+0.0;-0.0}";

            using (var font = new Font("Consolas", 7.5f))
            using (var lbrush = new SolidBrush(Color.FromArgb(180, 210, 230, 250)))
            {
                var size = g.MeasureString(label, font);
                float lx = p1.X + (p2.X - p1.X) * 0.5f - size.Width / 2;
                float ly = p1.Y + (p2.Y - p1.Y) * 0.5f - size.Height / 2;
                using (var bg = new SolidBrush(Color.FromArgb(160, 10, 14, 26)))
                    g.FillRectangle(bg, lx - 2, ly - 1, size.Width + 4, size.Height + 2);
                g.DrawString(label, font, lbrush, lx, ly);
            }
        }

        foreach (var (prog, val) in syn.InFlight())
        {
            float x = p1.X + (p2.X - p1.X) * prog;
            float y = p1.Y + (p2.Y - p1.Y) * prog;
            float dotR = 3f + (float)Math.Abs(val) * 3f;

            Color dotColor = excit
                ? Color.FromArgb(240, 160, 230, 255)
                : Color.FromArgb(240, 255, 160, 160);

            using var db = new SolidBrush(dotColor);
            g.FillEllipse(db, x - dotR, y - dotR, dotR * 2, dotR * 2);
        }
    }

    private static void DrawArrowHead(Graphics g, PointF tip, float ux, float uy, Color color)
    {
        float size = 10;
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
        bool isMotor = n.Region?.Name == "Motor";
        bool isSensory = n.Region?.Name == "Sensory";
        bool isStop = n.Name == "Stop";

        if (n.SpikedThisTick)
        {
            for (int ring = 3; ring >= 1; ring--)
            {
                int alpha = 60 / ring;
                float r = Radius * (1 + ring * 0.35f);
                Color glowColor = isModulator
                    ? Color.FromArgb(alpha, 220, 120, 255)
                    : isMotor
                        ? Color.FromArgb(alpha, 120, 255, 180)
                        : Color.FromArgb(alpha, 255, 70, 70);
                using var gb = new SolidBrush(glowColor);
                g.FillEllipse(gb, c.X - r, c.Y - r, r * 2, r * 2);
            }
        }

        Color fill = isModulator
            ? Lerp(Color.FromArgb(40, 20, 60), Color.FromArgb(220, 120, 255), t)
            : isMotor
                ? Lerp(Color.FromArgb(20, 50, 40), Color.FromArgb(120, 255, 180), t)
                : isSensory
                    ? Lerp(Color.FromArgb(20, 40, 60), Color.FromArgb(120, 200, 255), t)
                    : isStop
                        ? Lerp(Color.FromArgb(50, 20, 20), Color.FromArgb(255, 120, 120), t)
                        : Lerp(Color.FromArgb(28, 36, 54), Color.FromArgb(255, 217, 61), t);

        using (var b = new SolidBrush(fill))
            g.FillEllipse(b, c.X - Radius, c.Y - Radius, Radius * 2, Radius * 2);

        if (n.Potential > 0.001 && !n.SpikedThisTick)
        {
            float r = Radius + 5;
            float sweep = 360f * t;
            using var pen = new Pen(Color.FromArgb(240, 255, 200, 60), 3)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, c.X - r, c.Y - r, r * 2, r * 2, -90, sweep);
        }

        if (n.InRefractory)
        {
            float r = Radius + 3;
            using var pen = new Pen(Color.FromArgb(180, 130, 130, 160), 2)
            { DashStyle = DashStyle.Dot };
            g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
        }

        Color outlineCol = n.SpikedThisTick
            ? (isModulator
                ? Color.FromArgb(255, 220, 120, 255)
                : isMotor
                    ? Color.FromArgb(255, 120, 255, 180)
                    : Color.FromArgb(255, 255, 120, 120))
            : Color.FromArgb(180, 100, 140, 200);
        using (var pen = new Pen(outlineCol, 2))
            g.DrawEllipse(pen, c.X - Radius, c.Y - Radius, Radius * 2, Radius * 2);

        using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
        {
            var size = g.MeasureString(n.Name, font);
            using var tb = new SolidBrush(Color.White);
            g.DrawString(n.Name, font, tb,
                c.X - size.Width / 2, c.Y - size.Height / 2);
        }

        using (var font = new Font("Consolas", 7.5f))
        {
            string info = isModulator && _sim.Modulator != null
                ? $"V={n.Potential:F2}"
                : $"V={n.Potential:F2}";
            var size = g.MeasureString(info, font);
            using var tb = new SolidBrush(Color.FromArgb(200, 200, 220, 240));
            g.DrawString(info, font, tb,
                c.X - size.Width / 2, c.Y + Radius + 4);
        }
    }

    private void DrawWorldStrip(Graphics g)
    {
        float worldY = ClientSize.Height - 75;
        float worldX1 = 60, worldX2 = ClientSize.Width - 60;

        using (var bgBar = new SolidBrush(Color.FromArgb(40, 60, 90, 140)))
            g.FillRectangle(bgBar, worldX1, worldY - 8, worldX2 - worldX1, 16);

        using (var worldPen = new Pen(Color.FromArgb(120, 100, 140, 200), 2))
            g.DrawLine(worldPen, worldX1, worldY, worldX2, worldY);

        float agentPx = worldX1 + (float)(_env.AgentNormalized * (worldX2 - worldX1));
        using (var ab = new SolidBrush(Color.FromArgb(255, 100, 200, 255)))
            g.FillEllipse(ab, agentPx - 7, worldY - 7, 14, 14);
        using (var ap = new Pen(Color.FromArgb(255, 200, 240, 255), 1))
            g.DrawEllipse(ap, agentPx - 7, worldY - 7, 14, 14);

        float foodPx = worldX1 + (float)(_env.FoodNormalized * (worldX2 - worldX1));
        using (var fb = new SolidBrush(Color.FromArgb(255, 120, 255, 120)))
            g.FillEllipse(fb, foodPx - 6, worldY - 6, 12, 12);

        using var font = new Font("Consolas", 8.5f);
        using var wb = new SolidBrush(Color.FromArgb(180, 200, 220, 240));
        g.DrawString("World  (agent hunts food)", font, wb, worldX1, worldY - 26);
        g.DrawString($"X={_env.AgentX:F1}", font, wb, agentPx - 20, worldY + 12);
        g.DrawString($"Food X={_env.FoodX:F1}", font, wb, foodPx - 30, worldY + 12);
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

        using (var sepPen = new Pen(Color.FromArgb(80, 100, 140, 200)))
            g.DrawLine(sepPen, 12, y, 260, y);
        y += 8;

        g.DrawString($"η={_sim.LearningRate:F4}  λ={_sim.WeightDecay:F5}  " +
                     $"A−={_sim.AMinus:F1}", fontMono, brushText, 12, y);
        y += 16;

        g.DrawString("Regions:", fontSmall, brushDim, 12, y); y += 14;
        foreach (var r in _net.Regions)
        {
            g.DrawString($"  {r.Name,-12} {r.Neurons.Count,3}N {r.Synapses.Count,3}S",
                fontSmall, brushDim, 12, y);
            y += 13;
        }

        if (_sim.Tick < _statusUntil)
        {
            using var brushStatus = new SolidBrush(Color.FromArgb(220, 255, 220, 100));
            g.DrawString(_statusMessage, fontMono, brushStatus, 12, y);
        }

        // ============ ПРАВАЯ КОЛОНКА ============
        int rightX = ClientSize.Width - 270;
        float yR = 10;

        using var fontMetric = new Font("Consolas", 10, FontStyle.Bold);
        using var brushMetric = new SolidBrush(Color.FromArgb(255, 200, 230, 255));

        g.DrawString("─── METRICS ───", fontSmall, brushDim, rightX, yR); yR += 16;

        // Energy
        double energy = _sim.Homeostasis?.Energy ?? 0;
        using var brushEnergy = new SolidBrush(Color.FromArgb(
            255, (int)(120 + 135 * (1 - energy)), 255, (int)(120 + 60 * energy)));
        g.DrawString($"Energy: {energy:F3}", fontMetric, brushEnergy, rightX, yR);
        yR += 16;

        int eBarW = 220, eBarH = 6;
        using (var bgBarE = new SolidBrush(Color.FromArgb(60, 60, 60, 90)))
        using (var fillE = new SolidBrush(Color.FromArgb(
            (int)(150 + 105 * energy), 120, 255, 120)))
        {
            g.FillRectangle(bgBarE, rightX, (int)yR, eBarW, eBarH);
            g.FillRectangle(fillE, rightX, (int)yR, (int)(eBarW * energy), eBarH);
        }
        yR += 12;

        // Мир
        g.DrawString($"Food eaten: {_env.FoodEaten}   Steps: {_env.Steps}",
            fontSmall, brushDim, rightX, yR); yR += 16;

        // Activity
        double activityLevel = _spikeWindow.Count == 0
            ? 0
            : _spikeWindow.Average() / _net.Neurons.Count;
        g.DrawString($"ActivityLevel: {activityLevel:F4}",
            fontMetric, brushMetric, rightX, yR); yR += 16;

        // WeightNorm
        double weightNorm = 0;
        foreach (var s in _net.Synapses) weightNorm += Math.Abs(s.Weight);
        g.DrawString($"WeightNorm: {weightNorm:F3}",
            fontMetric, brushMetric, rightX, yR); yR += 16;

        if (_sim.Homeostasis != null)
        {
            g.DrawString($"Discomfort: {_sim.Homeostasis.Discomfort:F3}",
                fontMetric, brushMetric, rightX, yR); yR += 16;
            g.DrawString($"λ×{_sim.Homeostasis.WeightDecayMultiplier:F1}  " +
                         $"θ×{_sim.Homeostasis.ThresholdMultiplier:F2}",
                fontSmall, brushDim, rightX, yR); yR += 16;
        }

        g.DrawString($"Snapshot @ tick {_snapshotTick}",
            fontSmall, brushDim, rightX, yR); yR += 14;

        // Распределение спайков
        g.DrawString("Spike distribution:", fontSmall, brushDim, rightX, yR);
        yR += 14;
        foreach (var n in _net.Neurons)
        {
            long delta = n.TotalSpikes - _neuronSpikesSnapshot[n.Index];
            g.DrawString($"  {n.Name,-10} {delta,6}",
                fontSmall, brushDim, rightX, yR);
            yR += 12;
        }

        // ============ НИЗ ============
        g.DrawString("[F5] save  [F9] load  [T] perturb  [R] reset  [1/2/3] fast-forward 5k/20k/100k  [W] dump weights",
            fontSmall, brushDim, 12, ClientSize.Height - 40);
        g.DrawString("blue = excitation   red = inhibition   purple = neuromodulator   green = motor   light-blue = sensory",
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