using WallpaperEditor.Core.Publishing;

namespace WallpaperEditor.Core.Tests;

public sealed class DataMirrorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("we-mirror-").FullName;
    private readonly string _data;
    private readonly string _mirrorRoot;
    private readonly DataMirror _mirror;
    private readonly AppStateStore _store;

    public DataMirrorTests()
    {
        _data = Path.Combine(_root, "AppData");
        _mirrorRoot = Path.Combine(_root, ".wallpaperEditor");
        _mirror = new DataMirror(_data, _mirrorRoot);
        _store = new AppStateStore(Path.Combine(_data, "state.json"), _mirror);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Saving_the_state_keeps_a_copy_with_the_originals_but_not_the_published_images()
    {
        Write(@"published\abc\original\montanha.jpg", "original");
        Write(@"published\abc\0123.png", "published");

        _store.Save(StateWith("abc"));

        Assert.Equal(File.ReadAllText(Path.Combine(_data, "state.json")), File.ReadAllText(Path.Combine(_mirrorRoot, "state.json")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(_mirrorRoot, @"published\abc\original\montanha.jpg")));
        Assert.False(File.Exists(Path.Combine(_mirrorRoot, @"published\abc\0123.png")));
    }

    [Fact]
    public void A_deleted_data_folder_comes_back_from_the_mirror()
    {
        Write(@"published\abc\original\montanha.jpg", "original");
        _store.Save(StateWith("abc"));

        Directory.Delete(_data, recursive: true);
        var state = _store.Load();

        Assert.True(state.Monitors.ContainsKey("abc"));
        Assert.Equal("original", File.ReadAllText(Path.Combine(_data, @"published\abc\original\montanha.jpg")));
    }

    [Fact]
    public void A_damaged_state_is_set_aside_and_the_mirrored_one_is_used()
    {
        _store.Save(StateWith("abc"));
        File.WriteAllText(Path.Combine(_data, "state.json"), "{ not json");

        var state = _store.Load();

        Assert.True(state.Monitors.ContainsKey("abc"));
        Assert.Single(Directory.GetFiles(_data, "state.json.*.unreadable"));
    }

    [Fact]
    public void A_damaged_mirror_does_not_loop_and_leaves_an_empty_state()
    {
        Directory.CreateDirectory(_mirrorRoot);
        File.WriteAllText(Path.Combine(_mirrorRoot, "state.json"), "{ also not json");

        Assert.Empty(_store.Load().Monitors);
    }

    [Fact]
    public void A_good_state_is_never_replaced_by_the_mirror()
    {
        _store.Save(StateWith("abc"));
        Directory.CreateDirectory(_mirrorRoot);
        File.WriteAllText(Path.Combine(_mirrorRoot, "state.json"), "{ \"FormatVersion\": 1, \"Monitors\": {} }");

        Assert.False(_mirror.RestoreIfMissing());
        Assert.True(_store.Load().Monitors.ContainsKey("abc"));
    }

    [Fact]
    public void An_original_that_is_gone_from_the_data_folder_goes_from_the_mirror_too()
    {
        Write(@"published\abc\original\velho.jpg", "old");
        _store.Save(StateWith("abc"));

        File.Delete(Path.Combine(_data, @"published\abc\original\velho.jpg"));
        Write(@"published\abc\original\novo.jpg", "new");
        _store.Save(StateWith("abc"));

        Assert.False(File.Exists(Path.Combine(_mirrorRoot, @"published\abc\original\velho.jpg")));
        Assert.True(File.Exists(Path.Combine(_mirrorRoot, @"published\abc\original\novo.jpg")));
    }

    [Fact]
    public void Other_files_in_the_mirror_folder_are_left_alone()
    {
        Directory.CreateDirectory(_mirrorRoot);
        File.WriteAllText(Path.Combine(_mirrorRoot, "leia-me.txt"), "meu");

        _store.Save(StateWith("abc"));

        Assert.True(File.Exists(Path.Combine(_mirrorRoot, "leia-me.txt")));
    }

    [Fact]
    public void A_mirror_that_cannot_be_written_does_not_stop_the_state_from_saving()
    {
        // A file where the folder should be: every write into the mirror fails.
        File.WriteAllText(_mirrorRoot, "not a folder");

        _store.Save(StateWith("abc"));

        Assert.False(_mirror.Save());
        Assert.True(_store.Load().Monitors.ContainsKey("abc"));
    }

    private static AppState StateWith(string monitor)
    {
        var state = new AppState();
        state.Monitors[monitor] = new MonitorAssignment { MonitorId = monitor, LastKnownName = "Monitor" };
        return state;
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_data, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
