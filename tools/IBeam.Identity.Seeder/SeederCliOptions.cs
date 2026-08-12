namespace IBeam.Identity.Seeder;

internal sealed record SeederCliOptions
{
    public string ConfigPath { get; private init; } = Path.Combine("scripts", "identity", "identity.seed.sample.json");
    public string? ContentRoot { get; private init; }
    public string Environment { get; private init; } = "Development";
    public string? ReportPath { get; private init; }
    public bool Apply { get; private init; }
    public bool EnsureSchema { get; private init; } = true;
    public bool Verbose { get; private init; }
    public bool ShowHelp { get; private init; }

    public static SeederCliOptions Parse(string[] args)
    {
        var options = new SeederCliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                case "/?":
                    options = options with { ShowHelp = true };
                    break;
                case "--config":
                    options = options with { ConfigPath = RequireValue(args, ref i, arg) };
                    break;
                case "--content-root":
                    options = options with { ContentRoot = RequireValue(args, ref i, arg) };
                    break;
                case "--environment":
                    options = options with { Environment = RequireValue(args, ref i, arg) };
                    break;
                case "--report":
                    options = options with { ReportPath = RequireValue(args, ref i, arg) };
                    break;
                case "--apply":
                    options = options with { Apply = true };
                    break;
                case "--dry-run":
                    options = options with { Apply = false };
                    break;
                case "--skip-schema":
                    options = options with { EnsureSchema = false };
                    break;
                case "--verbose":
                    options = options with { Verbose = true };
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'. Pass --help for usage.");
            }
        }

        return options;
    }

    public static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("""
        IBeam Identity local/demo seeder

        Usage:
          dotnet run --project tools/IBeam.Identity.Seeder -- --config scripts/identity/identity.seed.local.json --apply

        Options:
          --config <path>        Seed config JSON path.
          --content-root <path>  Repo/content root. Defaults to current directory.
          --environment <name>   Appsettings environment. Defaults to Development.
          --report <path>        Optional JSON report output path.
          --apply                Write changes. Omit for dry-run.
          --dry-run              Force dry-run.
          --skip-schema          Do not run identity schema apply.
          --verbose              Enable debug logging.
        """);
    }

    private static string RequireValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{name} requires a value.");
        }

        index++;
        return args[index];
    }
}
