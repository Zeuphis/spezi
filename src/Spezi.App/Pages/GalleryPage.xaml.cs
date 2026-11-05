using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spezi.Core;
using Spezi.Core.Recording;

namespace Spezi.App;

public partial class GalleryPage : System.Windows.Controls.UserControl
{
    readonly AppController _controller;
    readonly ObservableCollection<ClipRow> _rows = [];
    bool _newestFirst = true;
    int _generation;
    readonly System.Windows.Threading.DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(200) };
    CancellationTokenSource? _scan;
    const int PageSize = 12;
    List<ClipRow> _all = [];
    List<ClipRow> _filtered = [];
    string? _gameFilter;
    string _query = "";
    int _page = 1;
    // Live refresh while the gallery is shown: file events are debounced into one rescan.
    readonly System.Windows.Threading.DispatcherTimer _liveDelay = new() { Interval = TimeSpan.FromMilliseconds(700) };
    FileSystemWatcher? _watcher;

    // Self-tests: every clip matching search + game filter, in display order (hero first).
    internal IReadOnlyList<ClipRow> FilteredRows => _filtered;

    public GalleryPage(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Clips.ItemsSource = _rows;
        Directory.CreateDirectory(ClipCache.Directory);
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); RefreshList(); };
        _liveDelay.Tick += (_, _) => { _liveDelay.Stop(); RefreshList(keepPage: true); };
        Loaded += (_, _) => StartWatcher();
        Unloaded += (_, _) =>
        {
            StopWatcher();
            _generation++;
            _searchDelay.Stop();
            _scan?.Cancel();
            foreach (var row in _rows) HideRow(row);
            if (Hero.DataContext is ClipRow hero) HideRow(hero);
        };
    }

    void StartWatcher()
    {
        StopWatcher();
        var directory = _controller.Settings.OutputDirectory;
        if (!Directory.Exists(directory)) return;
        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(QueueLiveRefresh);
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(QueueLiveRefresh);
            watcher.Error += (_, e) => AppLog.Error("Gallery folder watcher failed.", e.GetException());
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Gallery folder watcher could not start.", ex);
        }
    }

    void StopWatcher()
    {
        _liveDelay.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }

    void QueueLiveRefresh()
    {
        if (_watcher == null) return;
        _liveDelay.Stop();
        _liveDelay.Start();
    }

    // Called by the save pipeline (clip saved / recording stopped) while the gallery is open.
    public void NotifyLibraryChanged() { if (IsLoaded) QueueLiveRefresh(); else RefreshList(keepPage: true); }

    public void RefreshList(bool keepPage = false)
    {
        var generation = ++_generation;
        _searchDelay.Stop();
        _scan?.Cancel();
        _scan?.Dispose();
        _scan = new CancellationTokenSource();
        var cancellation = _scan.Token;
        var directory = _controller.Settings.OutputDirectory;
        var query = SearchBox.Text.Trim();
        var newestFirst = _newestFirst;
        if (IsLoaded && !string.Equals(_watcher?.Path, directory, StringComparison.OrdinalIgnoreCase)) StartWatcher();
        if (!keepPage) LibraryText.Text = "Loading clips\u2026";
        _ = PopulateAsync();
        async Task PopulateAsync()
        {
        List<ClipRow> result;
        try
        {
        result = await Task.Run(() =>
        {
            if (!Directory.Exists(directory))
                return new List<ClipRow>();
            var files = ClipLibrary.VideoFiles(directory)
                .Select(path => { cancellation.ThrowIfCancellationRequested(); return new FileInfo(path); })
                .Where(file => query.Length == 0 || Path.GetRelativePath(directory, file.FullName).Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.LastWriteTimeUtc);
            if (newestFirst)
                files = files.OrderByDescending(file => file.LastWriteTimeUtc);
            return files.Take(400).Select(file => new ClipRow(file, directory)).ToList();
        }, cancellation);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { AppLog.Error("Gallery scan failed.", ex); return; }
        // Resumes on the UI thread.
            {
                if (generation != _generation) return;
                _all = result;
                _query = query;
                if (!keepPage) _page = 1;
                if (_gameFilter != null && !_all.Any(row => row.Game.Equals(_gameFilter, StringComparison.OrdinalIgnoreCase))) _gameFilter = null;
                BuildChips();
                ApplyFilter();
                var games = _all.Select(row => row.Game).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                LibraryText.Text = _all.Count == 400 ? "Showing 400 clips" : query.Length > 0
                    ? $"{_all.Count} {(_all.Count == 1 ? "match" : "matches")}" : _all.Count == 0
                    ? "Your saved moments." : $"{_all.Count} {(_all.Count == 1 ? "clip" : "clips")} \u00B7 {games} {(games == 1 ? "game" : "games")}";
            }
        }
    }

    void BuildChips()
    {
        GameChips.Children.Clear();
        var groups = _all.GroupBy(row => row.Game, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Game: group.First().Game, Count: group.Count()))
            .OrderByDescending(group => group.Count).ThenBy(group => group.Game, StringComparer.OrdinalIgnoreCase).ToList();
        GameChips.Visibility = _all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_all.Count == 0) return;
        AddChip("All", null, _all.Count);
        foreach (var (game, count) in groups) AddChip(game, game, count);
        void AddChip(string label, string? game, int count)
        {
            var text = new TextBlock { FontSize = 11.5 };
            text.Inlines.Add(new System.Windows.Documents.Run(label) { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new System.Windows.Documents.Run("  " + count) { FontWeight = FontWeights.Normal });
            var chip = new System.Windows.Controls.RadioButton
            {
                Content = text, GroupName = "GalleryGame", Style = (Style)FindResource("FilterChip"),
                IsChecked = string.Equals(game, _gameFilter, StringComparison.OrdinalIgnoreCase)
            };
            chip.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"{label} ({count})");
            chip.Checked += (_, _) =>
            {
                if (string.Equals(game, _gameFilter, StringComparison.OrdinalIgnoreCase)) return;
                _gameFilter = game; _page = 1; ApplyFilter();
            };
            GameChips.Children.Add(chip);
        }
    }

    // Splits the filtered list into the latest-clip hero (page 1, All, no search) and 12-clip pages.
    void ApplyFilter()
    {
        _filtered = _gameFilter == null ? _all : _all.Where(row => row.Game.Equals(_gameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        var hasHero = _gameFilter == null && _query.Length == 0 && _newestFirst && _filtered.Count > 0;
        var rest = hasHero ? _filtered.Skip(1).ToList() : _filtered;
        var pages = Math.Max(1, (rest.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 1, pages);
        var showHero = hasHero && _page == 1;
        if (Hero.DataContext is ClipRow oldHero && !ReferenceEquals(oldHero, showHero ? _filtered[0] : null)) HideRow(oldHero);
        Hero.DataContext = showHero ? _filtered[0] : null;
        Hero.Visibility = showHero ? Visibility.Visible : Visibility.Collapsed;

        foreach (var row in _rows) HideRow(row);
        _rows.Clear();
        foreach (var row in rest.Skip((_page - 1) * PageSize).Take(PageSize)) _rows.Add(row);

        var empty = _filtered.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        GalleryScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        Clips.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = _query.Length == 0 ? "Your first highlight awaits" : "No matching clips";
        EmptyText.Text = _query.Length == 0
            ? "Save a clip while you play. Your moments will show up here."
            : "Try a different filename or game.";
        BuildPager(pages);
        GalleryScroll.ScrollToTop();
        // Don't rely on Loaded/DataContextChanged/IsVisibleChanged ordering for reused elements
        // (e.g. the hero after a delete): once layout settled, load every shown clip explicitly.
        var generation = _generation;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (generation != _generation || !IsVisible) return;
            if (Hero.Visibility == Visibility.Visible && Hero.DataContext is ClipRow hero) EnsureLoaded(hero);
            foreach (var row in _rows) EnsureLoaded(row);
        });
    }

    void EnsureLoaded(ClipRow row)
    {
        if (row.Path.Length == 0 || row.Thumbnail != null) return;
        row.Visible = true;
        _ = LoadDetailsAsync(row, _generation);
    }

    void BuildPager(int pages)
    {
        Pager.Children.Clear();
        Pager.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (pages <= 1) return;
        Add("\u2039", _page - 1, _page > 1, "Previous page");
        var shown = Enumerable.Range(1, pages).Where(n => n == 1 || n == pages || Math.Abs(n - _page) <= 1).ToList();
        var last = 0;
        foreach (var n in shown)
        {
            if (n - last > 1) Pager.Children.Add(new TextBlock { Text = "\u2026", Foreground = (System.Windows.Media.Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            Add(n.ToString(), n, true, $"Page {n}", n == _page);
            last = n;
        }
        Add("\u203A", _page + 1, _page < pages, "Next page");
        void Add(string label, int target, bool enabled, string name, bool current = false)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = new TextBlock { Text = label, FontSize = 12, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal },
                Style = (Style)FindResource("GalleryButton"), Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(3, 0, 3, 0),
                IsEnabled = enabled, Opacity = enabled ? 1 : 0.4, BorderThickness = new Thickness(current ? 1 : 0)
            };
            if (current)
            {
                button.SetResourceReference(BackgroundProperty, "AccentSoft");
                button.SetResourceReference(BorderBrushProperty, "Accent");
                button.SetResourceReference(ForegroundProperty, "AccentText");
            }
            else button.Background = System.Windows.Media.Brushes.Transparent;
            button.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, name);
            button.Click += (_, _) => { if (target == _page) return; _page = target; ApplyFilter(); };
            Pager.Children.Add(button);
        }
    }

    async Task LoadDetailsAsync(ClipRow row, int generation)
    {
        if (row.Loading || !row.Visible || row.Thumbnail != null) return;
        row.Loading = true;
        using var pending = new CancellationTokenSource();
        row.Pending = pending;
        try
        {
            var details = await ClipCache.GetDetailsAsync(row.Path, DecodeWidth(row), pending.Token);
            if (!row.Visible || pending.IsCancellationRequested) return;  // row objects are per scan; Visible alone says it is still shown
            row.DurationText = ClipText.FormatDuration(details.Duration);
            row.Thumbnail = details.Thumbnail;
        }
        catch (OperationCanceledException) { /* scrolled away or changed the search */ }
        catch (Exception ex) { AppLog.Error("Could not load gallery details: " + row.Path, ex); }
        finally
        {
            row.Pending = null;
            row.Loading = false;
            if (pending.IsCancellationRequested && row.Visible && generation == _generation) _ = LoadDetailsAsync(row, generation);
        }
    }

    // Hero shows ~620 DIP wide (source is 320 px anyway); grid tiles ~200 DIP.
    int DecodeWidth(ClipRow row)
    {
        var dip = ReferenceEquals(Hero.DataContext, row) ? 640 : 200;
        return (int)Math.Ceiling(dip * VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    void RowVisible(object sender, RoutedEventArgs e) => ShowRow(sender);
    void ShowRow(object sender)
    {
        if (sender is not FrameworkElement { IsVisible: true, DataContext: ClipRow row } || row.Path.Length == 0) return;
        row.Visible = true;
        _ = LoadDetailsAsync(row, _generation);
    }

    static void HideRow(ClipRow row) { row.Visible = false; row.Pending?.Cancel(); row.Thumbnail = null; }
    void RowCleanUp(object sender, CleanUpVirtualizedItemEventArgs e) { if (e.Value is ClipRow row) HideRow(row); }
    void RowHidden(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: ClipRow row }) HideRow(row); }
    void RowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) ShowRow(sender);
        else if (sender is FrameworkElement { DataContext: ClipRow row }) HideRow(row);
    }
    void RowContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ClipRow old) HideRow(old);
        ShowRow(sender);
    }
    void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholder != null && ClearSearchButton != null)
        {
            SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        _searchDelay.Stop(); _searchDelay.Start();
    }

    void ClearSearch(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }

    void ToggleSort(object sender, RoutedEventArgs e)
    {
        _newestFirst = !_newestFirst;
        SortLabel.Text = _newestFirst ? "Newest" : "Oldest";
        SortButton.ToolTip = _newestFirst ? "Newest first" : "Oldest first";
        SortButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, _newestFirst ? "Sort newest first" : "Sort oldest first");
        RefreshList();
    }

    void ShowClipMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    void OpenRoot(object sender, RoutedEventArgs e) => OpenFolder(_controller.Settings.OutputDirectory);

    void Play(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { Path.Length: > 0 } row)
            VideoLauncher.Open(row.Path, Window.GetWindow(this));
    }

    void Rename(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { Path.Length: > 0 } row)
            return;
        var next = Prompt.Ask(Window.GetWindow(this), "New filename", Path.GetFileNameWithoutExtension(row.Path));
        if (string.IsNullOrWhiteSpace(next))
            return;
        foreach (var invalid in Path.GetInvalidFileNameChars())
            next = next.Replace(invalid, '_');
        var destination = Path.Combine(Path.GetDirectoryName(row.Path)!, next + ".mp4");
        if (File.Exists(destination))
        {
            System.Windows.MessageBox.Show("A file with this name already exists.", "Spezi");
            return;
        }
        File.Move(row.Path, destination);
        RefreshList();
    }

    void OpenItem(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { Path.Length: > 0 } row)
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Path}\"") { UseShellExecute = true });
    }

    void Delete(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { Path.Length: > 0 } row)
            return;
        if (System.Windows.MessageBox.Show($"Delete “{row.Name}”?", "Spezi", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        File.Delete(row.Path);
        RefreshList();
    }

    static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    static ClipRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as ClipRow;

    public sealed class ClipRow : INotifyPropertyChanged
    {
        static readonly Regex GeneratedName = new(@"^.+_(?<kind>Clip|Recording|Session|Aufnahme|Recovered|Wiederhergestellt)_(?:\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}|\d{8}_\d{6})(?:[-_]\d{3})?(?:_[0-9a-f]{12,32})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        internal bool Visible;
        internal bool Loading;
        internal CancellationTokenSource? Pending;
        public string Path { get; }
        public string Name { get; }
        public string Game { get; } = "";
        public string DisplayName { get; }
        public string When { get; }
        public string SizeText { get; }
        public string TimeText { get; } = "";
        public string DetailText { get; } = "";
        string _duration = "…";
        ImageSource? _thumbnail;

        ClipRow(string name)
        {
            Path = "";
            Name = name;
            DisplayName = name;
            When = "";
            SizeText = "";
            _duration = "";
        }

        public ClipRow(System.IO.FileInfo file, string? root = null)
        {
            Path = file.FullName;
            Name = file.Name;
            var relative = root == null ? "." : System.IO.Path.GetRelativePath(root, file.DirectoryName!);
            Game = relative == "." ? "General" : relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)[0];
            if (Game == "Allgemein") Game = "General"; // Display legacy folders in English without moving files.
            When = file.LastWriteTime.ToString("MMM d, yyyy  HH:mm");
            var stem = System.IO.Path.GetFileNameWithoutExtension(Name);
            var generated = GeneratedName.Match(stem);
            var kind = generated.Groups["kind"].Value.ToLowerInvariant() switch
            {
                "clip" => "Replay",
                "recovered" or "wiederhergestellt" => "Recovered recording",
                _ => "Recording"
            };
            DisplayName = generated.Success ? $"{kind} · {file.LastWriteTime:HH:mm}" : stem;
            SizeText = file.Length < 1024 * 1024
                ? $"{file.Length / 1024.0:0} KB"
                : $"{file.Length / 1024.0 / 1024.0:0.0} MB";
            TimeText = file.LastWriteTime.Date == DateTime.Today ? file.LastWriteTime.ToString("HH:mm") : file.LastWriteTime.ToString("MMM d  HH:mm");
            DetailText = $"{Game} \u00B7 {file.LastWriteTime:MMM d, yyyy HH:mm} \u00B7 {SizeText}";
        }

        public static ClipRow Note(string text) => new(text);

        public string DurationText
        {
            get => _duration;
            set { _duration = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationText))); }
        }

        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
