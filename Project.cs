using System.Xml.Linq;

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

