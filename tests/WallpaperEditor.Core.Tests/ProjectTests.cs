using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Tests;

public sealed class ProjectTests : IDisposable
{
    private static readonly ItemRect SomeRect = new(10, 20, 300, 200);

    private readonly string _root = Directory.CreateTempSubdirectory("we-project-").FullName;
    private readonly string _path;
    private readonly string _background;
    private readonly string _cheatsheet;
    private readonly List<ProjectFolder> _open = [];

    public ProjectTests()
    {
        _path = Path.Combine(_root, "Minha mesa");
        _background = Touch("montanha.JPG", "background");
        _cheatsheet = Touch("linq.png", "cheatsheet");
    }

    public void Dispose()
    {
        _open.ForEach(p => p.Dispose());
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_new_project_has_one_revision_with_the_background()
    {
        var editor = NewEditor();

        Assert.Equal(1, editor.Project.Head);
        Assert.Equal(new PixelSize(2560, 1080), editor.Project.Canvas);
        Assert.Equal("Minha mesa", editor.Project.Name);
        Assert.Equal("montanha.JPG", editor.Document.Background.Source.OriginalName);
        Assert.Empty(editor.Document.Items);
        Assert.Equal(ChangeKinds.Created, Assert.Single(editor.Project.ReadHistory()).Change!.Kind);
    }

    [Fact]
    public void A_folder_with_something_in_it_is_not_used_for_a_new_project()
    {
        Directory.CreateDirectory(_path);
        File.WriteAllText(Path.Combine(_path, "important.txt"), "keep");

        Assert.Throws<ProjectUnavailableException>(() => ProjectFolder.Create(_path, "x", new PixelSize(10, 10), _background, FitMode.Fill));
        Assert.True(File.Exists(Path.Combine(_path, "important.txt")));
    }

    [Fact]
    public void Assets_are_copied_and_the_original_is_no_longer_needed()
    {
        var editor = NewEditor();
        var item = editor.AddImage(_cheatsheet, SomeRect);

        File.Delete(_cheatsheet);

        Assert.True(File.Exists(editor.Project.ResolveAsset(item.Source.Asset)));
    }

    [Fact]
    public void Importing_the_same_file_twice_stores_it_once()
    {
        var editor = NewEditor();
        var copy = Touch("another-name.png", "cheatsheet");

        var first = editor.AddImage(_cheatsheet, SomeRect);
        var second = editor.AddImage(copy, SomeRect);

        Assert.Equal(first.Source.Asset, second.Source.Asset);
        Assert.Equal("another-name.png", second.Source.OriginalName);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_path, "assets")).Length);
    }

    [Theory]
    [InlineData(@"..\..\secret.txt")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("sub/file.png")]
    public void An_asset_name_cannot_point_outside_the_project(string name)
    {
        var editor = NewEditor();

        Assert.Throws<ProjectUnavailableException>(() => editor.Project.ResolveAsset(name));
    }

    [Fact]
    public void Each_change_is_one_revision_and_survives_reopening()
    {
        var editor = NewEditor();
        var image = editor.AddImage(_cheatsheet, SomeRect);
        var list = editor.AddChecklist("Hoje", new ItemRect(0, 0, 400, 300));
        editor.AddEntry(list.Id, "Comprar café");
        editor.Place(image.Id, new ItemRect(50, 60, 300, 200), 15);

        editor.Project.Dispose();
        var reopened = Reopen();

        Assert.Equal(5, reopened.Project.Head);
        Assert.Equal(5, reopened.Project.ReadHistory().Count);
        Assert.Equal(new ItemRect(50, 60, 300, 200), reopened.Find<ImageItem>(image.Id)!.Rect);
        Assert.Equal(15, reopened.Find<ImageItem>(image.Id)!.Rotation);
        Assert.Equal("Comprar café", Assert.Single(reopened.Find<ChecklistItem>(list.Id)!.Entries).Text);
    }

    [Fact]
    public void Turning_the_background_is_a_revision_that_survives_reopening()
    {
        var editor = NewEditor();
        editor.SetBackgroundOrientation(new ImageOrientation(90, FlipHorizontal: true));
        editor.SetBackgroundOrientation(new ImageOrientation(90, FlipHorizontal: true));

        editor.Project.Dispose();
        var reopened = Reopen();

        Assert.Equal(2, reopened.Project.Head);
        Assert.Equal(new ImageOrientation(90, FlipHorizontal: true), reopened.Document.Background.Orientation);
        Assert.Equal(ChangeKinds.BackgroundChanged, reopened.Project.ReadHistory()[0].Change!.Kind);
    }

    [Fact]
    public void A_background_that_is_not_turned_writes_no_orientation()
    {
        NewEditor();

        var revision = File.ReadAllText(Path.Combine(_path, "revisions", "000001.json"));

        Assert.DoesNotContain("orientation", revision, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dropping_an_item_where_it_was_creates_no_revision()
    {
        var editor = NewEditor();
        var image = editor.AddImage(_cheatsheet, SomeRect);
        var head = editor.Project.Head;

        editor.Place(image.Id, SomeRect, 0);

        Assert.Equal(head, editor.Project.Head);
    }

    [Fact]
    public void Items_keep_their_drawing_order_and_can_be_reordered()
    {
        var editor = NewEditor();
        var a = editor.AddImage(_cheatsheet, SomeRect);
        var b = editor.AddChecklist("B", SomeRect);
        var c = editor.AddChecklist("C", SomeRect);

        editor.Reorder(a.Id, 1);
        Assert.Equal([b.Id, a.Id, c.Id], editor.Document.Items.Select(i => i.Id));

        editor.Reorder(a.Id, 99);
        Assert.Equal([b.Id, c.Id, a.Id], editor.Document.Items.Select(i => i.Id));
    }

    [Fact]
    public void Moving_an_item_past_the_end_of_the_order_creates_no_revision()
    {
        var editor = NewEditor();
        var a = editor.AddImage(_cheatsheet, SomeRect);
        var head = editor.Project.Head;

        editor.Reorder(a.Id, 1);

        Assert.Equal(head, editor.Project.Head);
    }

    [Fact]
    public void Checklist_entries_can_be_added_toggled_edited_and_removed()
    {
        var editor = NewEditor();
        var list = editor.AddChecklist("Hoje", SomeRect);

        editor.AddEntry(list.Id, "  Comprar café  ");
        var entry = Assert.Single(editor.Find<ChecklistItem>(list.Id)!.Entries);
        Assert.Equal("Comprar café", entry.Text);
        Assert.False(entry.Done);

        editor.ToggleEntry(list.Id, entry.Id);
        Assert.True(Assert.Single(editor.Find<ChecklistItem>(list.Id)!.Entries).Done);
        Assert.Equal(ChangeKinds.EntryToggled, editor.Project.ReadHistory()[0].Change!.Kind);
        Assert.StartsWith("Marcou", editor.Project.ReadHistory()[0].Change!.Label);

        editor.ToggleEntry(list.Id, entry.Id);
        Assert.StartsWith("Desmarcou", editor.Project.ReadHistory()[0].Change!.Label);

        editor.EditEntry(list.Id, entry.Id, "Comprar pão");
        Assert.Equal("Comprar pão", Assert.Single(editor.Find<ChecklistItem>(list.Id)!.Entries).Text);

        editor.RemoveEntry(list.Id, entry.Id);
        Assert.Empty(editor.Find<ChecklistItem>(list.Id)!.Entries);
    }

    [Fact]
    public void A_checklist_grows_to_fit_new_entries_in_the_same_revision()
    {
        var project = ProjectFolder.Create(_path, "Minha mesa", new PixelSize(2560, 1080), _background, FitMode.Fill);
        _open.Add(project);
        var editor = new ProjectEditor(project, item => 100 + (((ChecklistItem)item).Entries.Count * 50));
        var list = editor.AddChecklist("Hoje", new ItemRect(0, 0, 300, 120));
        var head = project.Head;

        editor.AddEntry(list.Id, "Uma");
        Assert.Equal(150, editor.Find<ChecklistItem>(list.Id)!.Rect.Height);
        Assert.Equal(head + 1, project.Head);
    }

    [Fact]
    public void A_checklist_does_not_shrink_when_entries_are_removed()
    {
        var project = ProjectFolder.Create(_path, "Minha mesa", new PixelSize(2560, 1080), _background, FitMode.Fill);
        _open.Add(project);
        var editor = new ProjectEditor(project, item => 100 + (((ChecklistItem)item).Entries.Count * 50));
        var list = editor.AddChecklist("Hoje", new ItemRect(0, 0, 300, 120));
        editor.AddEntry(list.Id, "Uma");
        var entry = Assert.Single(editor.Find<ChecklistItem>(list.Id)!.Entries);

        editor.RemoveEntry(list.Id, entry.Id);

        Assert.Equal(150, editor.Find<ChecklistItem>(list.Id)!.Rect.Height);
    }

    [Fact]
    public void Reverting_restores_the_document_and_keeps_all_of_the_history()
    {
        var editor = NewEditor();
        var before = editor.Document;
        var image = editor.AddImage(_cheatsheet, SomeRect);
        var withImage = editor.Project.Head;

        editor.Revert(1);

        Assert.True(editor.Document.HasSameContentAs(before));
        Assert.Equal(3, editor.Project.Head);
        Assert.Equal(ChangeKinds.Reverted, editor.Project.ReadHistory()[0].Change!.Kind);
        Assert.Equal(1, editor.Project.ReadHistory()[0].Change!.Target);

        // What came after the point reverted to still exists, and can be returned to.
        editor.Revert(withImage);
        Assert.NotNull(editor.Find<ImageItem>(image.Id));
        Assert.Equal(4, editor.Project.Head);
    }

    [Fact]
    public void One_removed_item_can_be_restored_without_undoing_later_work()
    {
        var editor = NewEditor();
        var image = editor.AddImage(_cheatsheet, SomeRect);
        var hadImage = editor.Project.Head;
        var list = editor.AddChecklist("Hoje", SomeRect);
        editor.RemoveItem(image.Id);
        editor.AddEntry(list.Id, "Depois de remover");

        editor.RestoreItem(hadImage, image.Id);

        Assert.NotNull(editor.Find<ImageItem>(image.Id));
        Assert.Single(editor.Find<ChecklistItem>(list.Id)!.Entries);
        Assert.Equal(ChangeKinds.ItemRestored, editor.Project.ReadHistory()[0].Change!.Kind);
    }

    [Fact]
    public void A_restored_item_returns_to_its_place_in_the_stack()
    {
        var editor = NewEditor();
        var bottom = editor.AddImage(_cheatsheet, SomeRect);
        var middle = editor.AddChecklist("Meio", SomeRect);
        var top = editor.AddChecklist("Topo", SomeRect);
        var hadAll = editor.Project.Head;
        editor.RemoveItem(middle.Id);

        editor.RestoreItem(hadAll, middle.Id);

        Assert.Equal([bottom.Id, middle.Id, top.Id], editor.Document.Items.Select(i => i.Id));
    }

    [Fact]
    public void A_restored_item_goes_above_the_nearest_survivor_that_was_below_it()
    {
        var editor = NewEditor();
        var a = editor.AddChecklist("A", SomeRect);
        var b = editor.AddChecklist("B", SomeRect);
        var c = editor.AddChecklist("C", SomeRect);
        var d = editor.AddChecklist("D", SomeRect);
        var hadAll = editor.Project.Head;
        editor.RemoveItem(c.Id);
        editor.RemoveItem(b.Id);

        editor.RestoreItem(hadAll, c.Id);

        Assert.Equal([a.Id, c.Id, d.Id], editor.Document.Items.Select(i => i.Id));
    }

    [Fact]
    public void An_item_that_was_at_the_bottom_returns_to_the_bottom()
    {
        var editor = NewEditor();
        var bottom = editor.AddImage(_cheatsheet, SomeRect);
        var top = editor.AddChecklist("Topo", SomeRect);
        var hadBoth = editor.Project.Head;
        editor.RemoveItem(bottom.Id);

        editor.RestoreItem(hadBoth, bottom.Id);

        Assert.Equal([bottom.Id, top.Id], editor.Document.Items.Select(i => i.Id));
    }

    [Fact]
    public void A_restored_item_stays_above_later_additions_only_if_it_was()
    {
        var editor = NewEditor();
        var first = editor.AddImage(_cheatsheet, SomeRect);
        var second = editor.AddChecklist("Segundo", SomeRect);
        var hadBoth = editor.Project.Head;
        editor.RemoveItem(second.Id);
        var later = editor.AddChecklist("Depois", SomeRect);

        editor.RestoreItem(hadBoth, second.Id);

        Assert.Equal([first.Id, second.Id, later.Id], editor.Document.Items.Select(i => i.Id));
    }

    [Fact]
    public void The_composition_file_lists_the_layers_with_their_original_names_in_order()
    {
        var editor = NewEditor();
        var image = editor.AddImage(_cheatsheet, SomeRect);
        editor.AddChecklist("Hoje", SomeRect);

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_path, "composition.json")));
        var root = json.RootElement;

        Assert.Equal(3, root.GetProperty("revision").GetInt32());
        Assert.Equal("montanha.JPG", root.GetProperty("background").GetProperty("originalName").GetString());
        Assert.StartsWith("assets/", root.GetProperty("background").GetProperty("file").GetString());

        var layers = root.GetProperty("layers");
        Assert.Equal(2, layers.GetArrayLength());
        Assert.Equal(1, layers[0].GetProperty("order").GetInt32());
        Assert.Equal(image.Id, layers[0].GetProperty("item").GetProperty("id").GetString());
        Assert.Equal("linq.png", layers[0].GetProperty("item").GetProperty("source").GetProperty("originalName").GetString());
        Assert.Equal("Hoje", layers[1].GetProperty("item").GetProperty("title").GetString());
    }

    [Fact]
    public void The_project_files_keep_accents_readable()
    {
        var editor = NewEditor();
        var list = editor.AddChecklist("Atenção", SomeRect);
        editor.AddEntry(list.Id, "Comprar café");

        Assert.Contains("Atenção", File.ReadAllText(Path.Combine(_path, "composition.json")));
        Assert.Contains("Comprar café", File.ReadAllText(Path.Combine(_path, "revisions", "000003.json")));
    }

    [Fact]
    public void The_composition_file_follows_a_revert()
    {
        var editor = NewEditor();
        editor.AddChecklist("Hoje", SomeRect);

        editor.Revert(1);

        Assert.DoesNotContain("Hoje", File.ReadAllText(Path.Combine(_path, "composition.json")));
    }

    [Fact]
    public void Tampering_with_the_composition_file_changes_nothing()
    {
        var editor = NewEditor();
        var list = editor.AddChecklist("Hoje", SomeRect);
        editor.Project.Dispose();
        File.WriteAllText(Path.Combine(_path, "composition.json"), "{ nonsense");

        var reopened = Reopen();

        Assert.NotNull(reopened.Find<ChecklistItem>(list.Id));
        Assert.Contains("Hoje", File.ReadAllText(Path.Combine(_path, "composition.json")));
    }

    [Fact]
    public void An_older_project_without_the_composition_file_gets_one_on_opening()
    {
        NewEditor().Project.Dispose();
        File.Delete(Path.Combine(_path, "composition.json"));

        Reopen();

        Assert.True(File.Exists(Path.Combine(_path, "composition.json")));
    }

    [Fact]
    public void A_read_only_instance_does_not_write_the_composition_file()
    {
        NewEditor();
        var file = Path.Combine(_path, "composition.json");
        var written = File.GetLastWriteTimeUtc(file);

        var second = ProjectFolder.Open(_path);
        _open.Add(second);

        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void The_font_of_a_checklist_can_be_changed_and_is_saved()
    {
        var editor = NewEditor();
        var list = editor.AddChecklist("Hoje", SomeRect);

        editor.SetItemFont(list.Id, "Consolas");
        editor.Project.Dispose();

        Assert.Equal("Consolas", Reopen().Find<ChecklistItem>(list.Id)!.Style.Font);
    }

    [Fact]
    public void Choosing_the_same_font_creates_no_revision()
    {
        var editor = NewEditor();
        var list = editor.AddChecklist("Hoje", SomeRect);
        var head = editor.Project.Head;

        editor.SetItemFont(list.Id, TextStyle.Default.Font);

        Assert.Equal(head, editor.Project.Head);
    }

    [Fact]
    public void A_checklist_grows_when_a_wider_font_needs_more_room()
    {
        var project = ProjectFolder.Create(_path, "Minha mesa", new PixelSize(2560, 1080), _background, FitMode.Fill);
        _open.Add(project);
        var editor = new ProjectEditor(project, item => item.Style.Font == "Larga" ? 500 : 100);
        var list = editor.AddChecklist("Hoje", new ItemRect(0, 0, 300, 120));

        editor.SetItemFont(list.Id, "Larga");

        Assert.Equal(500, editor.Find<ChecklistItem>(list.Id)!.Rect.Height);
    }

    [Fact]
    public void Text_and_panel_opacity_are_independent_and_saved()
    {
        var editor = NewEditor();
        var list = editor.AddChecklist("Hoje", SomeRect);

        editor.SetItemTextOpacity(list.Id, 0.5);
        editor.SetItemPanelOpacity(list.Id, 0);
        editor.Project.Dispose();

        var style = Reopen().Find<ChecklistItem>(list.Id)!.Style;
        Assert.Equal(0.5, style.TextOpacity, 2);
        Assert.Equal(0, style.PanelOpacity, 2);
    }

    [Fact]
    public void Setting_the_same_opacity_creates_no_revision()
    {
        var editor = NewEditor();
        var list = editor.AddChecklist("Hoje", SomeRect);
        var head = editor.Project.Head;

        editor.SetItemTextOpacity(list.Id, TextStyle.Default.TextOpacity);
        editor.SetItemPanelOpacity(list.Id, TextStyle.Default.PanelOpacity);

        Assert.Equal(head, editor.Project.Head);
    }

    [Theory]
    [InlineData("#FFFFFF", 1.0)]
    [InlineData("#FF00C800", 1.0)]
    [InlineData("#00101218", 0.0)]
    [InlineData("#80101218", 0.502)]
    [InlineData("white", 1.0)]
    public void The_alpha_of_a_color_can_be_read(string color, double expected)
    {
        Assert.Equal(expected, ColorAlpha.Of(color), 3);
    }

    [Theory]
    [InlineData("#FFFFFF", 0.5, "#80FFFFFF")]
    [InlineData("#BE101218", 1.0, "#FF101218")]
    [InlineData("#BE101218", 0.0, "#00101218")]
    [InlineData("#FFFFFF", 7.0, "#FFFFFF")]
    [InlineData("#FFFFFF", 1.0, "#FFFFFF")]
    [InlineData("#80FFFFFF", 0.5, "#80FFFFFF")]
    [InlineData("white", 0.5, "white")]
    public void The_alpha_of_a_color_can_be_changed(string color, double opacity, string expected)
    {
        Assert.Equal(expected, ColorAlpha.With(color, opacity));
    }

    [Fact]
    public void Restoring_an_item_that_still_exists_replaces_it_without_duplicating()
    {
        var editor = NewEditor();
        var image = editor.AddImage(_cheatsheet, SomeRect);
        var original = editor.Project.Head;
        editor.Place(image.Id, new ItemRect(500, 500, 300, 200), 0);

        editor.RestoreItem(original, image.Id);

        Assert.Equal(SomeRect, Assert.Single(editor.Document.Items).Rect);
    }

    [Fact]
    public void A_project_folder_can_be_moved()
    {
        var editor = NewEditor();
        var image = editor.AddImage(_cheatsheet, SomeRect);
        editor.Project.Dispose();

        var moved = Path.Combine(_root, "Outro lugar");
        Directory.Move(_path, moved);
        var project = ProjectFolder.Open(moved);
        _open.Add(project);

        var reopened = new ProjectEditor(project);
        Assert.True(File.Exists(project.ResolveAsset(reopened.Find<ImageItem>(image.Id)!.Source.Asset)));
    }

    [Fact]
    public void A_second_instance_opens_the_project_read_only()
    {
        var editor = NewEditor();

        var second = ProjectFolder.Open(_path);
        _open.Add(second);

        Assert.False(editor.Project.IsReadOnly);
        Assert.True(second.IsReadOnly);
        Assert.Throws<ProjectReadOnlyException>(() => new ProjectEditor(second).AddChecklist("x", SomeRect));
    }

    [Fact]
    public void The_lock_goes_away_with_the_instance_that_held_it()
    {
        var editor = NewEditor();
        editor.Project.Dispose();

        var second = ProjectFolder.Open(_path);
        _open.Add(second);

        Assert.False(second.IsReadOnly);
    }

    [Fact]
    public void A_write_interrupted_before_the_rename_leaves_no_trace()
    {
        var editor = NewEditor();
        editor.Project.Dispose();
        var leftover = Path.Combine(_path, "revisions", ".tmp-abc.json");
        File.WriteAllText(leftover, "half a rev");

        var reopened = Reopen();

        Assert.False(File.Exists(leftover));
        Assert.Equal(1, reopened.Project.Head);
        Assert.Null(reopened.Project.RecoveryNote);
    }

    [Fact]
    public void A_revision_written_without_updating_the_pointer_is_kept()
    {
        // Simulates dying between the rename and rewriting project.json.
        var editor = NewEditor();
        editor.AddChecklist("Hoje", SomeRect);
        editor.Project.Dispose();
        var projectFile = Path.Combine(_path, "project.json");
        File.WriteAllText(projectFile, File.ReadAllText(projectFile).Replace("\"head\": 2", "\"head\": 1"));

        var reopened = Reopen();

        Assert.Equal(2, reopened.Project.Head);
        Assert.Single(reopened.Document.Items);
        Assert.NotNull(reopened.Project.RecoveryNote);
    }

    [Fact]
    public void A_damaged_revision_does_not_take_the_others_with_it()
    {
        var editor = NewEditor();
        editor.AddChecklist("Um", SomeRect);
        editor.AddChecklist("Dois", SomeRect);
        editor.Project.Dispose();
        File.WriteAllText(Path.Combine(_path, "revisions", "000002.json"), "{ damaged");

        var reopened = Reopen();

        Assert.Equal(3, reopened.Project.Head);
        Assert.Equal(2, reopened.Document.Items.Count);
        var history = reopened.Project.ReadHistory();
        Assert.Equal(3, history.Count);
        Assert.False(history.Single(h => h.Number == 2).IsValid);
    }

    [Fact]
    public void A_damaged_head_falls_back_to_the_newest_readable_revision()
    {
        var editor = NewEditor();
        editor.AddChecklist("Um", SomeRect);
        editor.AddChecklist("Dois", SomeRect);
        editor.Project.Dispose();
        File.WriteAllText(Path.Combine(_path, "revisions", "000003.json"), "{ damaged");

        var reopened = Reopen();

        Assert.Equal(2, reopened.Project.Head);
        Assert.Single(reopened.Document.Items);
        Assert.Contains("danificada", reopened.Project.RecoveryNote);
    }

    [Fact]
    public void A_new_revision_after_a_damaged_head_does_not_overwrite_it()
    {
        var editor = NewEditor();
        editor.AddChecklist("Um", SomeRect);
        editor.Project.Dispose();
        var damaged = Path.Combine(_path, "revisions", "000002.json");
        File.WriteAllText(damaged, "{ damaged");

        var reopened = Reopen();
        reopened.AddChecklist("Novo", SomeRect);

        Assert.Equal(3, reopened.Project.Head);
        Assert.Equal("{ damaged", File.ReadAllText(damaged));
    }

    [Fact]
    public void A_folder_that_is_not_a_project_is_refused()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;

        Assert.Throws<ProjectUnavailableException>(() => ProjectFolder.Open(empty));
    }

    [Fact]
    public void A_project_from_a_newer_version_is_refused_and_left_alone()
    {
        NewEditor().Project.Dispose();
        var projectFile = Path.Combine(_path, "project.json");
        var content = File.ReadAllText(projectFile).Replace("\"formatVersion\": 1", "\"formatVersion\": 99");
        File.WriteAllText(projectFile, content);

        Assert.Throws<ProjectUnavailableException>(() => ProjectFolder.Open(_path));
        Assert.Equal(content, File.ReadAllText(projectFile));
    }

    [Fact]
    public void Revisions_use_the_documented_json_shape()
    {
        var editor = NewEditor();
        editor.AddChecklist("Hoje", SomeRect);

        var json = File.ReadAllText(Path.Combine(_path, "revisions", "000002.json"));

        Assert.Contains("\"type\": \"checklist\"", json);
        Assert.Contains("\"fit\": \"fill\"", json);
        Assert.Contains("\"kind\": \"item-added\"", json);
    }

    private ProjectEditor NewEditor()
    {
        var project = ProjectFolder.Create(_path, "Minha mesa", new PixelSize(2560, 1080), _background, FitMode.Fill);
        _open.Add(project);
        return new ProjectEditor(project);
    }

    private ProjectEditor Reopen()
    {
        var project = ProjectFolder.Open(_path);
        _open.Add(project);
        return new ProjectEditor(project);
    }

    private string Touch(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
}
