using Spectre.Console;

namespace IaTerminal;

public static class Banner
{
    private static readonly string[] Logo =
    {
        "               ####               ",
        "      ##       ####       ##      ",
        "       ####    #####    ####      ",
        "         #### ##### ####         ",
        "           ##########            ",
        "  ##        ##########        ##  ",
        "    ####   ###########   ####    ",
        "      ###################        ",
        " ################################ ",
        "      ###################        ",
        "    ####   ###########   ####    ",
        "  ##        ##########        ##  ",
        "           ##########            ",
        "         #### ##### ####         ",
        "       ####    #####    ####     ",
        "      ##       ####       ##     ",
        "               ####              "
    };

    public static void Render(AppConfig config, string modelName)
    {
        var art = string.Join("\n", Logo);

        var logoPanel = new Panel(
            Align.Center(new Markup($"[blue]{Markup.Escape(art)}[/]")))
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan1),
            Header = new PanelHeader("[bold cyan]IR3C5.CORE[/]"),
            Padding = new Padding(2, 0, 2, 0),
        };
        AnsiConsole.Write(logoPanel);

        AnsiConsole.MarkupLine("[cyan]  INTRA-NET :: [/][bold]IA27 TERMINAL[/][cyan] :: sistema local // canal seguro[/]");
        AnsiConsole.WriteLine();

        var estado = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Title("[bold cyan]Estado[/]")
            .AddColumn(new TableColumn("[grey]Campo[/]").LeftAligned())
            .AddColumn(new TableColumn("[grey]Valor[/]").LeftAligned());

        estado.AddRow("[bold]Modelo[/]", Markup.Escape(modelName));
        estado.AddRow("[bold]Origen[/]", Markup.Escape(Shorten(config.ModelDirectory, 55)));
        estado.AddRow("[bold]Motor[/]", "[green]llama.cpp local[/]");
        estado.AddRow(
            "[bold]Red[/]",
            config.NetEnabled
                ? "[green]● ON[/] [grey](bajo autorización)[/]"
                : "[red]○ OFF[/]");
        estado.AddRow(
            "[bold]Contexto[/]",
            $"[yellow]{config.ContextSize}[/] tokens · [yellow]{config.Threads}[/] hilos · [yellow]{config.GpuLayers}[/] capas GPU");
        AnsiConsole.Write(estado);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Comandos rápidos[/]");
        var comandos = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("cmd").LeftAligned())
            .AddColumn(new TableColumn("desc").LeftAligned());

        void AddCommand(string cmd, string desc)
        {
            comandos.AddRow($"[cyan]{Markup.Escape(cmd)}[/]", $"[white]{Markup.Escape(desc)}[/]");
        }

        AddCommand("/help", "ver comandos");
        AddCommand("/clear", "limpiar historial");
        AddCommand("/use <modelo>", "cambiar modelo");
        AddCommand("/net on|off", "activar/desactivar red");
        AddCommand("/harness <objetivo>", "modo agéntico por pasos");
        AddCommand("/exit", "salir");
        AnsiConsole.Write(comandos);

        AnsiConsole.Write(new Rule().RuleStyle(Color.DarkBlue));
        AnsiConsole.MarkupLine("[grey]Listo. Escribe /help para ver los comandos. Ctrl+C detiene la respuesta en curso.[/]");
        AnsiConsole.WriteLine();
    }

    private static string Shorten(string path, int max)
        => string.IsNullOrEmpty(path) || path.Length <= max ? path : "…" + path[^(max - 1)..];
}
