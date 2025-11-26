using System.Xml.Linq;

public class Args
{
    public string ExecProjectFile { get; }
    public string InputFolder { get; }
    public string OutputFolder { get; }
    public IReadOnlyCollection<string> IgnoredModules { get; }
    public bool ObfuscateWpf { get; }
    public bool ObfuscatePlugins { get; }

    public Args(string execProjectFile, string inputFolder, string outputFolder, IReadOnlyCollection<string> ignoredModules, bool wpf, bool plugins)
    {
        ExecProjectFile = execProjectFile;
        InputFolder = inputFolder;
        OutputFolder = outputFolder;
        IgnoredModules = ignoredModules;
        ObfuscateWpf = wpf;
        ObfuscatePlugins = plugins;
    }
}

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
        const string WPF_ARG = "--wpf";
        const string PLUGIN_ARG = "--plugin";

        const string INPUT_ARG_DEFAULT = "./";
        const string OUTPUT_ARG_DEFAULT = "/Obfuscated/";
        const string WPF_ARG_DEFAULT = "false";
        const string PLUGIN_ARG_DEFAULT = "false";

        if (args.Any(a => a == HELP_ARG|| a == ArgToAlias(HELP_ARG)))
        {
            Console.WriteLine($@"
Required arguments:
    {EXEC_PROJECT_ARG}, {ArgToAlias(EXEC_PROJECT_ARG)}: Path to executable .csproj file.

Optional arguments:
    {INPUT_ARG}, {ArgToAlias(INPUT_ARG)}: Obfuscar input directory. 
        Default value: ""{INPUT_ARG_DEFAULT}"".

    {OUTPUT_ARG}, {ArgToAlias(OUTPUT_ARG)}: Obfuscar output directory. 
        Default value: ""<input>{OUTPUT_ARG_DEFAULT}"".

    {IGNORE_ARG}: Comma-separated list of modules to ignore.
        E.g., ""{IGNORE_ARG} Foo,Bar,Baz"" will exclude Foo.dll, Bar.dll and Baz.dll from list of modules.
        Default value: empty string.

    {WPF_ARG}: Configures if projects that have UseWPF property set to true should be obfuscated.
        E.g., ""{WPF_ARG} true"" will enable obfuscation of WPF projects.
        Default value: {WPF_ARG_DEFAULT}.

    {PLUGIN_ARG}: Configures if projects that have ""Plugin"" in their name should be obfuscated.
        E.g., ""{PLUGIN_ARG} true"" will enable obfuscation of modules like ""FooPlugin.dll"".
        Default value: {PLUGIN_ARG_DEFAULT}.

    {HELP_ARG}, {ArgToAlias(HELP_ARG)}: displays this help.
");
            return null;
        }

        var execProjectFile = ParseArg(args, EXEC_PROJECT_ARG);
        var inputDir = ParseArg(args, INPUT_ARG, defaultValue: INPUT_ARG_DEFAULT);
        var outputDir = ParseArg(args, INPUT_ARG, defaultValue: Path.Combine(inputDir, OUTPUT_ARG_DEFAULT));
        var ignoreStr = ParseArg(args, IGNORE_ARG, defaultValue: string.Empty, hasAlias: false);
        var wpfStr = ParseArg(args, WPF_ARG, defaultValue: WPF_ARG_DEFAULT, hasAlias: false);
        var pluginStr = ParseArg(args, PLUGIN_ARG, defaultValue: PLUGIN_ARG_DEFAULT, hasAlias: false);

        var ignore = ignoreStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var wpf = bool.Parse(wpfStr);
        var plugin = bool.Parse(pluginStr);

        return new Args(execProjectFile, inputDir, outputDir, ignore, wpf, plugin);
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
        var projectPaths = projectTreeParser.GetProjects(execProjectPath);

        ProjectParser projectParser = new();
        var projects = projectPaths
            .Select(p => projectParser.Parse(p))
            .OrderBy(p => p.Name); 

        foreach (var project in projects)
            configBuilder.AddProject(project);
        
        var config = configBuilder.Build();
        config.Save(CONFIG_FILENAME);

        Console.WriteLine($"Config saved to {CONFIG_FILENAME}, {configBuilder.ProjectCount} modules included");
    }
}

public class Project(string name, bool isWPF, bool isPlugin)
{
    public string Name { get; } = name;
    public bool IsWPF { get; } = isWPF;
    public bool IsPlugin { get; } = isPlugin;

    public string DllName => Name + ".dll";
}

public class ProjectParser
{
    public Project Parse(string pathToCsproj)
    {
        var doc = XDocument.Load(pathToCsproj);
        var useWpfEl = doc.Descendants("UseWPF").FirstOrDefault();
        var useWpf = useWpfEl is null ? false : bool.Parse(useWpfEl.Value);
        var name = Path.GetFileNameWithoutExtension(pathToCsproj);
        var isPlugin = name.Contains("Plugin");
        return new Project(name, useWpf, isPlugin);
    }
}

public class ProjectTreeParser
{
    public IReadOnlyCollection<string> GetProjects(string executableProjectFile)
    {
        var hashset = new HashSet<string>();
        ParseProjectAndRefs(executableProjectFile, hashset);
        return hashset;
    }

    private void ParseProjectAndRefs(string path, HashSet<string> hashset)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Extension != ".csproj")
            return;

        if (hashset.Contains(fileInfo.FullName))
            return;

        hashset.Add(fileInfo.FullName);

        var doc = XDocument.Load(path);
        var refs = doc.Descendants("ProjectReference");
        foreach (var @ref in refs)
        {
            var refPath = @ref.Attribute("Include").Value;
            var absRefPath = Path.Combine(fileInfo.DirectoryName, refPath);
            absRefPath = Path.GetFullPath(absRefPath); // normalize
            ParseProjectAndRefs(absRefPath, hashset);
        }
    }
}

public class ConfigBuilder
{
    private readonly Args _args;
    private readonly List<Project> _projects = new();
    private readonly XDocument _doc = new();
    private readonly XElement _root;

    public int ProjectCount { get; private set; }

    private ConfigBuilder(Args args)
    {
        _args = args;
        _doc = new XDocument();

        _root = new XElement("Obfuscator");
        _doc.Add(_root);

        AddVar("InPath", args.InputFolder);
        AddVar("OutPath", args.OutputFolder);
    }

    public static ConfigBuilder Create(Args args)
    {
        var builder = new ConfigBuilder(args);
        return builder;
    }

    public ConfigBuilder SetLogFile(string logFile, bool xml)
    {
        AddVar("LogFile", logFile);
        if (xml)
            AddVar("XmlMapping", true);

        return this;
    }

    public ConfigBuilder SetSearchPath(string searchPath)
    {
        var el = new XElement("AssemblySearchPath");
        el.Add(new XAttribute("path", searchPath));
        _root.Add(el);

        return this;
    }

    public ConfigBuilder AddProject(Project project)
    {
        if (project.IsWPF && _args.ObfuscateWpf == false)
            return this;

        if (project.IsPlugin && _args.ObfuscatePlugins == false)
            return this;

        if (_args.IgnoredModules.Contains(project.Name))
            return this;

        AddModule(project.DllName);

        return this;
    }

    public XDocument Build()
    {
        return _doc;
    }

    public ConfigBuilder AddVar(string name, object value)
    {
        var el = new XElement("Var");
        el.Add(new XAttribute("name", name));
        el.Add(new XAttribute("value", value));
        _root.Add(el);
        return this;
    }

    private ConfigBuilder AddModule(string file)
    {
        var el = new XElement("Module");
        el.Add(new XAttribute("file", $"$(InPath)\\{file}"));
        _root.Add(el);
        ++ProjectCount;
        return this;
    }
}
