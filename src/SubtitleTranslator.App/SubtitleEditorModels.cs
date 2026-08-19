using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
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
    public string OriginalText
    {
        get { var lines = SplitLines(); return lines.Length > 1 ? lines[0] : string.Empty; }
        set { var lines = SplitLines(); Text = string.IsNullOrEmpty(TranslationText) ? value : value + Environment.NewLine + TranslationText; }
    }
    public string TranslationText
    {
        get { var lines = SplitLines(); return lines.Length > 1 ? string.Join(Environment.NewLine, lines.Skip(1)) : Text; }
        set { var original = OriginalText; Text = string.IsNullOrEmpty(original) ? value : original + Environment.NewLine + value; }
    }
    public string Preview => Text.Replace('\r', ' ').Replace('\n', ' ');
    public string IssueSummary { get => issueSummary; set => Set(ref issueSummary, value); }
    public bool HasIssue => !string.IsNullOrEmpty(IssueSummary);
    public string IssueStateDisplay => HasIssue ? IssueSummary : "未发现问题";
    public bool HasError { get; private set; }
    public bool HasWarning { get; private set; }
    public bool IsModified { get; private set; }
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
        if (name is nameof(StartText) or nameof(EndText) or nameof(Text))
        {
            IsModified = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        }
        if (name == nameof(Text))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationText)));
        }
    }

    public void SetIssues(IEnumerable<SubtitleCueIssue> issues)
    {
        var values = issues.ToArray();
        HasError = values.Any(x => x.Severity == SubtitleIssueSeverity.Error);
        HasWarning = values.Any(x => x.Severity == SubtitleIssueSeverity.Warning);
        IssueSummary = string.Join("；", values.Select(x =>
            (x.Severity == SubtitleIssueSeverity.Error ? "错误：" : "提示：") + x.Message));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIssue)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IssueStateDisplay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWarning)));
    }

    public void AcceptChanges()
    {
        IsModified = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
    }

    private string[] SplitLines() => (Text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
}

public sealed class SubtitleEditorViewModel : INotifyPropertyChanged
{
    private readonly SrtDocumentService service = new();
    private EditableSubtitleCue? selectedCue;
    private string status = "正在读取字幕……";
    private bool isDirty;
    private string issueFilter = "全部字幕";
    private string searchText = string.Empty;

    public SubtitleEditorViewModel()
    {
        CuesView = CollectionViewSource.GetDefaultView(Cues);
        CuesView.Filter = MatchesFilter;
    }

    public ObservableCollection<EditableSubtitleCue> Cues { get; } = [];
    public ICollectionView CuesView { get; }
    public ObservableCollection<SubtitleCueIssue> Issues { get; } = [];
    public IReadOnlyList<string> IssueFilters { get; } = ["全部字幕", "仅问题", "仅错误", "仅提示", "已修改"];
    public EditableSubtitleCue? SelectedCue { get => selectedCue; set { selectedCue = value; Notify(); Notify(nameof(HasSelection)); } }
    public bool HasSelection => SelectedCue is not null;
    public string Status { get => status; private set { status = value; Notify(); } }
    public bool IsDirty { get => isDirty; private set { isDirty = value; Notify(); Notify(nameof(DirtyStateDisplay)); } }
    public string DirtyStateDisplay => IsDirty ? "有未保存修改" : "所有修改已保存";
    public string IssueFilter { get => issueFilter; set { if (issueFilter == value) return; issueFilter = value; Notify(); RefreshFilter(); } }
    public string SearchText { get => searchText; set { if (searchText == value) return; searchText = value; Notify(); RefreshFilter(); } }
    public int TotalCueCount => Cues.Count;
    public int ErrorCount => Issues.Count(x => x.Severity == SubtitleIssueSeverity.Error);
    public int WarningCount => Issues.Count(x => x.Severity == SubtitleIssueSeverity.Warning);
    public int IssueCueCount => Cues.Count(x => x.HasIssue);
    public int ModifiedCount => Cues.Count(x => x.IsModified);
    public bool HasIssues => Issues.Count > 0;
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
                if (args.PropertyName == nameof(EditableSubtitleCue.IsModified))
                {
                    Notify(nameof(ModifiedCount));
                    if (IssueFilter == "已修改") RefreshFilter();
                }
            };
            Cues.Add(row);
        }
        SelectedCue = Cues.FirstOrDefault();
        foreach (var cue in Cues) cue.AcceptChanges();
        IsDirty = false;
        Validate();
    }

    public bool Validate()
    {
        Issues.Clear();
        foreach (var cue in Cues) cue.SetIssues([]);
        var parsed = new List<SubtitleCue>(Cues.Count);
        foreach (var row in Cues)
        {
            if (!row.TryToCue(out var cue))
            {
                var issue = new SubtitleCueIssue(row.Number, SubtitleIssueSeverity.Error, "invalid-time", "时间格式应为 00:00:00,000");
                Issues.Add(issue);
            }
            else parsed.Add(cue);
        }
        if (parsed.Count == Cues.Count)
        {
            foreach (var issue in service.Validate(parsed)) Issues.Add(issue);
            foreach (var group in Issues.GroupBy(x => x.CueNumber)) Cues[group.Key - 1].SetIssues(group);
        }
        else foreach (var group in Issues.GroupBy(x => x.CueNumber)) Cues[group.Key - 1].SetIssues(group);
        var errors = ErrorCount;
        var warnings = WarningCount;
        Status = Issues.Count == 0 ? $"共 {Cues.Count} 条字幕，未发现问题。" :
            $"共 {Cues.Count} 条字幕：{errors} 个错误，{warnings} 个提示。";
        NotifySummary();
        RefreshFilter();
        return errors == 0;
    }

    public async Task SaveAsync(string path)
    {
        if (!Validate()) throw new InvalidOperationException("字幕存在错误，请先根据红色提示修正。");
        var cues = Cues.Select(row => { row.TryToCue(out var cue); return cue; }).ToArray();
        await service.SaveAsync(path, cues, CancellationToken.None);
        foreach (var cue in Cues) cue.AcceptChanges();
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

    public EditableSubtitleCue? FindCueAt(TimeSpan position)
    {
        foreach (var cue in Cues)
        {
            if (!cue.TryToCue(out var parsed)) continue;
            if (parsed.Start <= position && position < parsed.End) return cue;
            if (parsed.Start > position) break;
        }
        return null;
    }

    private bool MatchesFilter(object value)
    {
        if (value is not EditableSubtitleCue cue) return false;
        var matchesText = string.IsNullOrWhiteSpace(SearchText) || cue.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || cue.Number.ToString().Contains(SearchText, StringComparison.Ordinal);
        var matchesIssue = IssueFilter switch
        {
            "仅问题" => cue.HasIssue,
            "仅错误" => cue.HasError,
            "仅提示" => cue.HasWarning,
            "已修改" => cue.IsModified,
            _ => true
        };
        return matchesText && matchesIssue;
    }

    private void RefreshFilter()
    {
        CuesView.Refresh();
        if (SelectedCue is not null && !CuesView.Cast<EditableSubtitleCue>().Contains(SelectedCue))
            SelectedCue = CuesView.Cast<EditableSubtitleCue>().FirstOrDefault();
    }

    private void NotifySummary()
    {
        Notify(nameof(TotalCueCount)); Notify(nameof(ErrorCount)); Notify(nameof(WarningCount));
        Notify(nameof(IssueCueCount)); Notify(nameof(ModifiedCount)); Notify(nameof(HasIssues));
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
