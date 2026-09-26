namespace AutomatedMacro;

public sealed record RunProgress(int Cycle, int TotalCycles, int StepIndex, int StepCount, StepNode? Step, string Text);

/// <summary>Izvodi korake redom i ponavlja cijelu proceduru u ciklusima. Radi na pozadinskoj dretvi.</summary>
public sealed class MacroRunner
{
    private sealed record Item(StepNode? Step, GroupNode? EndOfGroup);

    private readonly List<Item> _items = new();
    private readonly int _stepCount;
    private readonly int _cycles, _cycleDelayMs, _startDelaySec;

    /// <summary>Pozivaju se s pozadinske dretve.</summary>
    public event Action<RunProgress>? Progress;
    public event Action<string>? Log;
    public event Action<int>? CycleCompleted;

    /// <param name="nodes">Cvorovi koje treba izvesti (grupe se razmotaju).</param>
    public MacroRunner(IEnumerable<MacroNode> nodes, RunSettings settings, bool singlePass = false)
    {
        foreach (var n in nodes) Flatten(n);
        _stepCount = _items.Count(i => i.Step != null);
        _cycles = singlePass ? 1 : settings.EffectiveCycles;
        _cycleDelayMs = settings.CycleDelayMs;
        _startDelaySec = singlePass ? 0 : settings.StartDelaySec;
    }

    public int StepCount => _stepCount;

    private void Flatten(MacroNode node)
    {
        if (!node.Enabled) return;
        if (node is StepNode s) { _items.Add(new Item(s, null)); return; }
        if (node is GroupNode g)
        {
            foreach (var c in g.Children) Flatten(c);
            if (!g.IsRoot) _items.Add(new Item(null, g));
        }
    }

    public void Run(CancellationToken ct)
    {
        if (_stepCount == 0)
        {
            Log?.Invoke(Loc.T("Run_NoSteps"));
            return;
        }

        for (int s = _startDelaySec; s > 0; s--)
        {
            Progress?.Invoke(new RunProgress(0, _cycles, 0, _stepCount, null, Loc.F("Run_StartingIn", s)));
            Wait(1000, ct);
        }

        Log?.Invoke(Loc.T("Run_Started"));
        for (int cycle = 1; _cycles == 0 || cycle <= _cycles; cycle++)
        {
            Log?.Invoke(Loc.F("Run_CycleStarted", _cycles == 0 ? $"{cycle}" : $"{cycle} / {_cycles}"));
            int index = 0;
            foreach (var item in _items)
            {
                ct.ThrowIfCancellationRequested();
                if (item.EndOfGroup is { } g)
                {
                    Log?.Invoke(Loc.F("Run_GroupDone", g.Name));
                    continue;
                }

                var step = item.Step!;
                index++;
                Progress?.Invoke(new RunProgress(cycle, _cycles, index, _stepCount, step, Describe(step)));
                Execute(step, ct);
                if (step is not WaitStep && step.DelayAfterMs > 0) Wait(step.DelayAfterMs, ct);
            }

            Log?.Invoke(Loc.F("Run_CycleDone", cycle));
            CycleCompleted?.Invoke(cycle);

            bool last = _cycles != 0 && cycle >= _cycles;
            if (!last && _cycleDelayMs > 0)
            {
                Progress?.Invoke(new RunProgress(cycle, _cycles, _stepCount, _stepCount, null,
                    Loc.F("Run_CyclePause", MacroNode.FormatMs(_cycleDelayMs))));
                Wait(_cycleDelayMs, ct);
            }
        }
    }

    private static string Describe(StepNode step) =>
        string.IsNullOrWhiteSpace(step.Name) ? step.ActionText : $"{step.Name} · {step.ActionText}";

    private void Execute(StepNode step, CancellationToken ct)
    {
        switch (step)
        {
            case ClickStep c:
                InputSender.MoveTo(c.X, c.Y);
                Thread.Sleep(30);
                InputSender.Click(c.Button, c.Clicks);
                break;

            case DragStep d:
                Drag(d, ct);
                break;

            case TextStep t:
                if (t.ClickFirst)
                {
                    InputSender.MoveTo(t.X, t.Y);
                    Thread.Sleep(30);
                    InputSender.Click(MouseButtonKind.Left, 1);
                    Wait(120, ct);
                }
                if (t.ClearFirst)
                {
                    InputSender.KeyCombo(InputSender.VK_A, ctrl: true, shift: false, alt: false, win: false);
                    Thread.Sleep(40);
                    InputSender.KeyPress(InputSender.VK_DELETE);
                    Thread.Sleep(40);
                }
                var text = TextGenerator.ForStep(t);
                if (t.Mode == TextMode.Random) Log?.Invoke(Loc.F("Run_Generated", text));
                InputSender.TypeText(text, t.CharDelayMs, ct);
                if (t.PressEnter)
                {
                    Thread.Sleep(40);
                    InputSender.KeyPress(InputSender.VK_RETURN);
                }
                break;

            case WaitStep w:
                int ms = w.UseRandom
                    ? Random.Shared.Next(Math.Min(w.Ms, w.MaxMs), Math.Max(w.Ms, w.MaxMs) + 1)
                    : w.Ms;
                Wait(ms, ct);
                break;

            case KeyStep k:
                for (int i = 0; i < k.Repeat; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    InputSender.KeyCombo((ushort)k.Vk, k.Ctrl, k.Shift, k.Alt, k.Win);
                    if (i < k.Repeat - 1) Thread.Sleep(40);
                }
                break;
        }
    }

    private static void Drag(DragStep d, CancellationToken ct)
    {
        InputSender.MoveTo(d.X, d.Y);
        Thread.Sleep(40);
        InputSender.ButtonDown(d.Button);
        try
        {
            int steps = Math.Max(8, d.DurationMs / 15);
            int pause = Math.Max(1, d.DurationMs / steps);
            for (int i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                double t = (double)i / steps;
                InputSender.MoveTo((int)Math.Round(d.X + (d.X2 - d.X) * t), (int)Math.Round(d.Y + (d.Y2 - d.Y) * t));
                Thread.Sleep(pause);
            }
            Thread.Sleep(40);
        }
        finally
        {
            InputSender.ButtonUp(d.Button);
        }
    }

    private static void Wait(int ms, CancellationToken ct)
    {
        if (ms <= 0) return;
        if (ct.WaitHandle.WaitOne(ms)) throw new OperationCanceledException(ct);
    }

    /// <summary>Procjena trajanja jednog ciklusa (za statistiku).</summary>
    public static TimeSpan EstimateCycle(GroupNode root)
    {
        double ms = 0;
        foreach (var s in root.AllSteps(onlyEnabled: true))
        {
            ms += s switch
            {
                ClickStep c => 60 + c.Clicks * 80,
                DragStep d => 130 + d.DurationMs,
                TextStep t => (t.ClickFirst ? 200 : 0) + (t.ClearFirst ? 120 : 0) + (t.PressEnter ? 60 : 0)
                              + (t.CharDelayMs + 2) * (t.Mode == TextMode.Fixed
                                  ? t.Text.Length
                                  : t.Prefix.Length + t.Suffix.Length + (t.LowLength + t.HighLength) / 2.0),
                WaitStep w => w.UseRandom ? (w.Ms + w.MaxMs) / 2.0 : w.Ms,
                KeyStep k => k.Repeat * 70,
                _ => 0,
            };
            if (s is not WaitStep) ms += s.DelayAfterMs;
        }
        return TimeSpan.FromMilliseconds(ms);
    }
}
