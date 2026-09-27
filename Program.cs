using System.Runtime.InteropServices;
using System.Text;

namespace IaTerminal;

public static class Program
{
    private const int WmSetIcon = 0x0110;
    private const int IconSmall = 0;
    private const int IconBig = 1;

    private static readonly List<IntPtr> IconHandles = new();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    public static async Task<int> Main(string[] args)
    {
        var previousTitle = string.Empty;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                previousTitle = Console.Title;
            }
            catch (IOException)
            {
            }
        }

        ConfigureStyle();
        ApplyWindowIcon();
        try
        {
            return await new TerminalApplication().RunAsync(args);
        }
        catch (TerminalException error)
        {
            Console.Error.WriteLine($"Error: {error.Message}");
            Console.Error.WriteLine("Usa 'portable.exe help' para ver los comandos.");
            return 2;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error inesperado: {error.Message}");
            return 1;
        }
        finally
        {
            try
            {
                Console.ResetColor();
                if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(previousTitle))
                {
                    Console.Title = previousTitle;
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static void ApplyWindowIcon()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var window = GetConsoleWindow();
            if (window == IntPtr.Zero)
            {
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return;
            }

            var large = new IntPtr[1];
            var small = new IntPtr[1];
            if (ExtractIconEx(executable, 0, large, small, 1) == 0)
            {
                return;
            }

            if (small[0] != IntPtr.Zero)
            {
                SendMessage(window, WmSetIcon, new IntPtr(IconSmall), small[0]);
                IconHandles.Add(small[0]);
            }

            if (large[0] != IntPtr.Zero)
            {
                SendMessage(window, WmSetIcon, new IntPtr(IconBig), large[0]);
                IconHandles.Add(large[0]);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void ConfigureStyle()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            if (OperatingSystem.IsWindows())
            {
                Console.Title = "Estalingrado Corp · Intra-net";
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.Cyan;
            }
        }
        catch (IOException)
        {
        }
    }
}
