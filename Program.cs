using System.Xml.Linq;

internal class Program
{
    private static void Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: ObfuscarConfigGenerator path-to-solution.sln /path/to/input/folder/ /path/to/output/folder/");
            return;
        }

        var solutionPath = (string)args[0];
        if (!File.Exists(solutionPath))
        {
            Console.Error.WriteLine($"File {solutionPath} does not exist");
            return;
        }

        var inputDir = (string)args[1];
        var outputDir = (string)args[2];

        var configBuilder = ConfigBuilder
            .Create(inputDir, outputDir)
            .SetLogFile("obfuscar.log", xml: true)
            .AddVar("KeepPublicApi", false)
            .AddVar("HidePublicApi", true)
            .AddVar("SkipGenerated", true);

        SolutionParser solutionParser = new();
        var projectPaths = solutionParser.GetProjectPaths(solutionPath);

        ProjectParser projectParser = new();
        var projects = projectPaths
            .Select(p => projectParser.Parse(p))
            .OrderBy(p => p.Name); 

        foreach (var project in projects)
            configBuilder.AddProject(project);
        
        var config = configBuilder.Build();
        config.Save("obfuscar_config.xml");
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
        var useWpfEl = doc.Element("UseWPF");
        var useWpf = useWpfEl is null ? false : bool.Parse(useWpfEl.Value);
        var name = Path.GetFileNameWithoutExtension(pathToCsproj);
        var isPlugin = name.Contains("Plugin");
        return new Project(name, useWpf, isPlugin);
    }
}

public class SolutionParser
{
    private const string CS_PROJECT_GUID =  @"FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";
    private const string FOLDER_GUID =      @"2150E333-8FFC-42A3-9474-1A3956D46DE8";
    private const string CPP_PROJECT_GUID = @"8BC9CEB8-8B4A011D0-8D11-00A0C91BC942";

    public IReadOnlyCollection<string> GetProjectPaths(string solutionFileName)
    {
        var list = new List<string>();

        var solutionDir = Path.GetDirectoryName(solutionFileName);

        using var fs = File.OpenText(solutionFileName);
        while (fs.ReadLine() is {} line)
        {
            if (!line.Contains(CS_PROJECT_GUID))
                continue;

            var start = line.IndexOf(',') + 3;
            var end = line.IndexOf(',', start) - 1;
            
            if (start == -1 || end == -1)
                Console.Error.WriteLine($"Failed to parse C# project: '{line}'");
            
            var projectPath = line[start..end];
            var absProjectPath = Path.Combine(solutionDir, projectPath);
            list.Add(absProjectPath);
        }

        return list;
    }
}

public class ConfigBuilder
{
    private readonly List<Project> _projects = new();
    private readonly XDocument _doc = new();
    private readonly XElement _root;

    private ConfigBuilder(string inPath, string outPath)
    {
        _doc = new XDocument();

        _root = new XElement("Obfuscator");
        _doc.Add(_root);

        AddVar("InPath", inPath);
        AddVar("OutPath", outPath);
    }

    public static ConfigBuilder Create(string inPath, string outPath)
    {
        var builder = new ConfigBuilder(inPath, outPath);
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
        if (!project.IsWPF && !project.IsPlugin)
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
        return this;
    }
}
