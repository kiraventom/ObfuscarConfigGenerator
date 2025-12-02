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

