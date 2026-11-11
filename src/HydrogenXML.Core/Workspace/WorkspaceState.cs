using System.Text.Json;
using System.Text.Json.Serialization;

namespace HydrogenXML.Core.Workspace;

public sealed class OpenDocumentState
{
    public string? Path { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Unsaved edits are stored in a backup file so they survive closing the app.</summary>
    public string? BackupFile { get; set; }
    public int CaretOffset { get; set; }
    public double VerticalOffset { get; set; }
}

public sealed class ParameterSetting
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class TransformSettings
{
    public bool AutoTransform { get; set; }
    public int AutoTransformDelayMs { get; set; } = 1000;
    public bool PrettyPrintOutput { get; set; } = true;
    public string? InitialTemplate { get; set; }
    public string? InitialMode { get; set; }
    public List<ParameterSetting> Parameters { get; set; } = new();
}

public sealed class LayoutState
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1500;
    public double Height { get; set; } = 900;
    public bool Maximized { get; set; }
    public double XmlPaneWeight { get; set; } = 1;
    public double XslPaneWeight { get; set; } = 1.3;
    public double OutputPaneWeight { get; set; } = 1;
    public bool SidebarVisible { get; set; } = true;
    public double SidebarWidth { get; set; } = 240;
    public bool BottomPanelVisible { get; set; } = true;
    public double BottomPanelHeight { get; set; } = 220;
    public int BottomTabIndex { get; set; }
    public int SidebarTabIndex { get; set; }
    public bool XmlPaneVisible { get; set; } = true;
    public bool OutputPaneVisible { get; set; } = true;
    public bool OutputWordWrap { get; set; }
    public bool XslSplit { get; set; }
    public string Theme { get; set; } = "Dark";
}

public sealed class WorkspaceState
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Default";
    public List<OpenDocumentState> XslDocuments { get; set; } = new();
    public List<OpenDocumentState> XmlDocuments { get; set; } = new();
    public int ActiveXslIndex { get; set; }
    public int ActiveXmlIndex { get; set; }
    public string? CurrentXslPath { get; set; }
    public string? CurrentXmlPath { get; set; }
    public List<string> RecentXsl { get; set; } = new();
    public List<string> RecentXml { get; set; } = new();
    public List<string> RecentWorkspaces { get; set; } = new();
    public TransformSettings Transform { get; set; } = new();
    public LayoutState Layout { get; set; } = new();
}

/// <summary>Reads and writes workspace files and keeps backups of unsaved documents.</summary>
public sealed class WorkspaceService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public string RootDirectory { get; }
    public string DefaultWorkspacePath => Path.Combine(RootDirectory, "workspace.json");
    public string BackupDirectory => Path.Combine(RootDirectory, "Backup");
    public string SessionFile => Path.Combine(RootDirectory, "session.json");

    public WorkspaceService(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HydrogenXML");
        if (rootDirectory == null) MigrateLegacyData(RootDirectory);
        Directory.CreateDirectory(RootDirectory);
    }

    // Copies data saved under the application's former name so existing samples and workspaces carry over.
    private static void MigrateLegacyData(string target)
    {
        try
        {
            var legacy = Path.Combine(Path.GetDirectoryName(target)!, "MyXSLT");
            if (Directory.Exists(target) || !Directory.Exists(legacy)) return;
            foreach (var dir in Directory.EnumerateDirectories(legacy, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(legacy, dir)));
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(target, Path.GetRelativePath(legacy, file)), true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public WorkspaceState Load(string? path = null)
    {
        path ??= DefaultWorkspacePath;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<WorkspaceState>(json, Options) ?? new WorkspaceState();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged workspace file must never stop the app from starting.
            try { File.Copy(path, path + ".corrupt", true); } catch { }
        }

        return new WorkspaceState();
    }

    public void Save(WorkspaceState state, string? path = null)
    {
        path ??= DefaultWorkspacePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, path, true);
    }

    /// <summary>Path of the workspace that was open last (empty means the default workspace).</summary>
    public string? LoadLastWorkspacePath()
    {
        try
        {
            if (File.Exists(SessionFile))
            {
                var p = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SessionFile));
                if (p != null && p.TryGetValue("workspace", out var w) && File.Exists(w)) return w;
            }
        }
        catch { }

        return null;
    }

    public void SaveLastWorkspacePath(string? path)
    {
        try
        {
            var d = new Dictionary<string, string> { ["workspace"] = path ?? "" };
            File.WriteAllText(SessionFile, JsonSerializer.Serialize(d));
        }
        catch { }
    }

    public string WriteBackup(string text)
    {
        Directory.CreateDirectory(BackupDirectory);
        var file = Path.Combine(BackupDirectory, Guid.NewGuid().ToString("N") + ".bak");
        File.WriteAllText(file, text);
        return file;
    }

    public string? ReadBackup(string? file)
    {
        try { return file != null && File.Exists(file) ? File.ReadAllText(file) : null; }
        catch { return null; }
    }

    public void ClearBackups()
    {
        try
        {
            if (Directory.Exists(BackupDirectory))
                foreach (var f in Directory.GetFiles(BackupDirectory, "*.bak"))
                    File.Delete(f);
        }
        catch { }
    }
}
