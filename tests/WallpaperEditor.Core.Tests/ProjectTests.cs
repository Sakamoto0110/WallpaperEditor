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
        var editor = new ProjectEditor(project, item => 100 + (item.Entries.Count * 50));
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
        var editor = new ProjectEditor(project, item => 100 + (item.Entries.Count * 50));
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
