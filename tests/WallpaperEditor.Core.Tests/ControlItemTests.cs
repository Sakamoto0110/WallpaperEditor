using System.Text.RegularExpressions;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Tests;

/// <summary>Counters, progress bars and switches: values changed by clicking in the editor.</summary>
public sealed class ControlItemTests : IDisposable
{
    private static readonly ItemRect SomeRect = new(10, 20, 300, 100);

    private readonly string _root = Directory.CreateTempSubdirectory("we-control-").FullName;
    private readonly string _path;
    private readonly string _background;
    private readonly List<ProjectFolder> _open = [];

    public ControlItemTests()
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
    public void Each_control_is_saved_with_its_own_type_and_survives_reopening()
    {
        var editor = NewEditor();
        var counter = editor.AddCounter("Dias", SomeRect);
        var progress = editor.AddProgress("Leitura", SomeRect);
        var toggle = editor.AddToggle("Modo", SomeRect);
        editor.StepCounter(counter.Id, 3);
        editor.SetProgress(progress.Id, 40);
        editor.SetToggle(toggle.Id, true);
        editor.Project.Dispose();

        var reopened = Reopen();
        Assert.Equal(3, reopened.Find<CounterItem>(counter.Id)!.Value);
        Assert.Equal(40, reopened.Find<ProgressItem>(progress.Id)!.Percent);
        Assert.True(reopened.Find<ToggleItem>(toggle.Id)!.On);

        var composition = File.ReadAllText(Path.Combine(_path, "composition.json"));
        Assert.Contains("\"type\": \"counter\"", composition);
        Assert.Contains("\"type\": \"progress\"", composition);
        Assert.Contains("\"type\": \"toggle\"", composition);
    }

    [Fact]
    public void Each_step_of_a_counter_is_one_revision_named_after_the_new_value()
    {
        var editor = NewEditor();
        var counter = editor.AddCounter("Dias", SomeRect);
        var head = editor.Project.Head;

        editor.StepCounter(counter.Id, 1);
        editor.StepCounter(counter.Id, 1);
        editor.StepCounter(counter.Id, -1);

        Assert.Equal(head + 3, editor.Project.Head);
        Assert.Equal(1, editor.Find<CounterItem>(counter.Id)!.Value);

        var last = editor.Project.ReadRevision(editor.Project.Head).Change;
        Assert.Equal(ChangeKinds.ValueChanged, last.Kind);
        Assert.Equal("Mudou o contador \"Dias\" para 1", last.Label);
    }

    [Fact]
    public void A_counter_can_go_below_zero()
    {
        var editor = NewEditor();
        var counter = editor.AddCounter("Saldo", SomeRect);

        editor.StepCounter(counter.Id, -2);

        Assert.Equal(-2, editor.Find<CounterItem>(counter.Id)!.Value);
    }

    [Fact]
    public void A_counter_stops_at_the_largest_number_instead_of_wrapping_around()
    {
        var editor = NewEditor();
        var counter = editor.AddCounter("Muito", SomeRect);
        editor.SetCounterValue(counter.Id, int.MaxValue);

        editor.StepCounter(counter.Id, 1);

        Assert.Equal(int.MaxValue, editor.Find<CounterItem>(counter.Id)!.Value);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(55, 55)]
    [InlineData(140, 100)]
    public void Progress_stays_between_0_and_100(int asked, int expected)
    {
        var editor = NewEditor();
        var progress = editor.AddProgress("Leitura", SomeRect);

        editor.SetProgress(progress.Id, asked);

        Assert.Equal(expected, editor.Find<ProgressItem>(progress.Id)!.Percent);
    }

    [Fact]
    public void A_switch_is_described_by_the_state_it_went_to()
    {
        var editor = NewEditor();
        var toggle = editor.AddToggle("Modo", SomeRect);
        editor.SetToggleTexts(toggle.Id, "Foco", "Livre");

        editor.SetToggle(toggle.Id, true);

        Assert.Equal("Mudou a alternância \"Modo\" para \"Foco\"", editor.Project.ReadRevision(editor.Project.Head).Change.Label);
        Assert.Equal("Foco", editor.Find<ToggleItem>(toggle.Id)!.StateText);
    }

    [Fact]
    public void The_history_joins_de_with_the_article_as_Portuguese_does()
    {
        var editor = NewEditor();
        var toggle = editor.AddToggle("Modo", SomeRect);
        var counter = editor.AddCounter("Dias", SomeRect);

        editor.SetToggleTexts(toggle.Id, "Foco", "Livre");
        var states = editor.Project.ReadRevision(editor.Project.Head).Change.Label;
        editor.SetItemFont(counter.Id, "Consolas");
        var font = editor.Project.ReadRevision(editor.Project.Head).Change.Label;

        Assert.Equal("Mudou os estados da alternância \"Modo\"", states);
        Assert.Equal("Mudou a fonte do contador \"Dias\" para Consolas", font);
    }

    [Fact]
    public void A_panel_can_become_a_balloon_pointing_either_way()
    {
        var editor = NewEditor();
        var text = editor.AddText("Pensando", SomeRect);

        editor.SetItemPanelShape(text.Id, PanelShape.Thought);
        var shapeLabel = editor.Project.ReadRevision(editor.Project.Head).Change.Label;
        editor.SetItemTail(text.Id, TailSide.Right);
        var tailLabel = editor.Project.ReadRevision(editor.Project.Head).Change.Label;
        editor.Project.Dispose();

        var style = Reopen().Find<TextItem>(text.Id)!.Style;
        Assert.Equal(PanelShape.Thought, style.Shape);
        Assert.Equal(TailSide.Right, style.Tail);
        Assert.Equal("Mudou o painel do texto \"Pensando\" para balão de pensamento", shapeLabel);
        Assert.Equal("Virou a ponta do balão do texto \"Pensando\" para a direita", tailLabel);
    }

    [Fact]
    public void A_project_saved_before_shapes_existed_opens_with_rounded_panels()
    {
        var editor = NewEditor();
        var text = editor.AddText("Antigo", SomeRect);
        editor.Project.Dispose();

        // As the file looked before: no shape and no tail in the style.
        foreach (var file in Directory.GetFiles(Path.Combine(_path, "revisions"), "*.json"))
        {
            var old = Regex.Replace(File.ReadAllText(file), "\"(shape|tail)\": \"\\w+\",?\\s*", string.Empty);
            File.WriteAllText(file, Regex.Replace(old, ",(\\s*})", "$1"));
            Assert.DoesNotContain("\"shape\"", File.ReadAllText(file), StringComparison.Ordinal);
        }

        var style = Reopen().Find<TextItem>(text.Id)!.Style;
        Assert.Equal(PanelShape.Rounded, style.Shape);
        Assert.Equal(TailSide.Left, style.Tail);
    }

    [Fact]
    public void Mirroring_an_image_is_one_revision_each_way_and_survives_reopening()
    {
        var picture = Path.Combine(_root, "gato.png");
        File.WriteAllText(picture, "image");
        var editor = NewEditor();
        var image = editor.AddImage(picture, SomeRect);

        editor.ToggleMirror(image.Id);
        var on = editor.Project.ReadRevision(editor.Project.Head).Change.Label;
        editor.ToggleMirror(image.Id);
        var off = editor.Project.ReadRevision(editor.Project.Head).Change.Label;
        editor.ToggleMirror(image.Id);
        editor.Project.Dispose();

        Assert.Equal("Espelhou a imagem gato.png", on);
        Assert.Equal("Desfez o espelho da imagem gato.png", off);
        Assert.True(Reopen().Find<ImageItem>(image.Id)!.Mirrored);
    }

    [Fact]
    public void Mirroring_a_turned_image_turns_it_the_other_way_so_the_screen_shows_a_mirror()
    {
        var picture = Path.Combine(_root, "gato.png");
        File.WriteAllText(picture, "image");
        var editor = NewEditor();
        var image = editor.AddImage(picture, SomeRect);
        editor.RotateItem(image.Id, 30);

        editor.MirrorItem(image.Id, vertically: true);
        var label = editor.Project.ReadRevision(editor.Project.Head).Change.Label;
        var mirrored = editor.Find<ImageItem>(image.Id)!;

        Assert.Equal("Espelhou a imagem gato.png na vertical", label);
        Assert.True(mirrored.MirroredVertically);
        Assert.False(mirrored.Mirrored);
        Assert.Equal(330, mirrored.Rotation);
    }

    [Theory]
    [InlineData(90, 90)]
    [InlineData(-90, 270)]
    [InlineData(180, 180)]
    public void Rotating_an_item_adds_to_its_angle(double degrees, double expected)
    {
        var editor = NewEditor();
        var list = editor.AddChecklist("Hoje", SomeRect);

        editor.RotateItem(list.Id, degrees);

        Assert.Equal(expected, editor.Find<ChecklistItem>(list.Id)!.Rotation);
        Assert.StartsWith("Girou a checklist", editor.Project.ReadRevision(editor.Project.Head).Change.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void A_switch_state_cannot_have_empty_text()
    {
        var editor = NewEditor();
        var toggle = editor.AddToggle("Modo", SomeRect);

        Assert.ThrowsAny<ArgumentException>(() => editor.SetToggleTexts(toggle.Id, "Foco", " "));
    }

    [Fact]
    public void Setting_the_same_value_again_records_nothing()
    {
        var editor = NewEditor();
        var counter = editor.AddCounter("Dias", SomeRect);
        var progress = editor.AddProgress("Leitura", SomeRect);
        var toggle = editor.AddToggle("Modo", SomeRect);
        var head = editor.Project.Head;

        editor.SetCounterValue(counter.Id, 0);
        editor.SetProgress(progress.Id, 0);
        editor.SetToggle(toggle.Id, false);
        editor.SetControlLabel(counter.Id, "Dias");
        editor.SetToggleTexts(toggle.Id, "Ligado", "Desligado");

        Assert.Equal(head, editor.Project.Head);
    }

    [Fact]
    public void Renaming_a_control_trims_the_label()
    {
        var editor = NewEditor();
        var progress = editor.AddProgress("Leitura", SomeRect);

        editor.SetControlLabel(progress.Id, "  Livro  ");

        Assert.Equal("Livro", editor.Find<ProgressItem>(progress.Id)!.Label);
        Assert.Equal("Renomeou a barra de progresso \"Leitura\" para \"Livro\"", editor.Project.ReadRevision(editor.Project.Head).Change.Label);
    }

    [Fact]
    public void A_control_without_a_label_is_still_named_in_the_history()
    {
        var editor = NewEditor();

        editor.AddCounter(string.Empty, SomeRect);

        Assert.Equal("Adicionou o contador", editor.Project.ReadRevision(editor.Project.Head).Change.Label);
    }

    [Fact]
    public void A_new_control_grows_to_fit_its_content()
    {
        var project = ProjectFolder.Create(_path, "Projeto", new PixelSize(2560, 1080), _background, FitMode.Fill);
        _open.Add(project);
        var editor = new ProjectEditor(project, _ => 150);

        var counter = editor.AddCounter("Dias", SomeRect with { Height = 40 });

        Assert.Equal(150, counter.Rect.Height);
    }

    [Fact]
    public void A_removed_control_comes_back_with_its_value()
    {
        var editor = NewEditor();
        var counter = editor.AddCounter("Dias", SomeRect);
        editor.StepCounter(counter.Id, 7);
        var before = editor.Project.Head;
        editor.RemoveItem(counter.Id);

        editor.RestoreItem(before, counter.Id);

        Assert.Equal(7, editor.Find<CounterItem>(counter.Id)!.Value);
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
