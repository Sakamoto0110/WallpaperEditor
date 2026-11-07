using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Tests;

public sealed class TextItemTests : IDisposable
{
    private static readonly ItemRect SomeRect = new(10, 20, 300, 100);

    private readonly string _root = Directory.CreateTempSubdirectory("we-text-").FullName;
    private readonly string _path;
    private readonly string _background;
    private readonly List<ProjectFolder> _open = [];

    public TextItemTests()
    {
        _path = Path.Combine(_root, "Projeto");
        _background = Path.Combine(_root, "fundo.png");
        File.WriteAllText(_background, "background");
    }

    public void Dispose()
    {
        _open.ForEach(p => p.Dispose());
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Text_is_added_as_a_layer_and_survives_reopening()
    {
        var editor = NewEditor();

        var item = editor.AddText("Olá", SomeRect);
        editor.Project.Dispose();

        Assert.Equal("Olá", Reopen().Find<TextItem>(item.Id)!.Text);
    }

    [Fact]
    public void Text_is_saved_with_its_own_type_in_the_files()
    {
        var editor = NewEditor();
        editor.AddText("Olá", SomeRect);

        Assert.Contains("\"type\": \"text\"", File.ReadAllText(Path.Combine(_path, "revisions", "000002.json")));
        Assert.Contains("\"type\": \"text\"", File.ReadAllText(Path.Combine(_path, "composition.json")));
    }

    [Fact]
    public void Line_breaks_are_kept_and_stored_the_same_way_everywhere()
    {
        var editor = NewEditor();

        var item = editor.AddText("um\r\ndois\nthree\rquatro", SomeRect);

        Assert.Equal("um\ndois\nthree\nquatro", item.Text);
    }

    [Fact]
    public void Editing_creates_one_revision_named_after_the_new_text()
    {
        var editor = NewEditor();
        var item = editor.AddText("Antes", SomeRect);
        var head = editor.Project.Head;

        editor.SetText(item.Id, "Depois");

        Assert.Equal(head + 1, editor.Project.Head);
        Assert.Equal("Depois", editor.Find<TextItem>(item.Id)!.Text);
        Assert.Contains("Depois", editor.Project.ReadHistory()[0].Change!.Label);
    }

    [Fact]
    public void Leaving_the_text_as_it_was_creates_no_revision()
    {
        var editor = NewEditor();
        var item = editor.AddText("um\ndois", SomeRect);
        var head = editor.Project.Head;

        // A multi-line box hands back Windows line endings for the same content.
        editor.SetText(item.Id, "um\r\ndois");

        Assert.Equal(head, editor.Project.Head);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void Text_cannot_be_blank(string blank)
    {
        var editor = NewEditor();
        var item = editor.AddText("Algo", SomeRect);

        Assert.Throws<ArgumentException>(() => editor.AddText(blank, SomeRect));
        Assert.Throws<ArgumentException>(() => editor.SetText(item.Id, blank));
        Assert.Equal("Algo", editor.Find<TextItem>(item.Id)!.Text);
    }

    [Fact]
    public void Text_shares_the_look_controls_with_checklists()
    {
        var editor = NewEditor();
        var item = editor.AddText("Olá", SomeRect);

        editor.SetItemFont(item.Id, "Consolas");
        editor.SetItemFontSize(item.Id, 40);
        editor.SetItemTextOpacity(item.Id, 0.5);
        editor.SetItemPanelOpacity(item.Id, 0);
        editor.Project.Dispose();

        var style = Reopen().Find<TextItem>(item.Id)!.Style;
        Assert.Equal("Consolas", style.Font);
        Assert.Equal(40, style.Size);
        Assert.Equal(0.5, style.TextOpacity, 2);
        Assert.Equal(0, style.PanelOpacity, 2);
    }

    [Fact]
    public void Text_grows_to_fit_when_added_and_when_edited()
    {
        var project = ProjectFolder.Create(_path, "Projeto", new PixelSize(2560, 1080), _background, FitMode.Fill);
        _open.Add(project);
        var editor = new ProjectEditor(project, item => ((TextItem)item).Text.Length * 10);

        var item = editor.AddText(new string('x', 20), new ItemRect(0, 0, 300, 100));
        Assert.Equal(200, item.Rect.Height);

        editor.SetText(item.Id, new string('x', 50));
        Assert.Equal(500, editor.Find<TextItem>(item.Id)!.Rect.Height);
    }

    [Fact]
    public void A_removed_text_can_be_restored()
    {
        var editor = NewEditor();
        var item = editor.AddText("Olá", SomeRect);
        var had = editor.Project.Head;
        editor.RemoveItem(item.Id);

        editor.RestoreItem(had, item.Id);

        Assert.Equal("Olá", editor.Find<TextItem>(item.Id)!.Text);
    }

    [Fact]
    public void A_project_from_before_text_layers_still_opens()
    {
        // A checklist saved by the previous version: "style" sat on the checklist itself, as it still does in JSON.
        var editor = NewEditor();
        var list = editor.AddChecklist("Hoje", SomeRect);
        editor.Project.Dispose();

        var json = File.ReadAllText(Path.Combine(_path, "revisions", "000002.json"));
        Assert.Contains("\"style\"", json);

        Assert.NotNull(Reopen().Find<ChecklistItem>(list.Id));
    }

    [Theory]
    [InlineData("Curto", "Curto")]
    [InlineData("Primeira linha\nSegunda linha", "Primeira linha")]
    [InlineData("\n\n  Depois de linhas vazias", "Depois de linhas vazias")]
    [InlineData("123456789012345678901234567890", "123456789012345678901234567890")]
    [InlineData("1234567890123456789012345678901", "123456789012345678901234567890…")]
    public void The_label_is_the_start_of_the_first_line(string text, string expected)
    {
        var item = new TextItem { Id = "t", Rect = SomeRect, Text = text };

        Assert.Equal(expected, item.Label());
    }

    private ProjectEditor NewEditor()
    {
        var project = ProjectFolder.Create(_path, "Projeto", new PixelSize(2560, 1080), _background, FitMode.Fill);
        _open.Add(project);
        return new ProjectEditor(project);
    }

    private ProjectEditor Reopen()
    {
        var project = ProjectFolder.Open(_path);
        _open.Add(project);
        return new ProjectEditor(project);
    }
}
