using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;
using Stopwatch = global::System.Diagnostics.Stopwatch;
using SysThread = global::System.Threading.Thread;

namespace DevKernel;

/// <summary>
/// Live CPU utilization + multithreading visualizer.
/// Spawns a controller thread that ramps CPU-bound workers up/down in a
/// triangular wave; renders CPU%, history graph, target/live counts, and the
/// scheduler's thread registry. ESC to exit.
/// </summary>
internal static class CpuStat
{
    private const int MaxStressThreads = 8;
    // Sized for any common framebuffer width. Actual sample count is set at
    // Run() time to (canvas.Width - 20) so each pixel column is one sample.
    private const int MaxHistory = 1920;

    private static volatile bool s_stop;
    private static volatile int s_target;
    private static volatile int s_live;
    private static volatile int s_dropRequest;
    private static volatile int s_direction = 1;

    private static readonly int[] s_cpuHistory = new int[MaxHistory];
    private static readonly int[] s_threadHistory = new int[MaxHistory];
    private static int s_historyIdx;
    private static int s_historyLen;
    private static int s_historyFilled;

    public static void Run()
    {
        Canvas canvas = Canvas.GetFullScreen();
        PCScreenFont font = PCScreenFont.DefaultFont;

        Array.Clear(s_cpuHistory, 0, s_cpuHistory.Length);
        Array.Clear(s_threadHistory, 0, s_threadHistory.Length);
        s_historyIdx = 0;
        s_historyFilled = 0;
        s_historyLen = (int)canvas.Mode.Width - 20;
        if (s_historyLen < 64)
        {
            s_historyLen = 64;
        }
        if (s_historyLen > MaxHistory)
        {
            s_historyLen = MaxHistory;
        }
        s_stop = false;
        s_target = 0;
        s_live = 0;
        s_dropRequest = 0;
        s_direction = 1;

        // Inline controller state — no separate controller thread needed.
        // We adjust target every ControlIntervalMs from inside the render loop;
        // that removes the controller↔renderer scheduling race that was
        // freezing the visualization.
        const int ControlIntervalMs = 500;
        int target = 0;
        ulong lastControlMs = 0;

        ulong lastWall = 0;
        ulong lastBusy = 0;
        int currentCpuPct = 0;
        int peakCpuPct = 0;
        ulong sampleFreq = (ulong)Stopwatch.Frequency;
        if (sampleFreq == 0)
        {
            sampleFreq = 1_000_000_000UL;
        }

        int width = (int)canvas.Mode.Width;
        int height = (int)canvas.Mode.Height;
        int lineHeight = font.Height + 2;

        // Adaptive layout knobs for narrow / short framebuffers (ARM64 QEMU
        // virt sometimes hands us a very small mode).
        bool narrow = width < 600;
        int graphH = height < 500 ? 90 : height < 700 ? 130 : 180;

        while (!Console.KeyAvailable || Console.ReadKey(true).Key != ConsoleKey.Escape)
        {
            ulong wallNow = (ulong)Stopwatch.GetTimestamp();
            ulong busyNow = SchedulerManager.GetBusyCpuTimeNs();

            // === Controller (inline) ===
            // Triangular wave 0 → MaxStressThreads → 0; one step per ControlIntervalMs.
            ulong nowMs = (ulong)((double)wallNow * 1000.0 / (double)sampleFreq);
            if (nowMs - lastControlMs >= (ulong)ControlIntervalMs)
            {
                lastControlMs = nowMs;
                int prevTarget = target;
                if (s_direction > 0)
                {
                    if (target < MaxStressThreads)
                    {
                        target++;
                    }
                    else
                    {
                        s_direction = -1;
                        target--;
                    }
                }
                else
                {
                    if (target > 0)
                    {
                        target--;
                    }
                    else
                    {
                        s_direction = 1;
                        target++;
                    }
                }
                s_target = target;

                if (target != prevTarget)
                {
                    Cosmos.Kernel.Core.IO.Serial.WriteString("[CpuStat] target=");
                    Cosmos.Kernel.Core.IO.Serial.WriteNumber((uint)target);
                    Cosmos.Kernel.Core.IO.Serial.WriteString(" live=");
                    Cosmos.Kernel.Core.IO.Serial.WriteNumber((uint)s_live);
                    Cosmos.Kernel.Core.IO.Serial.WriteString("\n");
                }

                // One spawn or one drop per control tick.
                int effectiveNow = s_live - s_dropRequest;
                if (effectiveNow < target)
                {
                    Interlocked.Increment(ref s_live);
                    try
                    {
                        SysThread w = new SysThread(StressWorker);
                        w.Start();
                    }
                    catch (Exception spawnEx)
                    {
                        Interlocked.Decrement(ref s_live);
                        Cosmos.Kernel.Core.IO.Serial.WriteString("[CpuStat] spawn FAILED: ");
                        Cosmos.Kernel.Core.IO.Serial.WriteString(spawnEx.Message ?? "?");
                        Cosmos.Kernel.Core.IO.Serial.WriteString("\n");
                    }
                }
                else if (effectiveNow > target)
                {
                    Interlocked.Increment(ref s_dropRequest);
                }
            }

            if (lastWall != 0 && wallNow > lastWall)
            {
                ulong wallDelta = wallNow - lastWall;
                ulong busyDelta = busyNow >= lastBusy ? busyNow - lastBusy : 0;

                // pct = busyNs * freq / (wallTicks * 1e9 * cpuCount) * 100
                // — done in double to avoid overflow on busyNs * freq.
                double wallNs = (double)wallDelta * 1_000_000_000.0 / (double)sampleFreq;
                double total = wallNs * (double)SchedulerManager.CpuCount;
                if (total > 0.0)
                {
                    double pct = (double)busyDelta * 100.0 / total;
                    if (pct < 0.0)
                    {
                        pct = 0.0;
                    }
                    if (pct > 100.0)
                    {
                        pct = 100.0;
                    }
                    currentCpuPct = (int)pct;
                }
            }
            lastWall = wallNow;
            lastBusy = busyNow;

            if (currentCpuPct > peakCpuPct)
            {
                peakCpuPct = currentCpuPct;
            }

            s_cpuHistory[s_historyIdx] = currentCpuPct;
            s_threadHistory[s_historyIdx] = s_live - s_dropRequest;
            s_historyIdx = (s_historyIdx + 1) % s_historyLen;
            if (s_historyFilled < s_historyLen)
            {
                s_historyFilled++;
            }

            // Explicit full-canvas wipe — canvas.Clear's ClearScreen path can
            // leave stale pixels on ARM64, which produced ghost graph lines on
            // the left edge of the framebuffer. DrawFilledRectangle goes
            // through the per-pixel write path the rest of the drawing uses,
            // so it stays consistent.
            canvas.DrawFilledRectangle(Color.Black, 0, 0, width, height);

            int rowY = 8;
            string header = narrow ? "CPU Monitor — ESC" : "CPU Utilization Monitor — ESC to exit";
            DrawClipped(canvas, font, Color.Cyan, 10, rowY, width - 20, header);
            rowY += lineHeight + 4;

            // Current % + peak. On narrow screens the Peak label is positioned
            // dynamically right after CPU% rather than at a fixed x=200.
            Color pctColor = currentCpuPct < 50 ? Color.LimeGreen
                            : currentCpuPct < 80 ? Color.Yellow
                            : Color.OrangeRed;
            string cpuLabel = "CPU: " + currentCpuPct + "%";
            string peakLabel = "Peak: " + peakCpuPct + "%";
            DrawClipped(canvas, font, pctColor, 10, rowY, width - 20, cpuLabel);
            int peakX = 10 + font.Width * (cpuLabel.Length + 3);
            int peakRoom = width - 10 - peakX;
            if (peakRoom >= peakLabel.Length * font.Width)
            {
                canvas.DrawString(peakLabel, font, Color.Gray, peakX, rowY);
            }
            rowY += lineHeight + 2;

            // Horizontal bar (full width minus margins)
            int barW = width - 40;
            int barH = 14;
            int barX = 10;
            int barY = rowY;
            canvas.DrawRectangle(Color.DarkSlateGray, barX, barY, barW, barH);
            int filledW = barW * currentCpuPct / 100;
            if (filledW > 0)
            {
                canvas.DrawFilledRectangle(pctColor, barX + 1, barY + 1,
                                           filledW - 1, barH - 1);
            }
            rowY += barH + 8;

            // Stress controller stats — short form on narrow screens.
            int effective = s_live - s_dropRequest;
            string statsLine = narrow
                ? "t=" + s_target + " live=" + s_live + " drop=" + s_dropRequest +
                  " " + (s_direction > 0 ? "+" : "-")
                : "Stress  target=" + s_target +
                  "  live=" + s_live +
                  "  drop=" + s_dropRequest +
                  "  effective=" + effective +
                  "  dir=" + (s_direction > 0 ? "+" : "-");
            DrawClipped(canvas, font, Color.White, 10, rowY, width - 20, statsLine);
            rowY += lineHeight + 4;

            // Full-width history graph — height already chosen for the screen.
            int graphX = 10;
            int graphY = rowY;
            int graphW = s_historyLen;
            // Clamp graphW to what actually fits on the canvas — protects against
            // any edge case where s_historyLen could disagree with canvas width.
            if (graphX + graphW > width - 4)
            {
                graphW = width - 4 - graphX;
            }
            // Explicitly wipe the graph region — canvas.Clear leaves stale pixels
            // on some ARM64 framebuffers, which produced "ghost" lines on the
            // left side of the graph from the previous frame.
            canvas.DrawFilledRectangle(Color.Black, graphX, graphY, graphW, graphH);
            canvas.DrawRectangle(Color.DimGray, graphX, graphY, graphW, graphH);

            // 25 / 50 / 75 % gridlines
            for (int g = 1; g < 4; g++)
            {
                int gy = graphY + graphH - (g * 25 * graphH / 100);
                canvas.DrawLine(Color.FromArgb(40, 40, 40),
                                graphX + 1, gy, graphX + graphW - 1, gy);
            }

            // CPU% line (green) + effective stress thread count (cyan, scaled to
            // MaxStressThreads). Always stretch the populated portion of the
            // buffer across the full graph width: when only a few samples have
            // accumulated they spread edge-to-edge, and once the buffer is full
            // we get exactly one sample per pixel column.
            int count = s_historyFilled;
            if (count >= 2)
            {
                int denom = count - 1;
                int prevCpuY = -1;
                int prevThreadY = -1;
                int prevPx = graphX;
                for (int i = 0; i < count; i++)
                {
                    int idx = (s_historyIdx - count + i + s_historyLen) % s_historyLen;
                    int cpu = s_cpuHistory[idx];
                    int th = s_threadHistory[idx];
                    if (th < 0)
                    {
                        th = 0;
                    }
                    if (th > MaxStressThreads)
                    {
                        th = MaxStressThreads;
                    }
                    int cpuY = graphY + graphH - (cpu * graphH / 100);
                    int thY = graphY + graphH - (th * graphH / MaxStressThreads);
                    if (thY < graphY)
                    {
                        thY = graphY;
                    }
                    int px = graphX + i * (graphW - 1) / denom;
                    if (i > 0)
                    {
                        canvas.DrawLine(Color.LimeGreen, prevPx, prevCpuY, px, cpuY);
                        canvas.DrawLine(Color.DeepSkyBlue, prevPx, prevThreadY, px, thY);
                    }
                    prevCpuY = cpuY;
                    prevThreadY = thY;
                    prevPx = px;
                }
            }
            rowY = graphY + graphH + 6;
            // ~10 Hz sampling → window in seconds equals s_historyLen / 10.
            // Skip later legend items if they'd overflow the screen width.
            int legendX = graphX;
            int swatchW = font.Width * 2;
            int swatchH = font.Height - 2;
            int swatchY = rowY + 1;
            int legendEdge = width - 4;

            string cpuLegend = narrow ? "CPU%" : "CPU %";
            if (legendX + swatchW + 6 + cpuLegend.Length * font.Width <= legendEdge)
            {
                canvas.DrawFilledRectangle(Color.LimeGreen, legendX, swatchY, swatchW, swatchH);
                legendX += swatchW + 6;
                canvas.DrawString(cpuLegend, font, Color.LimeGreen, legendX, rowY);
                legendX += font.Width * (cpuLegend.Length + 2);
            }

            string threadsLabel = narrow ? "threads" : "stress threads (0.." + MaxStressThreads + ")";
            if (legendX + swatchW + 6 + threadsLabel.Length * font.Width <= legendEdge)
            {
                canvas.DrawFilledRectangle(Color.DeepSkyBlue, legendX, swatchY, swatchW, swatchH);
                legendX += swatchW + 6;
                canvas.DrawString(threadsLabel, font, Color.DeepSkyBlue, legendX, rowY);
                legendX += font.Width * (threadsLabel.Length + 4);
            }

            string windowLabel = "window " + (s_historyLen / 10) + "s";
            if (legendX + windowLabel.Length * font.Width <= legendEdge)
            {
                canvas.DrawString(windowLabel, font, Color.Gray, legendX, rowY);
            }
            rowY += lineHeight + 6;

            // Scheduler thread registry — only render if there's room left below
            // the graph. On a very short screen this section is just dropped.
            if (rowY + lineHeight * 2 > height)
            {
                canvas.Display();
                SysThread.Sleep(100);
                continue;
            }
            DrawClipped(canvas, font, Color.Cyan, 10, rowY, width - 20,
                        "Scheduler threads (" + SchedulerManager.ThreadCount + " live):");
            rowY += lineHeight;

            SchedThread?[]? threads = SchedulerManager.Threads;
            if (threads != null)
            {
                // Fixed-width line: "Tnnn flag STA  rrrrr"  → 21 chars max.
                // Column pitch = (lineChars + 2 gutter) × font.Width — guarantees
                // no overlap regardless of glyph width.
                const int LineChars = 21;
                int colPitch = font.Width * (LineChars + 2);
                int colCount = (width - 20) / colPitch;
                if (colCount < 1)
                {
                    colCount = 1;
                }
                int maxRows = (height - rowY - 20) / lineHeight;
                if (maxRows < 1)
                {
                    maxRows = 1;
                }
                int maxSlots = colCount * maxRows;

                // Row-major: threads spread across the screen width first, then
                // wrap to a new row. Keeps the right side populated even when
                // the registry is small.
                int slot = 0;
                for (int i = 0; i < threads.Length && slot < maxSlots; i++)
                {
                    SchedThread? t = threads[i];
                    if (t == null)
                    {
                        continue;
                    }

                    string flag = (t.Flags & ThreadFlags.IdleThread) != 0 ? "idle"
                                : (t.Flags & ThreadFlags.Managed) != 0 ? "mgd "
                                : "krn ";
                    string runStr = FormatRuntime(t.TotalRuntime);
                    string line = "T" + t.Id.ToString().PadLeft(3) +
                                  " " + flag +
                                  " " + StateLabel(t.State) +
                                  " " + runStr.PadLeft(6);
                    if (line.Length > LineChars)
                    {
                        line = line.Substring(0, LineChars);
                    }
                    Color c = (t.Flags & ThreadFlags.IdleThread) != 0
                            ? Color.DarkGray
                            : t.State == Cosmos.Kernel.Core.Scheduler.ThreadState.Running
                                ? Color.LimeGreen
                                : Color.LightGray;
                    int col = slot % colCount;
                    int row = slot / colCount;
                    int xPos = 10 + col * colPitch;
                    int yPos = rowY + row * lineHeight;
                    canvas.DrawString(line, font, c, xPos, yPos);
                    slot++;
                }
            }

            canvas.Display();
            SysThread.Sleep(100);
        }

        // Tear down workers and controller
        s_stop = true;
        s_target = 0;
        // Wait up to ~2s for workers to drain.
        for (int i = 0; i < 40 && s_live > 0; i++)
        {
            SysThread.Sleep(50);
        }

        Console.Clear();
    }

    private static void DrawClipped(Canvas canvas, PCScreenFont font, Color color,
                                    int x, int y, int maxWidthPx, string text)
    {
        if (maxWidthPx <= 0 || font.Width <= 0)
        {
            return;
        }
        int maxChars = maxWidthPx / font.Width;
        if (maxChars <= 0)
        {
            return;
        }
        if (text.Length > maxChars)
        {
            text = text.Substring(0, maxChars);
        }
        canvas.DrawString(text, font, color, x, y);
    }

    private static string FormatRuntime(ulong totalRuntimeNs)
    {
        // Compact runtime so the per-thread line stays bounded:
        //  < 10s  → "X.Xs"
        //  < 1m   → "Xs"
        //  < 1h   → "Xm"
        //  ≥ 1h   → "Xh"
        ulong ms = totalRuntimeNs / 1_000_000UL;
        if (ms < 10_000UL)
        {
            return (ms / 1000UL) + "." + ((ms / 100UL) % 10UL) + "s";
        }
        if (ms < 60_000UL)
        {
            return (ms / 1000UL) + "s";
        }
        if (ms < 3_600_000UL)
        {
            return (ms / 60_000UL) + "m";
        }
        return (ms / 3_600_000UL) + "h";
    }

    private static string StateLabel(Cosmos.Kernel.Core.Scheduler.ThreadState state)
    {
        switch (state)
        {
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Running: return "RUN";
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Ready:   return "RDY";
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Blocked: return "BLK";
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Sleeping:return "SLP";
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Dead:    return "DED";
            case Cosmos.Kernel.Core.Scheduler.ThreadState.Created: return "NEW";
            default: return "???";
        }
    }

    private static void StressWorker()
    {
        // Duty cycle: per-worker load ≈ BurnMs / (BurnMs + SleepMs). 30/400 ≈ 7 %,
        // so MaxStressThreads (8) workers reach ~56 % CPU at peak.
        // Long single Sleep (no chunking) keeps the kernel's per-wake scheduler
        // logging well under UART throughput at 115200 baud — that throughput
        // limit is what was causing the renderer to freeze.
        const int BurnMs = 30;
        const int SleepMs = 400;

        int dummy = 0;
        ulong freq = (ulong)Stopwatch.Frequency;
        if (freq == 0)
        {
            freq = 1_000_000_000UL;
        }
        ulong burnTicks = freq * (ulong)BurnMs / 1000UL;

        while (!s_stop)
        {
            // Try to claim a drop slot.
            if (s_dropRequest > 0)
            {
                int after = Interlocked.Decrement(ref s_dropRequest);
                if (after >= 0)
                {
                    break;
                }
                Interlocked.Increment(ref s_dropRequest);
            }

            // Burn CPU for ~BurnMs.
            ulong start = (ulong)Stopwatch.GetTimestamp();
            ulong end = start + burnTicks;
            while ((ulong)Stopwatch.GetTimestamp() < end)
            {
                for (int j = 0; j < 256; j++)
                {
                    dummy = unchecked(dummy + j);
                }
            }

            // One single Sleep — drop response time is up to SleepMs, but we
            // generate far fewer scheduler/UART events so the kernel can
            // actually keep up.
            SysThread.Sleep(SleepMs);
        }

        // Sink to keep dummy live so the JIT can't elide the loop body.
        if (dummy == int.MinValue)
        {
            Cosmos.Kernel.Core.IO.Serial.WriteString("");
        }
        Interlocked.Decrement(ref s_live);
    }
}
