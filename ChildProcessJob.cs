using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IaTerminal;

/// <summary>
/// Job Object de Windows con JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE. Al asociar
/// llama-server.exe a este job, el kernel lo mata SIEMPRE que muera portable.exe
/// (cierre de consola con la X, OutOfMemory, taskkill, corte de luz). El
/// DisposeAsync de la sesión ya lo mataba, pero eso solo corre si el proceso
/// desenrolla el await using: al morir abruptamente el hijo quedaba huérfano con
/// ~7 GB de RAM y la sesión siguiente no podía cargar el modelo (PARTE 8).
/// El handle se mantiene abierto toda la vida de la sesión a propósito: si se
/// cerrara antes, el job mataría al hijo. Si Assign falla (por ejemplo si el
/// proceso ya está en un job que no admite anidamiento) devuelve false y la
/// sesión sigue con la limpieza por ProcessExit.
/// </summary>
internal sealed class ChildProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private IntPtr handle;

    public ChildProcessJob(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        handle = CreateJobObject(IntPtr.Zero, name);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = new ExtendedLimitInformation
        {
            BasicLimitInformation = new BasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var length = Marshal.SizeOf<ExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)length))
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }
        }
        catch (Exception)
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public bool Assign(Process process)
    {
        if (handle == IntPtr.Zero || process is null)
        {
            return false;
        }

        try
        {
            return process.HasExited || AssignProcessToJobObject(handle, process.Handle);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }
}
