using System.Xml.Linq;

public class ProjectTreeParser
{
    public IEnumerable<Project> GetProjects(string executableProjectFile)
    {
        var dict = new Dictionary<string, Project>();
        ParseProjectAndRefs(executableProjectFile, dict);

        var toClean = new Dictionary<string, bool>();
        RemoveInvalidRefs(executableProjectFile, toClean, clean: false);
        foreach (var p in toClean.Where(p => p.Value).Select(p => p.Key))
        {
            dict.Remove(p);
            Console.WriteLine($"Removed project {p}");
        }

        return dict.Values;
    }

    private void ParseProjectAndRefs(string path, Dictionary<string, Project> dict)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Extension != ".csproj")
            return;

        if (dict.ContainsKey(fileInfo.FullName))
            return;

        var doc = XDocument.Load(path);
        var project = Project.Parse(fileInfo, doc);
        dict.Add(project.Path, project);

        Console.WriteLine($"Added project {project.Name}");

        var refs = doc.Descendants("ProjectReference");
        foreach (var @ref in refs)
        {
            var refPath = @ref.Attribute("Include").Value;
            var absRefPath = Path.Combine(fileInfo.DirectoryName, refPath);
            absRefPath = Path.GetFullPath(absRefPath); // normalize
            ParseProjectAndRefs(absRefPath, dict);
        }
    }

    private void RemoveInvalidRefs(string path, Dictionary<string, bool> toClean, bool clean)
    {
        if (toClean.ContainsKey(path))
            return;

        var fileInfo = new FileInfo(path);
        if (fileInfo.Extension != ".csproj")
            clean = true;

        toClean.Add(path, clean);

        var doc = XDocument.Load(path);

        var refs = doc.Descendants("ProjectReference");
        foreach (var @ref in refs)
        {
            var refPath = @ref.Attribute("Include").Value;
            var absRefPath = Path.Combine(fileInfo.DirectoryName, refPath);
            absRefPath = Path.GetFullPath(absRefPath); // normalize
            RemoveInvalidRefs(absRefPath, toClean, clean);
        }
    }
}

