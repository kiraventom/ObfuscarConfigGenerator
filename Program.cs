internal class Program
{
    private const string HELP_ARG = "--help";
    private const string CONFIG_FILENAME = "obfuscar_config.xml";

    private static string ArgToAlias(string arg) => arg[1..3];

    private static Args ParseArgs(string[] args)
    {
        const string EXEC_PROJECT_ARG = "--project";
        const string INPUT_ARG = "--input";
        const string OUTPUT_ARG = "--output";
        const string IGNORE_ARG = "--ignore";

        const string INPUT_ARG_DEFAULT = "./";
        const string OUTPUT_ARG_DEFAULT = "Obfuscated/";

        if (args.Any(a => a == HELP_ARG|| a == ArgToAlias(HELP_ARG)))
        {
            Console.WriteLine($@"
Required arguments:
    {EXEC_PROJECT_ARG}, {ArgToAlias(EXEC_PROJECT_ARG)}: Path to executable .csproj file.

Optional arguments:
    {INPUT_ARG}, {ArgToAlias(INPUT_ARG)}: Obfuscar input directory. 
        Default value: ""{INPUT_ARG_DEFAULT}"".

    {OUTPUT_ARG}, {ArgToAlias(OUTPUT_ARG)}: Obfuscar output directory. 
        Default value: ""<input>/{OUTPUT_ARG_DEFAULT}"".

    {IGNORE_ARG}: Comma-separated list of modules to ignore.
        E.g., ""{IGNORE_ARG} Foo,Bar,Baz"" will exclude Foo.dll, Bar.dll and Baz.dll from list of modules.
        Default value: empty string.

    {HELP_ARG}, {ArgToAlias(HELP_ARG)}: displays this help.
");
            return null;
        }

        var execProjectFile = ParseArg(args, EXEC_PROJECT_ARG);
        var inputDir = ParseArg(args, INPUT_ARG, defaultValue: INPUT_ARG_DEFAULT);
        var outputDir = ParseArg(args, INPUT_ARG, defaultValue: Path.Combine(inputDir, OUTPUT_ARG_DEFAULT));
        var ignoreStr = ParseArg(args, IGNORE_ARG, defaultValue: string.Empty, hasAlias: false);

        var ignore = ignoreStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

        return new Args(execProjectFile, inputDir, outputDir, ignore);
    }

    private static string ParseArg(string[] args, string arg, string defaultValue = null, bool hasAlias = true)
    {
        var alias = ArgToAlias(arg);

        var prefixIndex = Array.IndexOf(args, arg);
        bool isAlias = false;

        if (prefixIndex == -1 && hasAlias)
        {
            prefixIndex = Array.IndexOf(args, alias);
            isAlias = true;
        }

        if (prefixIndex == -1)
        {
            if (defaultValue == null)
                throw new NotSupportedException($"Argument \"{arg}\" is required. Use {HELP_ARG} for help.");

            return defaultValue;
        }

        var argIndex = prefixIndex + 1;
        if (args.Length <= argIndex)
        {
            throw new NotSupportedException($"Argument \"{(isAlias ? alias : arg)}\" should have value. Use {HELP_ARG} for help.");
        }

        return args[argIndex];
    }

    private static void Main(string[] a)
    {
        Args args;

        try
        {
            args = ParseArgs(a);
            if (args is null)
                return;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return;
        }

        var execProjectPath = args.ExecProjectFile;
        if (!File.Exists(execProjectPath))
        {
            Console.Error.WriteLine($"File {execProjectPath} does not exist");
            return;
        }

        var configBuilder = ConfigBuilder
            .Create(args)
            .SetLogFile("obfuscar.log", xml: true)
            .AddVar("KeepPublicApi", false)
            .AddVar("HidePublicApi", true)
            .AddVar("SkipGenerated", true);

        ProjectTreeParser projectTreeParser = new();
        var projects = projectTreeParser.GetProjects(execProjectPath);

        foreach (var project in projects.OrderBy(p => p.Name))
        {
            Console.WriteLine($"Processing {project.Name}, {project.XamlFiles.Count} xaml files...");
            configBuilder.AddProject(project);
        }
        
        var config = configBuilder.Build();
        config.Save(CONFIG_FILENAME);

        Console.WriteLine($"Config saved to {CONFIG_FILENAME}, {configBuilder.ProjectCount} included");
    }
}
