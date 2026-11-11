namespace HydrogenXML.Core.Workspace;

public sealed class SampleFile
{
    public string Dataset { get; init; } = "";
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public long Size { get; init; }
    public string Display => string.IsNullOrEmpty(Dataset) ? Name : $"{Dataset} / {Name}";
}

/// <summary>Stores sample XML documents in named datasets under a folder.</summary>
public sealed class SampleStore
{
    public const string DefaultDataset = "General";

    public string RootDirectory { get; }

    public SampleStore(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        Directory.CreateDirectory(RootDirectory);
    }

    public IReadOnlyList<string> GetDatasets()
    {
        var list = Directory.GetDirectories(RootDirectory).Select(System.IO.Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!list.Contains(DefaultDataset, StringComparer.OrdinalIgnoreCase)) list.Insert(0, DefaultDataset);
        return list;
    }

    public IReadOnlyList<SampleFile> GetSamples()
    {
        var result = new List<SampleFile>();
        foreach (var f in Directory.GetFiles(RootDirectory, "*.xml"))
            result.Add(Make(DefaultDataset, f));
        foreach (var dir in Directory.GetDirectories(RootDirectory))
        {
            var ds = System.IO.Path.GetFileName(dir);
            foreach (var f in Directory.GetFiles(dir, "*.xml"))
                result.Add(Make(ds, f));
        }

        return result.OrderBy(s => s.Dataset, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static SampleFile Make(string dataset, string file) => new()
    {
        Dataset = dataset,
        Name = System.IO.Path.GetFileName(file),
        Path = file,
        Size = new FileInfo(file).Length
    };

    public void CreateDataset(string name)
    {
        Directory.CreateDirectory(System.IO.Path.Combine(RootDirectory, Sanitize(name)));
    }

    public string Add(string dataset, string name, string content)
    {
        var path = UniquePath(dataset, name);
        File.WriteAllText(path, content);
        return path;
    }

    public string AddCopy(string dataset, string sourcePath)
    {
        var path = UniquePath(dataset, System.IO.Path.GetFileName(sourcePath));
        File.Copy(sourcePath, path);
        return path;
    }

    public void Delete(SampleFile sample)
    {
        if (File.Exists(sample.Path)) File.Delete(sample.Path);
    }

    private string UniquePath(string dataset, string name)
    {
        var dir = string.IsNullOrWhiteSpace(dataset) || dataset == DefaultDataset
            ? RootDirectory
            : System.IO.Path.Combine(RootDirectory, Sanitize(dataset));
        Directory.CreateDirectory(dir);
        name = Sanitize(name);
        if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) name += ".xml";
        var path = System.IO.Path.Combine(dir, name);
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        for (var i = 2; File.Exists(path); i++)
            path = System.IO.Path.Combine(dir, $"{stem} ({i}).xml");
        return path;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
