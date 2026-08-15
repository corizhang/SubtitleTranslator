using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SubtitleTranslator.Subtitles;

namespace SubtitleTranslator.App;

public sealed class EditableSubtitleCue : INotifyPropertyChanged
{
    private string startText;
    private string endText;
    private string text;
    private string issueSummary = string.Empty;

    public EditableSubtitleCue(int number, TimeSpan start, TimeSpan end, string text)
    {
        Number = number;
        startText = SrtDocumentService.FormatTimestamp(start);
        endText = SrtDocumentService.FormatTimestamp(end);
        this.text = text;
    }

    public int Number { get; }
    public string StartText { get => startText; set => Set(ref startText, value); }
    public string EndText { get => endText; set => Set(ref endText, value); }
    public string Text { get => text; set => Set(ref text, value); }
    public string Preview => Text.Replace('\r', ' ').Replace('\n', ' ');
    public string IssueSummary { get => issueSummary; set => Set(ref issueSummary, value); }
    public bool HasIssue => !string.IsNullOrEmpty(IssueSummary);
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TryToCue(out SubtitleCue cue)
    {
        var validStart = SrtDocumentService.TryParseTimestamp(StartText, out var start);
        var validEnd = SrtDocumentService.TryParseTimestamp(EndText, out var end);
        cue = new SubtitleCue(Number, start, end, Text ?? string.Empty);
        return validStart && validEnd;
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(Text)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
    }
}

public sealed class SubtitleEditorViewModel : INotifyPropertyChanged
{
    private readonly SrtDocumentService service = new();
    private EditableSubtitleCue? selectedCue;
    private string status = "正在读取字幕……";
    private bool isDirty;

    public ObservableCollection<EditableSubtitleCue> Cues { get; } = [];
    public ObservableCollection<SubtitleCueIssue> Issues { get; } = [];
    public EditableSubtitleCue? SelectedCue { get => selectedCue; set { selectedCue = value; Notify(); Notify(nameof(HasSelection)); } }
    public bool HasSelection => SelectedCue is not null;
    public string Status { get => status; private set { status = value; Notify(); } }
    public bool IsDirty { get => isDirty; private set { isDirty = value; Notify(); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(string path)
    {
        var cues = await service.LoadAsync(path, CancellationToken.None);
        Cues.Clear();
        for (var index = 0; index < cues.Count; index++)
        {
            var row = new EditableSubtitleCue(index + 1, cues[index].Start, cues[index].End, cues[index].Text);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(EditableSubtitleCue.StartText) or nameof(EditableSubtitleCue.EndText) or nameof(EditableSubtitleCue.Text))
                    IsDirty = true;
            };
            Cues.Add(row);
        }
        SelectedCue = Cues.FirstOrDefault();
        IsDirty = false;
        Validate();
    }

    public bool Validate()
    {
        Issues.Clear();
        foreach (var cue in Cues) cue.IssueSummary = string.Empty;
        var parsed = new List<SubtitleCue>(Cues.Count);
        foreach (var row in Cues)
        {
            if (!row.TryToCue(out var cue))
            {
                var issue = new SubtitleCueIssue(row.Number, SubtitleIssueSeverity.Error, "invalid-time", "时间格式应为 00:00:00,000");
                Issues.Add(issue);
                row.IssueSummary = "错误：" + issue.Message;
            }
            else parsed.Add(cue);
        }
        if (parsed.Count == Cues.Count)
        {
            foreach (var issue in service.Validate(parsed)) Issues.Add(issue);
            foreach (var group in Issues.GroupBy(x => x.CueNumber))
                Cues[group.Key - 1].IssueSummary = string.Join("；", group.Select(x =>
                    (x.Severity == SubtitleIssueSeverity.Error ? "错误：" : "提示：") + x.Message));
        }
        var errors = Issues.Count(x => x.Severity == SubtitleIssueSeverity.Error);
        var warnings = Issues.Count - errors;
        Status = Issues.Count == 0 ? $"共 {Cues.Count} 条字幕，未发现问题。" :
            $"共 {Cues.Count} 条字幕：{errors} 个错误，{warnings} 个提示。";
        return errors == 0;
    }

    public async Task SaveAsync(string path)
    {
        if (!Validate()) throw new InvalidOperationException("字幕存在错误，请先根据红色提示修正。");
        var cues = Cues.Select(row => { row.TryToCue(out var cue); return cue; }).ToArray();
        await service.SaveAsync(path, cues, CancellationToken.None);
        IsDirty = false;
        Status = $"已保存 {Cues.Count} 条字幕：{path}";
    }

    public void SelectIssue(int direction)
    {
        if (Issues.Count == 0) { Validate(); if (Issues.Count == 0) return; }
        var current = SelectedCue?.Number ?? 0;
        var target = direction >= 0
            ? Issues.FirstOrDefault(x => x.CueNumber > current) ?? Issues.First()
            : Issues.LastOrDefault(x => x.CueNumber < current) ?? Issues.Last();
        SelectedCue = Cues.FirstOrDefault(x => x.Number == target.CueNumber);
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
