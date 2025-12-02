using System.Text.RegularExpressions;
using System.Xml.Linq;

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
        HashSet<string> typesToSkip = new();

        foreach (var xamlFile in project.XamlFiles)
        {
            CollectXamlCsType(typesToSkip, xamlFile);

            var namespaces = CollectNamespaces(xamlFile);
            CollectTypesUsedInXaml(typesToSkip, xamlFile, namespaces);
        }

        var skipTypeElements = typesToSkip.Select(t => BuildSkipTypeElement(t));
        foreach (var skipTypeEl in skipTypeElements)
            moduleEl.Add(skipTypeEl);
    }

    private static void CollectTypesUsedInXaml(HashSet<string> typesToSkip, FileInfo xamlFile, IEnumerable<XamlNamespace> namespaces)
    {
        using var fs = File.OpenText(xamlFile.FullName);
        while (fs.ReadLine() is { } line)
        {
            foreach (var ns in namespaces)
            {
                var match = ns.XamlAliasRegex.Match(line);
                if (!match.Success)
                    continue;

                var @class = match.Groups[1].Value;
                typesToSkip.Add($"{ns.CSharpNamespace}.{@class}");
            }
        }
    }

    private static IEnumerable<XamlNamespace> CollectNamespaces(FileInfo xamlFile)
    {
        var namespaces = new List<XamlNamespace>();

        using var fs = File.OpenText(xamlFile.FullName);
        while (fs.ReadLine() is { } line)
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

        return namespaces;
    }

    private static void CollectXamlCsType(HashSet<string> typesToSkip, FileInfo xamlFile)
    {
        var xamlCsFilePath = Path.ChangeExtension(xamlFile.FullName, ".xaml.cs");

        if (!File.Exists(xamlCsFilePath))
            return;

        using var fs = File.OpenText(xamlCsFilePath);
        string @namespace = null;
        List<string> classes = new();

        while (fs.ReadLine() is { } line)
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
                typesToSkip.Add($"{@namespace}.{@class}");
        }
    }

    private static XElement BuildSkipTypeElement(string type)
    {
        var skipXamlCsTypeEl = new XElement("SkipType");
        skipXamlCsTypeEl.Add(new XAttribute("type", type));
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

