using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace p3rpc.difficultyEditor.Configuration;

/// <summary>
/// The mod's configuration UI, shown instead of Reloaded's default property grid
/// (see <see cref="ConfiguratorMixin.TryRunCustomConfiguration"/>).
///
/// Built entirely in code (no XAML) on purpose: the configurator assembly is loaded by the launcher
/// into a collectible plugin AssemblyLoadContext, and XAML/BAML resource loading resolves the
/// assembly by name via pack URIs, which lands in the wrong load context. Constructing the controls
/// directly sidesteps that and keeps the window unload-friendly.
///
/// Layout is deliberately minimal: a values grid, plus one preset dropdown (auto-matches the current
/// values) with Save / Delete beside it. Everything is edited on an in-memory working copy; nothing
/// touches Config.json until "Save &amp; Close".
/// </summary>
internal sealed class PresetManagerWindow : Window
{
    private static readonly Brush Bg     = Frozen("#FF1E1E1E");
    private static readonly Brush Panel  = Frozen("#FF252526");
    private static readonly Brush Alt    = Frozen("#FF2A2A2D");
    private static readonly Brush Text   = Frozen("#FFEAEAEA");
    private static readonly Brush Sub    = Frozen("#FFB0B0B0");
    private static readonly Brush Edge   = Frozen("#FF3F3F46");
    private static readonly Brush Accent = Frozen("#FF4C7DF0");
    private static readonly Brush Chip   = Frozen("#FF333337");

    private static SolidColorBrush Frozen(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    private readonly Config _config;
    private readonly FieldRow[] _rows;
    private readonly FrameworkElement[] _rowContainers;
    private readonly List<UserPreset> _userPresets;                // working copy

    private readonly ObservableCollection<PresetOption> _options = new();
    private readonly PresetOption _customOption = PresetOption.Custom();
    private readonly ComboBox _combo;
    private readonly Button _deleteBtn;
    private readonly Button _restoreBtn;
    private readonly CheckBox _hideBurst;
    private readonly CheckBox _logToConsole;
    private bool _syncingCombo;

    // A minimal TextBox template (just a border + content host) so cells don't get the OS theme's
    // bottom accent line. Built once in the ctor (on the UI thread) and shared by every cell.
    private readonly ControlTemplate _flatBox = (ControlTemplate)XamlReader.Parse(
        "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"TextBox\">" +
        "<Border Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" " +
        "BorderThickness=\"{TemplateBinding BorderThickness}\" SnapsToDevicePixels=\"True\">" +
        "<ScrollViewer x:Name=\"PART_ContentHost\" Focusable=\"False\" Margin=\"{TemplateBinding Padding}\" " +
        "VerticalAlignment=\"Center\" HorizontalScrollBarVisibility=\"Hidden\" VerticalScrollBarVisibility=\"Hidden\"/>" +
        "</Border></ControlTemplate>");

    /// <summary>Entry point: show the editor modally for the given config (on the WPF UI thread).</summary>
    public static void Edit(Config config)
    {
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => Edit(config));
            return;
        }

        var win = new PresetManagerWindow(config);
        try
        {
            if (app?.MainWindow != null && app.MainWindow.IsLoaded && app.MainWindow != win)
                win.Owner = app.MainWindow;
        }
        catch { /* owner is best-effort */ }
        win.ShowDialog();
    }

    private PresetManagerWindow(Config config)
    {
        _config = config;
        _userPresets = config.CustomPresets
            .Select(p => new UserPreset { Name = p.Name, Values = new Dictionary<string, double>(p.Values) })
            .ToList();

        // First-run: seed the bundled presets into the editable list (unless already seeded). They then
        // behave exactly like user presets — deletable, etc. Persisted (with the flag) on Save & Close.
        if (!config.SeededDefaultPresets)
            foreach (var d in Presets.DefaultPresets)
                if (!_userPresets.Any(u => string.Equals(u.Name, d.Name, StringComparison.OrdinalIgnoreCase)))
                    _userPresets.Add(new UserPreset { Name = d.Name, Values = Presets.ToValues(d.Table) });

        Title = "P3R Custom Difficulty Editor";
        Background = Bg;
        Foreground = Text;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 480;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        // build field rows from the config's current values
        var values = config.GetValues();
        _rows = new FieldRow[Presets.Fields.Length];
        _rowContainers = new FrameworkElement[Presets.Fields.Length];
        for (int f = 0; f < Presets.Fields.Length; f++)
        {
            var row = new FieldRow(f);
            for (int r = 0; r < Presets.Rows.Length; r++)
                row.SetCell(r, values[Presets.Key(f, r)]);
            row.PropertyChanged += (_, __) => UpdateMatch();
            _rows[f] = row;
        }

        _combo = new ComboBox
        {
            MinWidth = 168,
            Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = Text,
            Background = Chip,
            BorderBrush = Edge,
            DisplayMemberPath = nameof(PresetOption.Name),
            ItemsSource = _options,
            ItemContainerStyle = ComboItemStyle(),
        };
        _combo.SelectionChanged += OnComboChanged;

        _deleteBtn = MakeButton("Delete", OnDelete);
        _restoreBtn = MakeButton("Restore defaults", OnRestore);
        _restoreBtn.Visibility = Visibility.Collapsed;   // only shown when a seeded default is missing
        _hideBurst = MakeCheck("Hide weakness/crit rows", config.HideBurstFields);
        _logToConsole = MakeCheck("Log baked table to console (debug)", config.LogToConsole);
        _hideBurst.Checked += (_, __) => RefreshRowFilter();
        _hideBurst.Unchecked += (_, __) => RefreshRowFilter();

        Content = BuildLayout();

        RebuildOptions();
        RefreshRowFilter();
        UpdateMatch();
        UpdateRestoreButton();
    }

    // ---- layout ---------------------------------------------------------
    private UIElement BuildLayout()
    {
        var root = new StackPanel { Margin = new Thickness(14) };

        // row 1: preset dropdown + save/delete
        var presetRow = new StackPanel { Orientation = Orientation.Horizontal };
        presetRow.Children.Add(new TextBlock { Text = "Preset:", Foreground = Text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        presetRow.Children.Add(_combo);
        presetRow.Children.Add(MakeButton("Save as…", OnSaveAsNew));
        presetRow.Children.Add(_deleteBtn);
        presetRow.Children.Add(_restoreBtn);
        root.Children.Add(presetRow);

        // row 2: toggles
        var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        toggleRow.Children.Add(_hideBurst);
        _logToConsole.Margin = new Thickness(20, 0, 0, 0);
        toggleRow.Children.Add(_logToConsole);
        root.Children.Add(toggleRow);

        // values grid
        var card = new Border
        {
            Background = Panel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 12, 0, 0),
            Child = BuildValueGrid(),
        };
        root.Children.Add(card);

        // bottom bar
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var hint = new TextBlock { Text = "Changes take effect on the next game launch.", Foreground = Sub, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(hint, Dock.Left);
        bottom.Children.Add(hint);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = MakeButton("Save & Close", OnSave);
        save.Background = Accent;
        save.MinWidth = 110;
        actions.Children.Add(save);
        actions.Children.Add(MakeButton("Cancel", (_, __) => { DialogResult = false; Close(); }));
        bottom.Children.Add(actions);
        root.Children.Add(bottom);

        return root;
    }

    private FrameworkElement BuildValueGrid()
    {
        var scope = new StackPanel { Margin = new Thickness(1) };
        Grid.SetIsSharedSizeScope(scope, true);

        // header
        var header = MakeRowGrid();
        AddCell(header, 0, new TextBlock { Text = "Difficulty setting", Foreground = Text, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 7, 12, 7), VerticalAlignment = VerticalAlignment.Center });
        for (int r = 0; r < Presets.Rows.Length; r++)
            AddCell(header, r + 1, new TextBlock { Text = Presets.Rows[r], Foreground = Text, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center });
        scope.Children.Add(new Border { Background = Chip, BorderBrush = Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = header });

        // field rows
        for (int f = 0; f < _rows.Length; f++)
        {
            var row = _rows[f];
            var g = MakeRowGrid();
            g.DataContext = row;
            AddCell(g, 0, new TextBlock { Text = row.Field, Foreground = Text, Margin = new Thickness(10, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            for (int r = 0; r < Presets.Rows.Length; r++)
                AddCell(g, r + 1, MakeCellBox(r));

            var container = new Border
            {
                Child = g,
                Background = f % 2 == 1 ? Alt : Brushes.Transparent,
                BorderBrush = Edge,
                BorderThickness = new Thickness(0, 0, 0, f == _rows.Length - 1 ? 0 : 1),
            };
            _rowContainers[f] = container;
            scope.Children.Add(container);
        }

        return scope;
    }

    private static Grid MakeRowGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "label", MinWidth = 150 });
        for (int r = 0; r < Presets.Rows.Length; r++)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        return g;
    }

    private static void AddCell(Grid g, int col, UIElement e)
    {
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    private TextBox MakeCellBox(int row)
    {
        var tb = new TextBox
        {
            Template = _flatBox,
            Background = Brushes.Transparent,
            Foreground = Text,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CaretBrush = Text,
            TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 5, 8, 5),
            MinWidth = 0,
        };
        tb.SetBinding(TextBox.TextProperty, new Binding($"Cell{row}")
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        // One click = whole value selected, ready to type. Without this, WPF places the caret at the
        // click point and cancels the select-all; handling the first mouse-down (when not yet focused)
        // suppresses that. A second click in an already-focused cell still places the caret to edit.
        tb.PreviewMouseLeftButtonDown += (s, e) =>
        {
            var t = (TextBox)s;
            if (!t.IsKeyboardFocusWithin) { t.Focus(); e.Handled = true; }
        };
        tb.GotKeyboardFocus += (s, _) => { var t = (TextBox)s; t.BorderBrush = Accent; t.SelectAll(); };
        tb.LostKeyboardFocus += (s, _) => ((TextBox)s).BorderBrush = Brushes.Transparent;
        return tb;
    }

    private Button MakeButton(string content, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = content,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5),
            Background = Chip,
            Foreground = Text,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.Click += onClick;
        return b;
    }

    private CheckBox MakeCheck(string content, bool isChecked) => new()
    {
        Content = content,
        Foreground = Text,
        IsChecked = isChecked,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Style ComboItemStyle()
    {
        var s = new Style(typeof(ComboBoxItem));
        s.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        s.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        s.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        return s;
    }

    // ---- values <-> working copy ---------------------------------------
    private Dictionary<string, double> Snapshot()
    {
        var d = new Dictionary<string, double>(50);
        foreach (var row in _rows)
            for (int r = 0; r < Presets.Rows.Length; r++)
                d[Presets.Key(row.FieldIndex, r)] = row.GetCell(r);
        return d;
    }

    private void LoadValues(IReadOnlyDictionary<string, double> values)
    {
        foreach (var row in _rows)
            for (int r = 0; r < Presets.Rows.Length; r++)
                if (values.TryGetValue(Presets.Key(row.FieldIndex, r), out var v))
                    row.SetCell(r, v);
    }

    // ---- preset dropdown -----------------------------------------------
    private void RebuildOptions()
    {
        _options.Clear();
        _options.Add(_customOption);
        _options.Add(PresetOption.Protected("Vanilla", Presets.Vanilla));
        foreach (var u in _userPresets) _options.Add(PresetOption.Of(u));
    }

    private void UpdateMatch()
    {
        var snap = Snapshot();
        var match = _options.FirstOrDefault(o => o.MatchesValues(snap)) ?? _customOption;
        _syncingCombo = true;
        _combo.SelectedItem = match;
        _syncingCombo = false;
        _deleteBtn.IsEnabled = match.IsDeletable;
    }

    private void OnComboChanged(object? s, SelectionChangedEventArgs e)
    {
        if (_syncingCombo) return;
        if (_combo.SelectedItem is PresetOption opt && !opt.IsCustom)
            LoadValues(opt.Values());      // applying fires PropertyChanged -> UpdateMatch re-confirms selection
        _deleteBtn.IsEnabled = _combo.SelectedItem is PresetOption { IsDeletable: true };
    }

    private bool NameTaken(string name, UserPreset? except = null) =>
        _userPresets.Any(u => u != except && string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));

    private void OnSaveAsNew(object? s, RoutedEventArgs e)
    {
        var name = PromptForName(this, "Save preset", "Name for this preset:", SuggestName());
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        if (NameTaken(name))
        {
            if (!Confirm("Preset exists", $"A preset named “{name}” already exists.\nOverwrite it with the current values?"))
                return;
            _userPresets.First(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)).Values = Snapshot();
        }
        else
        {
            _userPresets.Add(new UserPreset { Name = name, Values = Snapshot() });
        }

        RebuildOptions();
        UpdateMatch();   // values match the just-saved preset, so the dropdown selects it
        UpdateRestoreButton();
    }

    private void OnDelete(object? s, RoutedEventArgs e)
    {
        if (_combo.SelectedItem is not PresetOption { IsDeletable: true, User: { } u }) return;
        if (!Confirm("Delete preset", $"Delete the preset “{u.Name}”?")) return;
        _userPresets.Remove(u);
        RebuildOptions();
        UpdateMatch();
        UpdateRestoreButton();
    }

    private IEnumerable<Presets.NamedPreset> MissingDefaults() =>
        Presets.DefaultPresets.Where(d => !_userPresets.Any(u => string.Equals(u.Name, d.Name, StringComparison.OrdinalIgnoreCase)));

    private void UpdateRestoreButton() =>
        _restoreBtn.Visibility = MissingDefaults().Any() ? Visibility.Visible : Visibility.Collapsed;

    private void OnRestore(object? s, RoutedEventArgs e)
    {
        foreach (var d in MissingDefaults().ToList())
            _userPresets.Add(new UserPreset { Name = d.Name, Values = Presets.ToValues(d.Table) });
        RebuildOptions();
        UpdateMatch();
        UpdateRestoreButton();
    }

    private void RefreshRowFilter()
    {
        bool hide = _hideBurst.IsChecked == true;
        for (int f = 0; f < _rows.Length; f++)
            if (_rows[f].IsBurst)
                _rowContainers[f].Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSave(object? s, RoutedEventArgs e)
    {
        _config.SetValues(Snapshot());
        _config.CustomPresets = _userPresets;
        _config.SeededDefaultPresets = true;   // we've shown (and persisted) the defaults; don't re-seed
        _config.HideBurstFields = _hideBurst.IsChecked == true;
        _config.LogToConsole = _logToConsole.IsChecked == true;
        _config.Save?.Invoke();

        DialogResult = true;
        Close();
    }

    private string SuggestName()
    {
        for (int i = 1; ; i++)
        {
            var candidate = $"My Preset {i}";
            if (!NameTaken(candidate)) return candidate;
        }
    }

    // ---- themed modal dialogs (no XAML, no system MessageBox) ----------
    private bool Confirm(string title, string message)
    {
        var dlg = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Bg,
            Foreground = Text,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            MinWidth = 300,
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = message, Foreground = Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 0, 0, 14) });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bool result = false;
        var yes = MakeButton("Yes", (_, __) => { result = true; dlg.DialogResult = true; });
        yes.IsDefault = true;
        yes.Background = Accent;
        yes.MinWidth = 72;
        var no = MakeButton("No", (_, __) => { dlg.DialogResult = false; });
        no.IsCancel = true;
        no.MinWidth = 72;
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        panel.Children.Add(buttons);

        dlg.Content = panel;
        dlg.ShowDialog();
        return result;
    }

    private string? PromptForName(Window owner, string title, string prompt, string initial)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = Bg,
            Foreground = Text,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = prompt, Foreground = Text, Margin = new Thickness(0, 0, 0, 8) });

        var box = new TextBox
        {
            Text = initial,
            Background = Panel,
            Foreground = Text,
            BorderBrush = Edge,
            CaretBrush = Text,
            Padding = new Thickness(4, 4, 4, 4),
        };
        panel.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        string? result = null;
        var ok = MakeButton("OK", (_, __) => { result = box.Text; dlg.DialogResult = true; });
        ok.IsDefault = true;
        var cancel = MakeButton("Cancel", (_, __) => { dlg.DialogResult = false; });
        cancel.IsCancel = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        dlg.Content = panel;
        dlg.Loaded += (_, __) => { box.Focus(); box.SelectAll(); };
        return dlg.ShowDialog() == true ? result : null;
    }

    /// <summary>One field (e.g. "Damage You Deal") across the 5 difficulties; bindable cells for the grid.</summary>
    private sealed class FieldRow : INotifyPropertyChanged
    {
        public int FieldIndex { get; }
        public string Field { get; }
        public bool IsBurst { get; }
        private readonly double[] _cells = new double[5];

        public FieldRow(int fieldIndex)
        {
            FieldIndex = fieldIndex;
            Field = Presets.FieldLabels[fieldIndex];
            IsBurst = Presets.IsBurstField(fieldIndex);
        }

        public double GetCell(int row) => _cells[row];
        public void SetCell(int row, double value)
        {
            if (_cells[row].Equals(value)) return;
            _cells[row] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Cell{row}"));
        }

        public double Cell0 { get => _cells[0]; set => SetCell(0, value); }
        public double Cell1 { get => _cells[1]; set => SetCell(1, value); }
        public double Cell2 { get => _cells[2]; set => SetCell(2, value); }
        public double Cell3 { get => _cells[3]; set => SetCell(3, value); }
        public double Cell4 { get => _cells[4]; set => SetCell(4, value); }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>An entry in the preset dropdown: the "Custom" sentinel, protected Vanilla, or a user preset.</summary>
    private sealed class PresetOption
    {
        public string Name { get; }
        public bool IsCustom { get; }       // the "Custom (unsaved)" sentinel
        public UserPreset? User { get; }    // a deletable user/seeded preset
        private readonly double[][]? _table; // a code-defined (protected) preset, e.g. Vanilla

        private PresetOption(string name, bool custom, double[][]? table, UserPreset? user)
        {
            Name = name; IsCustom = custom; _table = table; User = user;
        }

        public static PresetOption Custom() => new("Custom (unsaved)", true, null, null);
        public static PresetOption Protected(string name, double[][] table) => new(name, false, table, null);
        public static PresetOption Of(UserPreset u) => new(u.Name, false, null, u);

        /// <summary>Only user/seeded presets can be deleted (not Custom, not Vanilla).</summary>
        public bool IsDeletable => User != null;

        public bool MatchesValues(IReadOnlyDictionary<string, double> v) =>
            IsCustom ? false
            : _table != null ? Presets.Matches(v, _table)
            : Presets.ValuesEqual(v, User!.Values);

        public Dictionary<string, double> Values() =>
            _table != null ? Presets.ToValues(_table) : new Dictionary<string, double>(User!.Values);
    }
}
