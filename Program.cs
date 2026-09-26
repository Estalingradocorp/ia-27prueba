using System.Text;

namespace IaTerminal;

public static class Program
{
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
