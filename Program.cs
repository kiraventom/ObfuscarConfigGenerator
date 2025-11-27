using System.Text.RegularExpressions;
using System.Xml.Linq;

public class Args
{
    public string ExecProjectFile { get; }
    public string InputFolder { get; }
    public string OutputFolder { get; }
    public IReadOnlyCollection<string> IgnoredModules { get; }

    public Args(string execProjectFile, string inputFolder, string outputFolder, IReadOnlyCollection<string> ignoredModules)
    {
        ExecProjectFile = execProjectFile;
        InputFolder = inputFolder;
        OutputFolder = outputFolder;
        IgnoredModules = ignoredModules;
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
            configBuilder.AddProject(project);
        
        var config = configBuilder.Build();
        config.Save(CONFIG_FILENAME);

        Console.WriteLine($"Config saved to {CONFIG_FILENAME}, {configBuilder.ProjectCount} included");
    }
}

public class Project
{
    public string Path { get; }
    public string Name { get; }
    public string Namespace { get; }
    public IReadOnlyCollection<FileInfo> XamlFiles { get; }
    public bool IsNetFramework { get; }

    public string DllName => Name + ".dll";

    private Project(string path, string name, string @namespace, IReadOnlyCollection<FileInfo> xamlFiles, bool isNetFramework)
    {
        Path = path;
        Name = name;
        Namespace = @namespace;
        XamlFiles = xamlFiles;
        IsNetFramework = isNetFramework;
    }

    public static Project Parse(FileInfo fileInfo, XDocument doc)
    {
        var projectDir = fileInfo.Directory;

        var assemblyName = doc.Descendants("AssemblyName").FirstOrDefault()?.Value;
        var filename = System.IO.Path.GetFileNameWithoutExtension(fileInfo.FullName);

        var rootNamespace = doc.Descendants("RootNamespace").FirstOrDefault()?.Value;

        var useFrameworkEl = doc.Descendants("TargetFramework").FirstOrDefault();
        var useFramework = useFrameworkEl is null ? false : useFrameworkEl.Value.Contains("net4");

        var xamlFiles = projectDir.GetFiles("*.xaml", new EnumerationOptions() { RecurseSubdirectories = true });

        return new Project(fileInfo.FullName, assemblyName ?? filename, rootNamespace ?? filename, xamlFiles, useFramework);
    }
}

public class ProjectTreeParser
{
    public IEnumerable<Project> GetProjects(string executableProjectFile)
    {
        var dict = new Dictionary<string, Project>();
        ParseProjectAndRefs(executableProjectFile, dict);
        RemoveInvalidRefs(executableProjectFile, dict, clean: false);
        return dict.Values;
    }

    private void ParseProjectAndRefs(string path, Dictionary<string, Project> dict)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Extension != "*.csproj")
            return;

        if (dict.ContainsKey(fileInfo.FullName))
            return;

        var doc = XDocument.Load(path);
        var project = Project.Parse(fileInfo, doc);
        dict.Add(project.Path, project);

        var refs = doc.Descendants("ProjectReference");
        foreach (var @ref in refs)
        {
            var refPath = @ref.Attribute("Include").Value;
            var absRefPath = Path.Combine(fileInfo.DirectoryName, refPath);
            absRefPath = Path.GetFullPath(absRefPath); // normalize
            ParseProjectAndRefs(absRefPath, dict);
        }
    }

    private void RemoveInvalidRefs(string path, Dictionary<string, Project> dict, bool clean)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Extension != "*.csproj")
            clean = true;

        if (clean)
            dict.Remove(path);

        var doc = XDocument.Load(path);

        var refs = doc.Descendants("ProjectReference");
        foreach (var @ref in refs)
        {
            var refPath = @ref.Attribute("Include").Value;
            var absRefPath = Path.Combine(fileInfo.DirectoryName, refPath);
            absRefPath = Path.GetFullPath(absRefPath); // normalize
            RemoveInvalidRefs(absRefPath, dict, clean);
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

    public ConfigBuilder AddProject(Project project)
    {
        if (project.IsNetFramework)
            return this;

        if (_args.IgnoredModules.Contains(project.Name))
            return this;

        AddModule(project);

        return this;
    }

    private const string XAML_CS_NAMESPACE_REGEX = @"^\s*namespace\s*([\w\.]*)\s*";
    private const string XAML_CS_CLASS_REGEX = @"^\s*.*\s*class\s*(\w*)\s*";
    private const string XAML_NAMESPACE_REGEX = @"xmlns:(\w*)=""clr-namespace:([\w\.]*)""";

    private static Regex XamlCsNamespaceRegex { get; } = new Regex(XAML_CS_NAMESPACE_REGEX);
    private static Regex XamlCsClassRegex { get; } = new Regex(XAML_CS_CLASS_REGEX);
    private static Regex XamlNamespaceRegex { get; } = new Regex(XAML_NAMESPACE_REGEX);

    private void SkipXamlTypes(Project project, XElement moduleEl)
    {
        foreach (var xamlFile in project.XamlFiles)
        {
            var xamlCsFilePath = Path.ChangeExtension(xamlFile.FullName, ".xaml.cs");

            // Handling .xaml.cs file
            if (File.Exists(xamlCsFilePath))
            {
                using var fs = File.OpenText(xamlCsFilePath);
                string @namespace = null;
                List<string> classes = new();

                while (fs.ReadLine() is {} line)
                {
                    var namespaceMatch = XamlCsNamespaceRegex.Match(line);
                    if (!namespaceMatch.Success)
                        continue;

                    @namespace = namespaceMatch.Groups[1].Value;

                    var classMatch = XamlCsClassRegex.Match(line);
                    if (!classMatch.Success)
                        continue;

                    classes.Add(classMatch.Groups[1].Value);
                }

                if (@namespace != null)
                {
                    foreach (var @class in classes)
                    {
                        var skipTypeEl = BuildSkipTypeElement(@namespace, @class);
                        moduleEl.Add(skipTypeEl);
                    }
                }
            }

            var namespaces = new List<XamlNamespace>();

            // Collecting namespaces used in XAML
            {
                using var fs = File.OpenText(xamlFile.FullName);
                while (fs.ReadLine() is {} line)
                {
                    var namespaceMatch = XamlNamespaceRegex.Match(line);
                    if (!namespaceMatch.Success)
                        continue;

                    string alias = namespaceMatch.Groups[1].Value;
                    string @namespace = namespaceMatch.Groups[2].Value;

                    if (@namespace.StartsWith("System") || @namespace.StartsWith("Microsoft"))
                        continue;

                    string regexStr = $"{alias}:(\\w*)";
                    var regex = new Regex(regexStr);

                    namespaces.Add(new XamlNamespace(regex, @namespace));
                }
            }

            // Skipping types used in XAML
            {
                using var fs = File.OpenText(xamlFile.FullName);
                while (fs.ReadLine() is {} line)
                {
                    foreach (var ns in namespaces)
                    {
                        var match = ns.XamlAliasRegex.Match(line);
                        if (!match.Success)
                            continue;

                        var @class = match.Groups[1].Value;
                        var skipTypeEl = BuildSkipTypeElement(ns.CSharpNamespace, @class);
                        moduleEl.Add(skipTypeEl);
                    }
                }
            }
        }
    }

    private static XElement BuildSkipTypeElement(string @namespace, string @class)
    {
        var skipXamlCsTypeEl = new XElement("SkipType");
        skipXamlCsTypeEl.Add(new XAttribute("type", $"{@namespace}.{@class}"));
        skipXamlCsTypeEl.Add(new XAttribute("skipMethods", true));
        skipXamlCsTypeEl.Add(new XAttribute("skipProperties", true));
        return skipXamlCsTypeEl;
    }

    private ConfigBuilder AddModule(Project project)
    {
        var el = new XElement("Module");
        el.Add(new XAttribute("file", $"$(InPath)\\{project.DllName}"));
        SkipXamlTypes(project, el);
        _root.Add(el);
        ++ProjectCount;
        return this;
    }

    private readonly struct XamlNamespace
    {
        public Regex XamlAliasRegex { get; }
        public string CSharpNamespace { get; }

        public XamlNamespace(Regex xamlAliasRegex, string cSharpNamespace)
        {
            XamlAliasRegex = xamlAliasRegex;
            CSharpNamespace = cSharpNamespace;
        }
    }
}
