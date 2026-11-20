using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Projects;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;

namespace DesktopAgent.App.Views;

public sealed class ProjectFilesView : UserControl
{
    private readonly IAgentToolRouter router;
    private Guid? projectId;
    private CancellationTokenSource? operation;
    private readonly TextBox query = new() { MaxLength = 160, ToolTip = "Search the selected project", Margin = new Thickness(0, 5, 0, 5) };
    private readonly ComboBox mode = new() { ItemsSource = new[] { "File names", "Code/text", "Symbols/routes" }, SelectedIndex = 0 };
    private readonly ListBox results = new() { MinHeight = 140, DisplayMemberPath = "Label", MaxHeight = 300 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox preview = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox start = new() { Text = "1", Width = 55 }, end = new() { Text = "80", Width = 55 };
    private sealed record Item(string Path, bool Directory, int Line, string Label);
    public ProjectFilesView(IAgentToolRouter router)
    {
        this.router = router;
        var panel = new StackPanel(); Content = panel;
        panel.Children.Add(new TextBlock { Text = "Read-only project explorer · protected files are hidden", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(query); panel.Children.Add(mode);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        void Button(string label, Func<Task> run) { var button = new Button { Content = label }; button.Click += async (_, _) => await run(); buttons.Children.Add(button); }
        Button("List root", () => Run("list_project_directory", new { path = "" }));
        Button("Tree", () => Run("get_project_tree", new { }));
        Button("Search", () => Run(mode.SelectedIndex switch { 0 => "search_project_files", 1 => "search_project_code", _ => "find_project_symbol" },
            mode.SelectedIndex == 2 ? (object)new { query = query.Text.Trim() } : new { query = query.Text.Trim(), extension = (string?)null, directory = (string?)null }));
        Button("Cancel", () => { operation?.Cancel(); return Task.CompletedTask; });
        panel.Children.Add(status); panel.Children.Add(results);
        var range = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(range);
        range.Children.Add(new TextBlock { Text = "Read lines " }); range.Children.Add(start); range.Children.Add(new TextBlock { Text = " to " }); range.Children.Add(end);
        Button("Inspect selected", async () => {
            if (results.SelectedItem is not Item item) return;
            if (item.Directory) { await Run("list_project_directory", new { path = item.Path }); return; }
            if (!int.TryParse(start.Text, out var first) || !int.TryParse(end.Text, out var last)) { status.Text = "Enter a valid line range."; return; }
            await Run("read_project_file", new { path = item.Path, start_line = first, end_line = last });
        });
        Button("Open selected ↗", async () => { if (results.SelectedItem is Item { Directory: false } item) await Run("open_project_file", new { path = item.Path, line = item.Line }); });
        panel.Children.Add(preview);
        results.SelectionChanged += (_, _) => { if (results.SelectedItem is Item i) { start.Text = Math.Max(1, i.Line - 10).ToString(); end.Text = (i.Line + 60).ToString(); } };
        Unloaded += (_, _) => operation?.Cancel();
    }
    public void SelectProject(Guid? id)
    { if (projectId == id) return; operation?.Cancel(); projectId = id; results.ItemsSource = null; preview.Clear(); status.Text = id.HasValue ? "List or search this project to begin." : "Select a registered project."; }
    private async Task Run(string tool, object arguments)
    {
        if (operation is not null || projectId is not { } id) return;
        using var cancellation = new CancellationTokenSource(); operation = cancellation; status.Text = "Working…";
        try
        {
            var args = JsonSerializer.SerializeToElement(arguments).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone()); args["project"] = id.ToString();
            var result = await router.InvokeAsync(new(Guid.NewGuid().ToString("N"), tool, JsonSerializer.Serialize(args)), cancellation.Token);
            if (projectId != id) return;
            status.Text = result.Message;
            if (result.Data is ProjectTree tree) { results.ItemsSource = tree.Entries.Select(e => new Item(e.RelativePath, e.IsDirectory, 1, (e.IsDirectory ? "▸ " : "") + e.RelativePath)).ToArray(); status.Text += tree.Truncated ? " Partial tree/list; select a directory to narrow." : ""; }
            if (result.Data is ProjectSearchResult search) results.ItemsSource = search.Results.Select(r => new Item(r.RelativePath, false, r.LineNumber, r.RelativePath + ":" + r.LineNumber + "\n" + r.MatchedText)).ToArray();
            if (result.Data is ProjectFileRead read) preview.Text = string.Join("\n", read.Lines.Select(l => l.Number + "  " + l.Text));
        }
        catch (OperationCanceledException) { status.Text = "Stopped."; }
        catch { status.Text = "File operation unavailable. Refresh or select the project again."; }
        finally { operation = null; }
    }
}
