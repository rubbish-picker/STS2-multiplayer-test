using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace ModTheSpire;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true;
    }
}

internal sealed class MainForm : Form
{
    private readonly ComboBox _accountComboBox;
    private readonly Label _settingsPathLabel;
    private readonly Label _modsPathLabel;
    private readonly BufferedListView _modListView;
    private readonly CheckBox _favoritesFirstCheckBox;
    private readonly Button _refreshButton;
    private readonly Button _updateModsButton;
    private readonly Button _pushRemoteButton;
    private readonly Button _deleteAccountButton;
    private readonly Button _selectAllButton;
    private readonly Button _selectNoneButton;
    private readonly Button _showDependencyGraphButton;
    private readonly Button _saveButton;
    private readonly Button _launchButton;
    private readonly Button _saveAndLaunchButton;
    private readonly CheckBox _updateBeforeLaunchCheckBox;
    private readonly Label _statusLabel;
    private readonly bool _canPushRemote;
    private bool _isApplyingModSelection;
    private bool _isFavoriteClickInProgress;

    private LauncherState _state = LauncherState.Empty;

    public MainForm()
    {
        _canPushRemote = string.Equals(Environment.UserName, "27940", StringComparison.OrdinalIgnoreCase);
        Text = "ModTheSpire";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 640);
        Size = new Size(1100, 760);
        ApplyLauncherIcon();

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        FlowLayoutPanel topBar = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
        };
        root.Controls.Add(topBar, 0, 0);

        topBar.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0),
            Text = "账号目录",
        });

        _accountComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 260,
        };
        _accountComboBox.SelectedIndexChanged += (_, _) => OnAccountChanged();
        topBar.Controls.Add(_accountComboBox);

        _refreshButton = new Button
        {
            AutoSize = true,
            Text = "刷新",
        };
        _refreshButton.Click += (_, _) => ReloadState();
        topBar.Controls.Add(_refreshButton);

        _updateModsButton = new Button
        {
            AutoSize = true,
            Text = "更新 Mod",
        };
        _updateModsButton.Click += async (_, _) => await UpdateModsAsync(showPopupWhenFinished: true);
        topBar.Controls.Add(_updateModsButton);

        _pushRemoteButton = new Button
        {
            AutoSize = true,
            Text = "推送到远程",
            Enabled = _canPushRemote,
        };
        _pushRemoteButton.Click += async (_, _) => await PushRemoteAsync();
        topBar.Controls.Add(_pushRemoteButton);

        _deleteAccountButton = new Button
        {
            AutoSize = true,
            Text = "删除账号存档",
        };
        _deleteAccountButton.Click += (_, _) => DeleteSelectedAccount();
        topBar.Controls.Add(_deleteAccountButton);

        _updateBeforeLaunchCheckBox = new CheckBox
        {
            AutoSize = true,
            Margin = new Padding(16, 8, 0, 0),
            Text = "启动前先更新 Mod",
        };
        _updateBeforeLaunchCheckBox.Checked = false;
        topBar.Controls.Add(_updateBeforeLaunchCheckBox);

        TableLayoutPanel infoPanel = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 10, 0, 10),
        };
        root.Controls.Add(infoPanel, 0, 1);

        infoPanel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "启动前选择要启用的 Mod。保存后下次启动立即生效。",
        });

        _settingsPathLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1040, 0),
            Margin = new Padding(0, 6, 0, 0),
        };
        infoPanel.Controls.Add(_settingsPathLabel);

        _modsPathLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1040, 0),
            Margin = new Padding(0, 4, 0, 0),
        };
        infoPanel.Controls.Add(_modsPathLabel);

        _modListView = new BufferedListView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            FullRowSelect = true,
            View = View.Details,
        };
        _modListView.ItemChecked += (_, _) => OnModItemChecked();
        _modListView.MouseDown += ModListView_MouseDown;
        _modListView.MouseUp += ModListView_MouseUp;
        _modListView.Columns.Add("启用", 70);
        _modListView.Columns.Add("收藏", 60);
        _modListView.Columns.Add("名称", 260);
        _modListView.Columns.Add("ID", 220);
        _modListView.Columns.Add("作者", 160);
        _modListView.Columns.Add("依赖", 220);
        _modListView.Columns.Add("状态", 220);
        _modListView.Columns.Add("位置", 320);
        root.Controls.Add(_modListView, 0, 2);

        FlowLayoutPanel actionBar = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
        };
        root.Controls.Add(actionBar, 0, 3);

        _selectAllButton = new Button
        {
            AutoSize = true,
            Text = "全选",
        };
        _selectAllButton.Click += (_, _) => SetAllChecked(true);
        actionBar.Controls.Add(_selectAllButton);

        _selectNoneButton = new Button
        {
            AutoSize = true,
            Text = "全不选",
        };
        _selectNoneButton.Click += (_, _) => SetAllChecked(false);
        actionBar.Controls.Add(_selectNoneButton);

        _favoritesFirstCheckBox = new CheckBox
        {
            AutoSize = true,
            Margin = new Padding(18, 8, 0, 0),
            Text = "收藏置顶",
        };
        _favoritesFirstCheckBox.CheckedChanged += FavoritesFirstCheckBox_CheckedChanged;
        actionBar.Controls.Add(_favoritesFirstCheckBox);

        _showDependencyGraphButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(12, 3, 3, 3),
            Text = "依赖关系图",
        };
        _showDependencyGraphButton.Click += (_, _) => ShowDependencyGraph();
        actionBar.Controls.Add(_showDependencyGraphButton);

        _saveButton = new Button
        {
            AutoSize = true,
            Margin = new Padding(18, 3, 3, 3),
            Text = "只保存",
        };
        _saveButton.Click += (_, _) => SaveSelection(showMessage: false);
        actionBar.Controls.Add(_saveButton);

        _launchButton = new Button
        {
            AutoSize = true,
            Text = "直接启动游戏",
        };
        _launchButton.Click += async (_, _) => await LaunchWorkflowAsync(saveBeforeLaunch: false);
        actionBar.Controls.Add(_launchButton);

        _saveAndLaunchButton = new Button
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "保存并启动",
        };
        _saveAndLaunchButton.Click += (_, _) =>
        {
            _ = LaunchWorkflowAsync(saveBeforeLaunch: true);
        };
        actionBar.Controls.Add(_saveAndLaunchButton);

        _statusLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
        };
        root.Controls.Add(_statusLabel, 0, 4);

        ReloadState();
    }

    private void ReloadState()
    {
        try
        {
            _state = LauncherState.Load();
            RebindAccounts();
            RebindMods();
            _favoritesFirstCheckBox.CheckedChanged -= FavoritesFirstCheckBox_CheckedChanged;
            _favoritesFirstCheckBox.Checked = _state.Preferences.FavoritesFirst;
            _favoritesFirstCheckBox.CheckedChanged += FavoritesFirstCheckBox_CheckedChanged;
            SetStatus($"已发现 {_state.AvailableMods.Count} 个本地 mod。");
        }
        catch (Exception ex)
        {
            SetStatus($"加载失败: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.ToString(), "ModTheSpire 加载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RebindAccounts()
    {
        string? previousPath = (_accountComboBox.SelectedItem as SettingsCandidate)?.SettingsPath;
        _accountComboBox.BeginUpdate();
        _accountComboBox.Items.Clear();
        foreach (SettingsCandidate candidate in _state.SettingsCandidates)
        {
            _accountComboBox.Items.Add(candidate);
        }

        if (_accountComboBox.Items.Count == 0)
        {
            _accountComboBox.EndUpdate();
            UpdatePathLabels();
            return;
        }

        int index = 0;
        if (!string.IsNullOrWhiteSpace(previousPath))
        {
            int found = _state.SettingsCandidates.FindIndex(item =>
                string.Equals(item.SettingsPath, previousPath, StringComparison.OrdinalIgnoreCase));
            if (found >= 0)
            {
                index = found;
            }
        }
        else if (_state.SelectedCandidateIndex >= 0 && _state.SelectedCandidateIndex < _state.SettingsCandidates.Count)
        {
            index = _state.SelectedCandidateIndex;
        }

        _accountComboBox.SelectedIndex = index;
        _accountComboBox.EndUpdate();
    }

    private void RebindMods()
    {
        _modListView.BeginUpdate();
        _modListView.Items.Clear();
        foreach (InstalledMod mod in GetOrderedMods(_state.AvailableMods))
        {
            ListViewItem item = new("")
            {
                Checked = mod.IsEnabled,
                Tag = mod,
            };
            item.SubItems.Add(mod.IsFavorite ? "★" : "");
            item.SubItems.Add(mod.Name);
            item.SubItems.Add(mod.Id);
            item.SubItems.Add(mod.Author);
            item.SubItems.Add(mod.Dependencies.Count == 0 ? "-" : string.Join(", ", mod.Dependencies));
            item.SubItems.Add("");
            item.SubItems.Add(mod.ManifestPath);
            _modListView.Items.Add(item);
        }
        RefreshDependencyPresentation();
        _modListView.EndUpdate();
        UpdatePathLabels();
    }

    private void OnAccountChanged()
    {
        if (_accountComboBox.SelectedItem is not SettingsCandidate candidate)
        {
            return;
        }

        _state = _state.WithSelectedCandidate(candidate.SettingsPath);
        RebindMods();
        SetStatus($"当前账号目录: {candidate.DisplayName}");
    }

    private void UpdatePathLabels()
    {
        _settingsPathLabel.Text = _state.SelectedCandidate is null
            ? "设置文件: 未找到 settings.save"
            : $"设置文件: {_state.SelectedCandidate.SettingsPath}";
        _modsPathLabel.Text = $"游戏 mods 目录: {_state.ModsDirectory}";
    }

    private void FavoritesFirstCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        OnFavoritesFirstChanged();
    }

    private void ModListView_MouseDown(object? sender, MouseEventArgs e)
    {
        _isFavoriteClickInProgress = IsFavoriteCellHit(e.Location);
        if (_isFavoriteClickInProgress)
        {
            _modListView.SelectedItems.Clear();
        }
    }

    private void ModListView_MouseUp(object? sender, MouseEventArgs e)
    {
        ListViewHitTestInfo hit = _modListView.HitTest(e.Location);
        if (hit.Item?.Tag is not InstalledMod mod || hit.SubItem is null)
        {
            _isFavoriteClickInProgress = false;
            return;
        }

        int subItemIndex = hit.Item.SubItems.IndexOf(hit.SubItem);
        if (subItemIndex != 1)
        {
            _isFavoriteClickInProgress = false;
            return;
        }

        ToggleFavorite(mod.Id, hit.Item);
        _modListView.SelectedItems.Clear();
        BeginInvoke(new Action(() =>
        {
            _modListView.SelectedItems.Clear();
            _isFavoriteClickInProgress = false;
        }));
    }

    private bool IsFavoriteCellHit(Point location)
    {
        ListViewHitTestInfo hit = _modListView.HitTest(location);
        return hit.Item is not null
            && hit.SubItem is not null
            && hit.Item.SubItems.IndexOf(hit.SubItem) == 1;
    }

    private void OnFavoritesFirstChanged()
    {
        if (_state.Preferences.FavoritesFirst == _favoritesFirstCheckBox.Checked)
        {
            return;
        }

        LauncherPreferences updatedPreferences = _state.Preferences with
        {
            FavoritesFirst = _favoritesFirstCheckBox.Checked,
        };
        updatedPreferences.Save(_state.GameDirectory);
        _state = _state.WithPreferences(updatedPreferences);
        RebindMods();
        SetStatus(_favoritesFirstCheckBox.Checked ? "已开启收藏置顶。" : "已关闭收藏置顶。");
    }

    private List<InstalledMod> GetOrderedMods(IEnumerable<InstalledMod> mods)
    {
        IEnumerable<InstalledMod> ordered = mods;
        if (_state.Preferences.FavoritesFirst)
        {
            ordered = ordered
                .OrderByDescending(mod => mod.IsFavorite)
                .ThenByDescending(mod => string.Equals(mod.Id, "BaseLib", StringComparison.OrdinalIgnoreCase))
                .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase);
        }

        return ordered.ToList();
    }

    private void ToggleFavorite(string modId, ListViewItem? existingItem = null)
    {
        LauncherPreferences updatedPreferences = _state.Preferences.ToggleFavorite(modId);
        bool isFavorite = updatedPreferences.FavoriteModIds.Contains(modId);
        updatedPreferences.Save(_state.GameDirectory);

        List<InstalledMod> updatedMods = _state.AvailableMods
            .Select(mod => string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase)
                ? mod with { IsFavorite = isFavorite }
                : mod)
            .ToList();
        InstalledMod toggledMod = updatedMods.First(mod => string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase));
        _state = _state.WithPreferences(updatedPreferences).WithUpdatedMods(updatedMods);

        if (existingItem is not null)
        {
            _modListView.BeginUpdate();
            try
            {
                existingItem.Tag = toggledMod;
                UpdateFavoriteCell(existingItem, toggledMod);

                if (isFavorite && _state.Preferences.FavoritesFirst)
                {
                    MoveItemToSortedPosition(existingItem, toggledMod);
                }
            }
            finally
            {
                _modListView.EndUpdate();
            }
        }

        SetStatus(isFavorite
            ? $"已收藏 {toggledMod.Name}。"
            : $"已取消收藏 {toggledMod.Name}。");
    }

    private static void UpdateFavoriteCell(ListViewItem item, InstalledMod mod)
    {
        item.SubItems[1].Text = mod.IsFavorite ? "★" : "";
    }

    private void MoveItemToSortedPosition(ListViewItem item, InstalledMod mod)
    {
        int currentIndex = item.Index;
        List<InstalledMod> orderedMods = GetOrderedMods(_state.AvailableMods);
        int targetIndex = orderedMods.FindIndex(candidate => string.Equals(candidate.Id, mod.Id, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0 || targetIndex == currentIndex)
        {
            return;
        }

        _modListView.Items.RemoveAt(currentIndex);
        _modListView.Items.Insert(targetIndex, item);
        item.Selected = false;
        item.Focused = false;
        item.EnsureVisible();
    }

    private void OnModItemChecked()
    {
        if (_isApplyingModSelection || _isFavoriteClickInProgress)
        {
            return;
        }

        BeginInvoke(new Action(ResolveSelectionAfterUserChange));
    }

    private void ResolveSelectionAfterUserChange()
    {
        if (_isApplyingModSelection)
        {
            return;
        }

        Dictionary<string, InstalledMod> modMap = _state.AvailableMods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (ListViewItem item in _modListView.Items)
        {
            if (item.Tag is InstalledMod mod && item.Checked)
            {
                selectedIds.Add(mod.Id);
            }
        }

        HashSet<string> missingDependencies = GetMissingDependencies(selectedIds, modMap);
        HashSet<string> autoSelected = ResolveEnabledIdsWithDependencies(selectedIds, modMap);
        ApplyResolvedSelection(autoSelected, missingDependencies);
    }

    private void ApplyResolvedSelection(HashSet<string> selectedIds, HashSet<string>? missingDependencies = null, string? statusMessage = null)
    {
        Dictionary<string, InstalledMod> modMap = _state.AvailableMods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        HashSet<string> requiredIds = GetRequiredDependencyIds(selectedIds, modMap);
        missingDependencies ??= GetMissingDependencies(selectedIds, modMap);
        bool selectionChanged = _state.AvailableMods.Any(mod => mod.IsEnabled != selectedIds.Contains(mod.Id));

        _modListView.BeginUpdate();
        try
        {
            _isApplyingModSelection = true;
            if (selectionChanged)
            {
                foreach (ListViewItem item in _modListView.Items)
                {
                    if (item.Tag is not InstalledMod mod)
                    {
                        continue;
                    }

                    bool shouldEnable = selectedIds.Contains(mod.Id);
                    if (item.Checked != shouldEnable)
                    {
                        item.Checked = shouldEnable;
                    }
                }
            }

            List<InstalledMod> updatedMods = selectionChanged
                ? _state.AvailableMods
                    .Select(mod => mod with { IsEnabled = selectedIds.Contains(mod.Id) })
                    .ToList()
                : _state.AvailableMods;
            _state = _state.WithUpdatedMods(updatedMods);
            RefreshDependencyPresentation(requiredIds, missingDependencies);
        }
        finally
        {
            _isApplyingModSelection = false;
            _modListView.EndUpdate();
        }

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            SetStatus(statusMessage);
        }
        else if (missingDependencies.Count > 0)
        {
            SetStatus($"存在缺失依赖: {string.Join(", ", missingDependencies)}", isError: true);
        }
    }

    private void RefreshDependencyPresentation()
    {
        Dictionary<string, InstalledMod> modMap = _state.AvailableMods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        HashSet<string> enabledIds = _state.AvailableMods
            .Where(mod => mod.IsEnabled)
            .Select(mod => mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> requiredIds = GetRequiredDependencyIds(enabledIds, modMap);
        HashSet<string> missingDependencies = GetMissingDependencies(enabledIds, modMap);
        RefreshDependencyPresentation(requiredIds, missingDependencies);
    }

    private void RefreshDependencyPresentation(HashSet<string> requiredIds, HashSet<string> missingDependencies)
    {
        HashSet<string> enabledIds = _state.AvailableMods
            .Where(mod => mod.IsEnabled)
            .Select(mod => mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> dependentsById = BuildDependentsLookup(_state.AvailableMods, enabledIds);
        foreach (ListViewItem item in _modListView.Items)
        {
            if (item.Tag is not InstalledMod mod)
            {
                continue;
            }

            item.SubItems[1].Text = mod.IsFavorite ? "★" : "";
            item.SubItems[5].Text = mod.Dependencies.Count == 0 ? "-" : string.Join(", ", mod.Dependencies);
            item.SubItems[6].Text = BuildDependencyStatus(mod, requiredIds, dependentsById, missingDependencies);

            bool isMissingDependency = mod.Dependencies.Any(dep => missingDependencies.Contains(dep));
            item.ForeColor = isMissingDependency ? Color.Firebrick : SystemColors.WindowText;
            item.BackColor = requiredIds.Contains(mod.Id) ? Color.FromArgb(245, 245, 220) : SystemColors.Window;
        }
    }

    private static string BuildDependencyStatus(
        InstalledMod mod,
        HashSet<string> requiredIds,
        Dictionary<string, List<string>> dependentsById,
        HashSet<string> missingDependencies)
    {
        List<string> parts = new();
        List<string> missingForMod = mod.Dependencies.Where(missingDependencies.Contains).ToList();
        if (missingForMod.Count > 0)
        {
            parts.Add("缺少: " + string.Join(", ", missingForMod));
        }

        if (requiredIds.Contains(mod.Id) && dependentsById.TryGetValue(mod.Id, out List<string>? dependents) && dependents.Count > 0)
        {
            parts.Add("被依赖: " + string.Join(", ", dependents.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
        }

        return parts.Count == 0 ? "-" : string.Join(" | ", parts);
    }

    private static Dictionary<string, List<string>> BuildDependentsLookup(IEnumerable<InstalledMod> mods, HashSet<string> enabledIds)
    {
        HashSet<string> modIds = mods.Select(mod => mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<string>> dependents = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstalledMod mod in mods)
        {
            if (!enabledIds.Contains(mod.Id))
            {
                continue;
            }

            foreach (string dependency in mod.Dependencies.Where(modIds.Contains))
            {
                if (!dependents.TryGetValue(dependency, out List<string>? entries))
                {
                    entries = new List<string>();
                    dependents[dependency] = entries;
                }

                entries.Add(mod.Id);
            }
        }

        return dependents;
    }

    private static HashSet<string> ResolveEnabledIdsWithDependencies(HashSet<string> selectedIds, Dictionary<string, InstalledMod> modMap)
    {
        HashSet<string> resolved = new(selectedIds, StringComparer.OrdinalIgnoreCase);
        Queue<string> queue = new(selectedIds);
        while (queue.Count > 0)
        {
            string currentId = queue.Dequeue();
            if (!modMap.TryGetValue(currentId, out InstalledMod? mod))
            {
                continue;
            }

            foreach (string dependency in mod.Dependencies)
            {
                if (modMap.ContainsKey(dependency) && resolved.Add(dependency))
                {
                    queue.Enqueue(dependency);
                }
            }
        }

        return resolved;
    }

    private static HashSet<string> GetRequiredDependencyIds(HashSet<string> enabledIds, Dictionary<string, InstalledMod> modMap)
    {
        HashSet<string> required = new(StringComparer.OrdinalIgnoreCase);
        foreach (string enabledId in enabledIds)
        {
            if (!modMap.TryGetValue(enabledId, out InstalledMod? mod))
            {
                continue;
            }

            foreach (string dependency in mod.Dependencies)
            {
                if (modMap.ContainsKey(dependency))
                {
                    required.Add(dependency);
                }
            }
        }

        return required;
    }

    private static HashSet<string> GetMissingDependencies(HashSet<string> enabledIds, Dictionary<string, InstalledMod> modMap)
    {
        HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);
        foreach (string enabledId in enabledIds)
        {
            if (!modMap.TryGetValue(enabledId, out InstalledMod? mod))
            {
                continue;
            }

            foreach (string dependency in mod.Dependencies)
            {
                if (!modMap.ContainsKey(dependency))
                {
                    missing.Add(dependency);
                }
            }
        }

        return missing;
    }

    private void ShowDependencyGraph()
    {
        using DependencyGraphForm form = new(_state.AvailableMods);
        form.ShowDialog(this);
    }

    private void SetAllChecked(bool isChecked)
    {
        if (_modListView.Items.Count == 0)
        {
            return;
        }

        HashSet<string> selectedIds = isChecked
            ? _state.AvailableMods.Select(mod => mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase);
        ApplyResolvedSelection(selectedIds, statusMessage: isChecked ? "已全选并补齐依赖。" : "已取消未被依赖的模组。");
    }

    private bool SaveSelection(bool showMessage)
    {
        try
        {
            SettingsCandidate? selectedCandidate = _state.SelectedCandidate;
            if (selectedCandidate is null)
            {
                throw new InvalidOperationException("没有可写入的 settings.save。");
            }

            JsonObject root = _state.LoadSelectedSettingsJson();
            JsonObject modSettings = root["mod_settings"] as JsonObject ?? new JsonObject();
            JsonArray modList = modSettings["mod_list"] as JsonArray ?? new JsonArray();

            Dictionary<string, JsonObject> existingLocalEntries = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode? node in modList)
            {
                if (node is not JsonObject modEntry)
                {
                    continue;
                }

                string? id = modEntry["id"]?.GetValue<string>();
                string? source = modEntry["source"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id) || !string.Equals(source, "mods_directory", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                existingLocalEntries[id] = modEntry;
            }

            List<InstalledMod> currentSelections = GetCurrentSelections();
            foreach (InstalledMod mod in currentSelections)
            {
                if (!existingLocalEntries.TryGetValue(mod.Id, out JsonObject? entry))
                {
                    entry = new JsonObject();
                    modList.Add(entry);
                    existingLocalEntries[mod.Id] = entry;
                }

                entry["id"] = mod.Id;
                entry["source"] = "mods_directory";
                entry["is_enabled"] = mod.IsEnabled;
            }

            modSettings["mod_list"] = modList;
            modSettings["mods_enabled"] = currentSelections.Any(mod => mod.IsEnabled);
            root["mod_settings"] = modSettings;

            string backupPath = $"{selectedCandidate.SettingsPath}.modthespire.bak";
            File.Copy(selectedCandidate.SettingsPath, backupPath, overwrite: true);
            File.WriteAllText(
                selectedCandidate.SettingsPath,
                root.ToJsonString(JsonOptions.Indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            _state = _state.WithUpdatedMods(currentSelections);
            RefreshDependencyPresentation();
            SetStatus($"已保存到 {selectedCandidate.SettingsPath}");
            if (showMessage)
            {
                MessageBox.Show(this, "Mod 启用状态已保存。下次启动游戏时会按这里的选择加载。", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"保存失败: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.ToString(), "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private List<InstalledMod> GetCurrentSelections()
    {
        Dictionary<string, bool> enabledMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (ListViewItem item in _modListView.Items)
        {
            if (item.Tag is InstalledMod mod)
            {
                enabledMap[mod.Id] = item.Checked;
            }
        }

        return _state.AvailableMods
            .Select(mod => mod with { IsEnabled = enabledMap.TryGetValue(mod.Id, out bool isEnabled) && isEnabled })
            .ToList();
    }

    private async Task LaunchWorkflowAsync(bool saveBeforeLaunch)
    {
        try
        {
            SetBusyState(true);

            if (saveBeforeLaunch && !SaveSelection(showMessage: false))
            {
                return;
            }

            if (_updateBeforeLaunchCheckBox.Checked)
            {
                bool updateSucceeded = await UpdateModsAsync(showPopupWhenFinished: false);
                if (!updateSucceeded)
                {
                    return;
                }
            }

            LaunchGame();
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void LaunchGame()
    {
        try
        {
            SettingsCandidate? selectedCandidate = _state.SelectedCandidate;
            if (selectedCandidate is null)
            {
                throw new InvalidOperationException("请先选择一个账号目录。");
            }

            if (!File.Exists(_state.GameExePath))
            {
                throw new FileNotFoundException("未找到 SlayTheSpire2.exe", _state.GameExePath);
            }

            List<string> args = new();
            if (string.Equals(selectedCandidate.PlatformName, "editor", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("editor 账号目录仅供 Godot 编辑器使用，不能通过当前游戏构建直接启动。请选择 default/... 或 steam/... 账号目录。");
            }

            if (!string.Equals(selectedCandidate.PlatformName, "steam", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--force-steam");
                args.Add("off");
                if (ulong.TryParse(selectedCandidate.UserId, out ulong clientId))
                {
                    args.Add("--clientId");
                    args.Add(clientId.ToString());
                }
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = _state.GameExePath,
                WorkingDirectory = Path.GetDirectoryName(_state.GameExePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                Arguments = string.Join(" ", args.Select(QuoteArgument)),
            };

            Process.Start(startInfo);
            SetStatus($"已启动游戏: {_state.GameExePath}");
            Close();
        }
        catch (Exception ex)
        {
            SetStatus($"启动失败: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.ToString(), "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedAccount()
    {
        try
        {
            SettingsCandidate? selectedCandidate = _state.SelectedCandidate;
            if (selectedCandidate is null)
            {
                throw new InvalidOperationException("没有可删除的账号目录。");
            }

            string accountDirectory = selectedCandidate.AccountDirectory;
            string userDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SlayTheSpire2");
            string normalizedRoot = Path.GetFullPath(userDataRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedAccountDirectory = Path.GetFullPath(accountDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string allowedPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!normalizedAccountDirectory.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"账号目录不在允许删除的范围内: {accountDirectory}");
            }

            DialogResult confirm = MessageBox.Show(
                this,
                $"确认删除账号目录？\n\n{accountDirectory}\n\n这会删除该账号下的 settings.save 以及同目录存档文件。",
                "删除账号存档",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            if (Directory.Exists(accountDirectory))
            {
                Directory.Delete(accountDirectory, recursive: true);
            }

            ReloadState();
            SetStatus($"已删除账号目录: {accountDirectory}");
        }
        catch (Exception ex)
        {
            SetStatus($"删除失败: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.ToString(), "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<bool> UpdateModsAsync(bool showPopupWhenFinished)
    {
        try
        {
            SetBusyState(true);
            SetStatus("正在更新 Mod...");

            UpdateModsResult result = await Task.Run(() => GitModUpdater.Run(_state.GameDirectory, _state.RemoteUrl));

            if (result.Success)
            {
                SetStatus("Mod 更新完成。");
                ReloadState();
                return true;
            }

            SetStatus($"更新失败: {result.Message}", isError: true);
            if (showPopupWhenFinished)
            {
                MessageBox.Show(this, result.Message, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }
        catch (Exception ex)
        {
            SetStatus($"更新失败: {ex.Message}", isError: true);
            MessageBox.Show(this, ex.ToString(), "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool isBusy)
    {
        UseWaitCursor = isBusy;
        _refreshButton.Enabled = !isBusy;
        _updateModsButton.Enabled = !isBusy;
        _deleteAccountButton.Enabled = !isBusy;
        _selectAllButton.Enabled = !isBusy;
        _selectNoneButton.Enabled = !isBusy;
        _showDependencyGraphButton.Enabled = !isBusy;
        _saveButton.Enabled = !isBusy;
        _launchButton.Enabled = !isBusy;
        _saveAndLaunchButton.Enabled = !isBusy;
        _accountComboBox.Enabled = !isBusy;
        _favoritesFirstCheckBox.Enabled = !isBusy;
        _updateBeforeLaunchCheckBox.Enabled = !isBusy;
        _modListView.Enabled = !isBusy;
        _pushRemoteButton.Enabled = !isBusy && _canPushRemote;
    }

    private static string QuoteArgument(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private void SetStatus(string message, bool isError = false)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;
    }

    private void ApplyLauncherIcon()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "very_hot_cocoa.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(AppContext.BaseDirectory, "very_hot_cocoa.ico");
            }

            if (File.Exists(iconPath))
            {
                using Icon icon = new(iconPath);
                Icon = (Icon)icon.Clone();
            }
        }
        catch
        {
        }
    }

    private async Task PushRemoteAsync()
    {
        try
        {
            SetBusyState(true);
            SetStatus("正在推送到远程...");

            PublishRemoteResult result = await Task.Run(() => GitRemotePublisher.Run(_state.GameDirectory));
            SetStatus(result.Message, isError: !result.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"推送失败: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusyState(false);
        }
    }
}

internal sealed class DependencyGraphForm : Form
{
    public DependencyGraphForm(IReadOnlyList<InstalledMod> mods)
    {
        Text = "模组依赖关系图";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 520);
        Size = new Size(920, 640);

        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 360,
        };
        Controls.Add(split);

        TreeView tree = new()
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
        };
        split.Panel1.Controls.Add(tree);

        TextBox detailBox = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10f),
        };
        split.Panel2.Controls.Add(detailBox);

        Dictionary<string, InstalledMod> modMap = mods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        foreach (InstalledMod mod in mods.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
        {
            TreeNode node = BuildNode(mod, modMap, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            tree.Nodes.Add(node);
        }

        tree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is InstalledMod mod)
            {
                string dependencies = mod.Dependencies.Count == 0 ? "-" : string.Join(", ", mod.Dependencies);
                List<string> dependents = mods
                    .Where(candidate => candidate.Dependencies.Contains(mod.Id, StringComparer.OrdinalIgnoreCase))
                    .Select(candidate => candidate.Id)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                detailBox.Text = string.Join(Environment.NewLine, new[]
                {
                    $"Name: {mod.Name}",
                    $"ID: {mod.Id}",
                    $"Author: {mod.Author}",
                    $"Dependencies: {dependencies}",
                    $"Dependents: {(dependents.Count == 0 ? "-" : string.Join(", ", dependents))}",
                    $"Manifest: {mod.ManifestPath}",
                });
            }
        };

        if (tree.Nodes.Count > 0)
        {
            tree.SelectedNode = tree.Nodes[0];
            tree.Nodes[0].Expand();
        }
    }

    private static TreeNode BuildNode(InstalledMod mod, Dictionary<string, InstalledMod> modMap, HashSet<string> path)
    {
        TreeNode node = new($"{mod.Name} ({mod.Id})")
        {
            Tag = mod,
        };

        if (!path.Add(mod.Id))
        {
            node.Nodes.Add(new TreeNode("[循环依赖]"));
            return node;
        }

        if (mod.Dependencies.Count == 0)
        {
            node.Nodes.Add(new TreeNode("[无依赖]"));
        }
        else
        {
            foreach (string dependency in mod.Dependencies.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (modMap.TryGetValue(dependency, out InstalledMod? dependencyMod))
                {
                    node.Nodes.Add(BuildNode(dependencyMod, modMap, new HashSet<string>(path, StringComparer.OrdinalIgnoreCase)));
                }
                else
                {
                    node.Nodes.Add(new TreeNode($"[缺失] {dependency}"));
                }
            }
        }

        return node;
    }
}

internal sealed record LauncherState(
    string GameDirectory,
    string GameExePath,
    string ModsDirectory,
    string RemoteUrl,
    LauncherPreferences Preferences,
    List<SettingsCandidate> SettingsCandidates,
    int SelectedCandidateIndex,
    List<InstalledMod> AvailableMods)
{
    public static LauncherState Empty => new(
        GameDirectory: "",
        GameExePath: "",
        ModsDirectory: "",
        RemoteUrl: "",
        Preferences: LauncherPreferences.Empty,
        SettingsCandidates: new List<SettingsCandidate>(),
        SelectedCandidateIndex: -1,
        AvailableMods: new List<InstalledMod>());

    public SettingsCandidate? SelectedCandidate =>
        SelectedCandidateIndex >= 0 && SelectedCandidateIndex < SettingsCandidates.Count
            ? SettingsCandidates[SelectedCandidateIndex]
            : null;

    public static LauncherState Load()
    {
        LauncherConfig config = LauncherConfig.Load();
        string gameDir = config.ResolveGameDirectory();
        string gameExe = Path.Combine(gameDir, "SlayTheSpire2.exe");
        string modsDir = Path.Combine(gameDir, "mods");
        LauncherPreferences preferences = LauncherPreferences.Load(gameDir);

        List<SettingsCandidate> candidates = SettingsCandidate.Discover();
        int selectedIndex = SettingsCandidate.PickDefaultIndex(candidates);
        SettingsCandidate? selectedCandidate = selectedIndex >= 0 && selectedIndex < candidates.Count ? candidates[selectedIndex] : null;
        List<InstalledMod> mods = InstalledMod.Discover(modsDir, selectedCandidate, preferences);

        return new LauncherState(gameDir, gameExe, modsDir, config.ModUpdateRemoteUrl, preferences, candidates, selectedIndex, mods);
    }

    public LauncherState WithSelectedCandidate(string settingsPath)
    {
        int selectedIndex = SettingsCandidates.FindIndex(item =>
            string.Equals(item.SettingsPath, settingsPath, StringComparison.OrdinalIgnoreCase));
        SettingsCandidate? selectedCandidate = selectedIndex >= 0 && selectedIndex < SettingsCandidates.Count ? SettingsCandidates[selectedIndex] : null;
        List<InstalledMod> mods = InstalledMod.Discover(ModsDirectory, selectedCandidate, Preferences);
        return this with
        {
            SelectedCandidateIndex = selectedIndex,
            AvailableMods = mods,
        };
    }

    public LauncherState WithUpdatedMods(List<InstalledMod> mods)
    {
        return this with
        {
            AvailableMods = mods,
        };
    }

    public LauncherState WithPreferences(LauncherPreferences preferences)
    {
        return this with
        {
            Preferences = preferences,
        };
    }

    public LauncherState ReloadMods()
    {
        List<InstalledMod> mods = InstalledMod.Discover(ModsDirectory, SelectedCandidate, Preferences);
        return this with
        {
            AvailableMods = mods,
        };
    }

    public JsonObject LoadSelectedSettingsJson()
    {
        if (SelectedCandidate is null)
        {
            throw new InvalidOperationException("No selected settings file.");
        }

        string json = File.ReadAllText(SelectedCandidate.SettingsPath, Encoding.UTF8);
        JsonNode? node = JsonNode.Parse(json);
        if (node is not JsonObject root)
        {
            throw new InvalidDataException("settings.save is not a valid JSON object.");
        }

        return root;
    }
}

internal sealed record InstalledMod(
    string Id,
    string Name,
    string Author,
    string ManifestPath,
    List<string> Dependencies,
    bool IsFavorite,
    bool IsEnabled)
{
    public static List<InstalledMod> Discover(string modsDirectory, SettingsCandidate? candidate, LauncherPreferences preferences)
    {
        ModSelectionState selectionState = candidate?.LoadModSelectionState() ?? ModSelectionState.Default;
        Dictionary<string, bool> enabledMap = selectionState.EnabledMap;
        List<InstalledMod> mods = new();
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(modsDirectory))
        {
            return mods;
        }

        foreach (string manifestPath in EnumerateManifestPaths(modsDirectory))
        {
            try
            {
                using FileStream stream = File.OpenRead(manifestPath);
                JsonNode? node = JsonNode.Parse(stream);
                if (node is not JsonObject manifest)
                {
                    continue;
                }

                string? id = manifest["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id) || !seenIds.Add(id))
                {
                    continue;
                }

                string name = manifest["name"]?.GetValue<string>() ?? id;
                string author = manifest["author"]?.GetValue<string>() ?? "";
                List<string> dependencies = (manifest["dependencies"] as JsonArray)?
                    .Select(node => node?.GetValue<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<string>();
                bool isEnabled = enabledMap.TryGetValue(id, out bool value) ? value : selectionState.DefaultEnabled;
                mods.Add(new InstalledMod(
                    id,
                    name,
                    author,
                    manifestPath,
                    dependencies,
                    preferences.FavoriteModIds.Contains(id),
                    isEnabled));
            }
            catch
            {
            }
        }

        return mods
            .OrderByDescending(mod => string.Equals(mod.Id, "BaseLib", StringComparison.OrdinalIgnoreCase))
            .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateManifestPaths(string modsDirectory)
    {
        return Directory.EnumerateFiles(modsDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record SettingsCandidate(
    string PlatformName,
    string UserId,
    string SettingsPath,
    DateTime LastWriteTimeUtc)
{
    public string DisplayName => $"{PlatformName}/{UserId}";

    public string AccountDirectory => Path.GetDirectoryName(SettingsPath)
        ?? throw new InvalidOperationException("settings.save 缺少父目录。");

    public override string ToString() => $"{DisplayName}  ({LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss})";

    public ModSelectionState LoadModSelectionState()
    {
        string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
        JsonNode? node = JsonNode.Parse(json);
        Dictionary<string, bool> map = new(StringComparer.OrdinalIgnoreCase);
        JsonNode? modSettings = node?["mod_settings"];
        bool modsEnabled = modSettings?["mods_enabled"]?.GetValue<bool>() ?? true;
        JsonArray? modList = modSettings?["mod_list"] as JsonArray;
        if (modList == null)
        {
            return new ModSelectionState(map, modsEnabled);
        }

        foreach (JsonNode? entryNode in modList)
        {
            if (entryNode is not JsonObject entry)
            {
                continue;
            }

            string? id = entry["id"]?.GetValue<string>();
            string? source = entry["source"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id) || !string.Equals(source, "mods_directory", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            map[id] = entry["is_enabled"]?.GetValue<bool>() ?? true;
        }

        return new ModSelectionState(map, modsEnabled);
    }

    public static List<SettingsCandidate> Discover()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlayTheSpire2");
        List<SettingsCandidate> candidates = new();
        foreach (string platformName in new[] { "default", "steam", "editor" })
        {
            string platformDir = Path.Combine(root, platformName);
            if (!Directory.Exists(platformDir))
            {
                continue;
            }

            foreach (string userDir in Directory.EnumerateDirectories(platformDir))
            {
                string settingsPath = Path.Combine(userDir, "settings.save");
                if (!File.Exists(settingsPath))
                {
                    continue;
                }

                DirectoryInfo info = new(userDir);
                candidates.Add(new SettingsCandidate(
                    platformName,
                    info.Name,
                    settingsPath,
                    File.GetLastWriteTimeUtc(settingsPath)));
            }
        }

        return candidates
            .OrderByDescending(item => item.LastWriteTimeUtc)
            .ToList();
    }

    public static int PickDefaultIndex(List<SettingsCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return -1;
        }
        return 0;
    }
}

internal sealed record ModSelectionState(
    Dictionary<string, bool> EnabledMap,
    bool DefaultEnabled)
{
    public static ModSelectionState Default { get; } =
        new(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), true);
}

internal sealed record LauncherPreferences(
    HashSet<string> FavoriteModIds,
    bool FavoritesFirst)
{
    public static LauncherPreferences Empty { get; } = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), true);

    public static LauncherPreferences Load(string gameDirectory)
    {
        string path = GetPreferencesPath(gameDirectory);
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            JsonNode? node = JsonNode.Parse(json);
            HashSet<string> favorites = (node?["favorite_mod_ids"] as JsonArray)?
                .Select(entry => entry?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool favoritesFirst = node?["favorites_first"]?.GetValue<bool>() ?? true;
            return new LauncherPreferences(favorites, favoritesFirst);
        }
        catch
        {
            return Empty;
        }
    }

    public LauncherPreferences ToggleFavorite(string modId)
    {
        HashSet<string> favorites = new(FavoriteModIds, StringComparer.OrdinalIgnoreCase);
        if (!favorites.Add(modId))
        {
            favorites.Remove(modId);
        }

        return this with
        {
            FavoriteModIds = favorites,
        };
    }

    public void Save(string gameDirectory)
    {
        string path = GetPreferencesPath(gameDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject root = new()
        {
            ["favorites_first"] = FavoritesFirst,
            ["favorite_mod_ids"] = new JsonArray(FavoriteModIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Select(id => (JsonNode?)id)
                .ToArray()),
        };
        File.WriteAllText(path, root.ToJsonString(JsonOptions.Indented), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string GetPreferencesPath(string gameDirectory)
    {
        string stableKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(gameDirectory))));
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModTheSpire");
        return Path.Combine(root, $"launcher-preferences-{stableKey}.json");
    }
}

internal sealed class LauncherConfig
{
    public string Sts2Path { get; init; } = "";

    public string ModUpdateRemoteUrl { get; init; } =
        "https://github.com/rubbish-picker/Xk92Zm5zS29sTGFrZVN0cmluZ1Rva2VuMTIz-briu.git";

    public static LauncherConfig Load()
    {
        string repoConfigPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config.json");
        repoConfigPath = Path.GetFullPath(repoConfigPath);
        if (File.Exists(repoConfigPath))
        {
            string json = File.ReadAllText(repoConfigPath, Encoding.UTF8);
            JsonNode? node = JsonNode.Parse(json);
            string? sts2Path = node?["sts2_path"]?.GetValue<string>();
            string? remoteUrl = node?["mod_update_remote_url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(sts2Path))
            {
                return new LauncherConfig
                {
                    Sts2Path = sts2Path,
                    ModUpdateRemoteUrl = string.IsNullOrWhiteSpace(remoteUrl)
                        ? "https://github.com/rubbish-picker/Xk92Zm5zS29sTGFrZVN0cmluZ1Rva2VuMTIz-briu.git"
                        : remoteUrl,
                };
            }
        }

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam",
            "steamapps",
            "common",
            "Slay the Spire 2");
        return new LauncherConfig { Sts2Path = fallback };
    }

    public string ResolveGameDirectory()
    {
        string launcherDir = Path.GetFullPath(AppContext.BaseDirectory);
        if (LooksLikeGameDirectory(launcherDir))
        {
            return launcherDir;
        }

        if (!string.IsNullOrWhiteSpace(Sts2Path))
        {
            string configuredDir = Path.GetFullPath(Sts2Path);
            if (LooksLikeGameDirectory(configuredDir))
            {
                return configuredDir;
            }
        }

        return launcherDir;
    }

    private static bool LooksLikeGameDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        string exePath = Path.Combine(directory, "SlayTheSpire2.exe");
        string modsPath = Path.Combine(directory, "mods");
        return File.Exists(exePath) || Directory.Exists(modsPath);
    }
}

internal static class GitModUpdater
{
    private static readonly string[] ManagedPathSpecs = ["mods", ".gitignore"];
    private const string LauncherGitIgnoreContents = """
*
!.gitignore
!mods/
!mods/**
""";

    public static UpdateModsResult Run(string gameDirectory, string remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
        {
            return UpdateModsResult.Fail($"游戏目录不存在: {gameDirectory}");
        }

        if (!ToolExists("git"))
        {
            return UpdateModsResult.Fail("未在 PATH 中找到 git，无法执行更新。");
        }

        StringBuilder log = new();
        bool hasGitDirectory = Directory.Exists(Path.Combine(gameDirectory, ".git"));
        if (!hasGitDirectory || !TryRunGit(gameDirectory, log, "rev-parse", "--is-inside-work-tree"))
        {
            RunGit(gameDirectory, log, "init");
        }

        EnsureManagedGitIgnore(gameDirectory);
        ConfigureSparseCheckout(gameDirectory, log);

        string currentRemote = TryGetGitOutput(gameDirectory, "remote", "get-url", "origin");
        if (string.IsNullOrWhiteSpace(currentRemote))
        {
            RunGit(gameDirectory, log, "remote", "add", "origin", remoteUrl);
        }
        else if (!string.Equals(currentRemote.Trim(), remoteUrl, StringComparison.OrdinalIgnoreCase))
        {
            RunGit(gameDirectory, log, "remote", "set-url", "origin", remoteUrl);
        }

        RunGit(gameDirectory, log, "fetch", "origin", "--prune");

        bool resetMain = TryRunGit(gameDirectory, log, "reset", "--hard", "origin/main");
        if (!resetMain)
        {
            bool resetMaster = TryRunGit(gameDirectory, log, "reset", "--hard", "origin/master");
            if (!resetMaster)
            {
                return UpdateModsResult.Fail("无法重置到 origin/main 或 origin/master。\n\n" + log);
            }
        }

        return UpdateModsResult.Ok("Mod 更新完成。\n\n" + log);
    }

    public static bool ToolExists(string toolName)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = toolName,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process? process = Process.Start(startInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ConfigureSparseCheckout(string gameDirectory, StringBuilder log)
    {
        TryRunGit(gameDirectory, log, "config", "core.sparseCheckout", "true");
        string infoDir = Path.Combine(gameDirectory, ".git", "info");
        Directory.CreateDirectory(infoDir);
        string sparseCheckoutPath = Path.Combine(infoDir, "sparse-checkout");
        string sparseContents = string.Join(Environment.NewLine, ManagedPathSpecs.Select(ToSparseCheckoutPattern)) + Environment.NewLine;
        File.WriteAllText(sparseCheckoutPath, sparseContents, Encoding.UTF8);
    }

    private static string ToSparseCheckoutPattern(string pathSpec)
    {
        if (string.Equals(pathSpec, ".gitignore", StringComparison.OrdinalIgnoreCase))
        {
            return "/.gitignore";
        }

        return "/" + pathSpec.Trim('/').Replace('\\', '/') + "/";
    }

    private static void EnsureManagedGitIgnore(string gameDirectory)
    {
        string gitIgnorePath = Path.Combine(gameDirectory, ".gitignore");
        File.WriteAllText(gitIgnorePath, LauncherGitIgnoreContents.ReplaceLineEndings(Environment.NewLine), Encoding.UTF8);
    }

    private static bool TryRunGit(string workdir, StringBuilder log, params string[] args)
    {
        try
        {
            RunGit(workdir, log, args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RunGit(string workdir, StringBuilder log, params string[] args)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git 进程。");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        log.AppendLine($"> git {string.Join(" ", args)}");
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            log.AppendLine(stdout.Trim());
        }
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            log.AppendLine(stderr.Trim());
        }
        log.AppendLine();

        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            detail = string.IsNullOrWhiteSpace(detail) ? $"退出码 {process.ExitCode}" : detail.Trim();
            throw new InvalidOperationException($"git {string.Join(" ", args)} 执行失败: {detail}");
        }
    }

    private static string TryGetGitOutput(string workdir, params string[] args)
    {
        try
        {
            StringBuilder log = new();
            RunGit(workdir, log, args);
            string text = log.ToString();
            string marker = Environment.NewLine;
            int index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return string.Empty;
            }

            string remainder = text[(index + marker.Length)..].Trim();
            string[] lines = remainder.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            return lines.FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

internal sealed record UpdateModsResult(bool Success, string Message)
{
    public static UpdateModsResult Ok(string message) => new(true, message);

    public static UpdateModsResult Fail(string message) => new(false, message);
}

internal static class GitRemotePublisher
{
    public static PublishRemoteResult Run(string gameDirectory)
    {
        if (!string.Equals(Environment.UserName, "27940", StringComparison.OrdinalIgnoreCase))
        {
            return PublishRemoteResult.Fail("当前电脑未授权推送远程。");
        }

        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
        {
            return PublishRemoteResult.Fail($"游戏目录不存在: {gameDirectory}");
        }

        if (!Directory.Exists(Path.Combine(gameDirectory, ".git")))
        {
            return PublishRemoteResult.Fail("游戏目录不是 git 仓库。");
        }

        if (!GitModUpdater.ToolExists("git"))
        {
            return PublishRemoteResult.Fail("未在 PATH 中找到 git，无法推送。");
        }

        EnsureManagedGitIgnore(gameDirectory);

        string branch = TryGetGitValue(gameDirectory, "branch", "--show-current");
        if (string.IsNullOrWhiteSpace(branch))
        {
            branch = "main";
        }

        string status = TryGetGitValue(gameDirectory, "status", "--porcelain", "--", "mods", ".gitignore");
        bool hasChanges = !string.IsNullOrWhiteSpace(status);

        StringBuilder log = new();
        if (hasChanges)
        {
            RunGit(gameDirectory, log, "add", "-A", "--", "mods", ".gitignore");
            string commitMessage = $"Mod update {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            RunGit(gameDirectory, log, "commit", "-m", commitMessage);
        }

        RunGit(gameDirectory, log, "push", "origin", $"HEAD:{branch}");

        string message = hasChanges
            ? $"已提交并推送到远程分支 {branch}。"
            : $"没有本地改动，已尝试同步推送到远程分支 {branch}。";
        return PublishRemoteResult.Ok(message);
    }

    private static string TryGetGitValue(string workdir, params string[] args)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "git",
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 git 进程。");
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? stdout.Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void EnsureManagedGitIgnore(string gameDirectory)
    {
        string gitIgnorePath = Path.Combine(gameDirectory, ".gitignore");
        const string contents = """
*
!.gitignore
!mods/
!mods/**
""";
        File.WriteAllText(gitIgnorePath, contents.ReplaceLineEndings(Environment.NewLine), Encoding.UTF8);
    }

    private static void RunGit(string workdir, StringBuilder log, params string[] args)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git 进程。");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        log.AppendLine($"> git {string.Join(" ", args)}");
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            log.AppendLine(stdout.Trim());
        }
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            log.AppendLine(stderr.Trim());
        }
        log.AppendLine();

        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"git {string.Join(" ", args)} 执行失败: {detail}".Trim());
        }
    }
}

internal sealed record PublishRemoteResult(bool Success, string Message)
{
    public static PublishRemoteResult Ok(string message) => new(true, message);

    public static PublishRemoteResult Fail(string message) => new(false, message);
}

internal static class JsonOptions
{
    public static JsonSerializerOptions Indented { get; } = new()
    {
        WriteIndented = true,
    };
}
