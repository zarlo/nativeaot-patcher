using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Bridge;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Timer;
using SysThread = System.Threading.Thread;
using SchedThread = Cosmos.Kernel.Core.Scheduler.Thread;
#if ARCH_X64
using Cosmos.Kernel.Core.X64.Cpu;
#endif

namespace Cosmos.Kernel.Plugs.System.Threading;

[Plug(typeof(SysThread))]
public static unsafe class ThreadPlug
{
    // Pending ThreadStart delegates queued between Ctor and StartCore.
    // Per-thread entry storage lives in SchedulerManager (see RegisterThreadStart).
    private static readonly Queue<ThreadStart> _pendingDelegates = new();

    [PlugMember(".ctor")]
    public static void Ctor(SysThread aThis, ThreadStart start)
    {
        Serial.WriteString("[ThreadPlug] Ctor(ThreadStart)\n");

        // Only disable interrupts if scheduler is running (to avoid issues during early boot)
        if (SchedulerManager.Enabled)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                _pendingDelegates.Enqueue(start);
            }
        }
    }

    [PlugMember(".ctor")]
    public static void Ctor(SysThread aThis, ThreadStart start, int maxStackSize)
    {
        Serial.WriteString("[ThreadPlug] Ctor(ThreadStart, maxStackSize)\n");

        // Only disable interrupts if scheduler is running (to avoid issues during early boot)
        if (SchedulerManager.Enabled)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                _pendingDelegates.Enqueue(start);
            }
        }
    }

    [PlugMember("StartCore")]
    public static void StartCore(SysThread aThis)
    {
        Serial.WriteString("[ThreadPlug] StartCore()\n");

        if (SchedulerManager.Enabled)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                if (_pendingDelegates.Count == 0)
                {
                    Serial.WriteString("[ThreadPlug] No delegate found\n");
                    return;
                }

                ThreadStart start = _pendingDelegates.Dequeue();

                // Create scheduler thread with its entry delegate attached.
                // SchedulerManager.InvokeCurrentThreadStart reads it back off
                // the thread when it first runs.
                SchedThread thread = new SchedThread
                {
                    Id = SchedulerManager.AllocateThreadId(),
                    CpuId = 0,
                    State = Cosmos.Kernel.Core.Scheduler.ThreadState.Created,
                    StartDelegate = start.Invoke
                };

                Serial.WriteString("[ThreadPlug] Thread ");
                Serial.WriteNumber(thread.Id);
                Serial.WriteString(" - setting up stack\n");

                // Initial RIP/PC is the stable native entry-point stub in Core;
                // the scheduler's InvokeCurrentThreadStart runs the registered delegate.
                nuint entryPoint = (nuint)(delegate* unmanaged<void>)&ThreadNative.EntryPointStub;
#if ARCH_X64
                ushort cs = (ushort)Idt.GetCurrentCodeSelector();
                thread.InitializeStack(entryPoint, cs, thread.Id);
#elif ARCH_ARM64
                // ARM64: no code selector needed, use 0.
                thread.InitializeStack(entryPoint, 0, thread.Id);
#endif
                Serial.WriteString("[ThreadPlug] Stack initialized, registering with scheduler\n");

                SchedulerManager.CreateThread(0, thread);
                SchedulerManager.ReadyThread(0, thread);

                Serial.WriteString("[ThreadPlug] Thread ");
                Serial.WriteNumber(thread.Id);
                Serial.WriteString(" scheduled for execution\n");
            }
        }
    }

    [PlugMember]
    public static void Sleep(int millisecondsTimeout)
    {
        if (millisecondsTimeout > 0)
        {
            TimerManager.Wait((uint)millisecondsTimeout);
        }
    }

    [PlugMember]
    public static void Sleep(TimeSpan timeout)
    {
        Sleep((int)timeout.TotalMilliseconds);
    }

    [PlugMember]
    public static bool Yield() => true;

    [PlugMember]
    public static void SpinWait(int iterations)
    {
        for (int i = 0; i < iterations; i++) { }
    }
}
