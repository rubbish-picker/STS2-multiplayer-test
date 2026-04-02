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

internal sealed class MainForm : Form
{
    private readonly ComboBox _accountComboBox;
    private readonly Label _settingsPathLabel;
    private readonly Label _modsPathLabel;
    private readonly ListView _modListView;
    private readonly Button _refreshButton;
    private readonly Button _updateModsButton;
    private readonly Button _pushRemoteButton;
    private readonly Button _deleteAccountButton;
    private readonly Button _selectAllButton;
    private readonly Button _selectNoneButton;
    private readonly Button _saveButton;
    private readonly Button _launchButton;
    private readonly Button _saveAndLaunchButton;
    private readonly CheckBox _updateBeforeLaunchCheckBox;
    private readonly Label _statusLabel;
    private readonly bool _canPushRemote;

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

        _modListView = new ListView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            FullRowSelect = true,
            View = View.Details,
        };
        _modListView.Columns.Add("启用", 70);
        _modListView.Columns.Add("名称", 260);
        _modListView.Columns.Add("ID", 220);
        _modListView.Columns.Add("作者", 160);
        _modListView.Columns.Add("位置", 360);
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
        foreach (InstalledMod mod in _state.AvailableMods)
        {
            ListViewItem item = new("")
            {
                Checked = mod.IsEnabled,
                Tag = mod,
            };
            item.SubItems.Add(mod.Name);
            item.SubItems.Add(mod.Id);
            item.SubItems.Add(mod.Author);
            item.SubItems.Add(mod.ManifestPath);
            _modListView.Items.Add(item);
        }
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

    private void SetAllChecked(bool isChecked)
    {
        foreach (ListViewItem item in _modListView.Items)
        {
            item.Checked = isChecked;
        }
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

            _state = _state.WithUpdatedMods(GetCurrentSelections());
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
        List<InstalledMod> mods = new();
        foreach (ListViewItem item in _modListView.Items)
        {
            if (item.Tag is InstalledMod mod)
            {
                mods.Add(mod with { IsEnabled = item.Checked });
            }
        }

        return mods;
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
                if (ulong.TryParse(selectedCandidate.UserId, out ulong clientId) && clientId != 1)
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

            string launcherExePath = Application.ExecutablePath;
            if (DetachedGitUpdater.ShouldUseDetachedUpdate(_state.GameDirectory, launcherExePath))
            {
                DetachedUpdateStartResult startResult = DetachedGitUpdater.Start(
                    _state.GameDirectory,
                    _state.RemoteUrl,
                    launcherExePath,
                    Environment.ProcessId);

                if (!startResult.Success)
                {
                    SetStatus($"更新失败: {startResult.Message}", isError: true);
                    if (showPopupWhenFinished)
                    {
                        MessageBox.Show(this, startResult.Message, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return false;
                }

                SetStatus("启动器即将关闭，外部更新器会在更新后自动重新打开启动器。");
                Close();
                return false;
            }

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
        _saveButton.Enabled = !isBusy;
        _launchButton.Enabled = !isBusy;
        _saveAndLaunchButton.Enabled = !isBusy;
        _accountComboBox.Enabled = !isBusy;
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

internal sealed record LauncherState(
    string GameDirectory,
    string GameExePath,
    string ModsDirectory,
    string RemoteUrl,
    List<SettingsCandidate> SettingsCandidates,
    int SelectedCandidateIndex,
    List<InstalledMod> AvailableMods)
{
    public static LauncherState Empty => new(
        GameDirectory: "",
        GameExePath: "",
        ModsDirectory: "",
        RemoteUrl: "",
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

        List<SettingsCandidate> candidates = SettingsCandidate.Discover();
        int selectedIndex = SettingsCandidate.PickDefaultIndex(candidates);
        SettingsCandidate? selectedCandidate = selectedIndex >= 0 && selectedIndex < candidates.Count ? candidates[selectedIndex] : null;
        List<InstalledMod> mods = InstalledMod.Discover(modsDir, selectedCandidate);

        return new LauncherState(gameDir, gameExe, modsDir, config.ModUpdateRemoteUrl, candidates, selectedIndex, mods);
    }

    public LauncherState WithSelectedCandidate(string settingsPath)
    {
        int selectedIndex = SettingsCandidates.FindIndex(item =>
            string.Equals(item.SettingsPath, settingsPath, StringComparison.OrdinalIgnoreCase));
        SettingsCandidate? selectedCandidate = selectedIndex >= 0 && selectedIndex < SettingsCandidates.Count ? SettingsCandidates[selectedIndex] : null;
        List<InstalledMod> mods = InstalledMod.Discover(ModsDirectory, selectedCandidate);
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
    bool IsEnabled)
{
    public static List<InstalledMod> Discover(string modsDirectory, SettingsCandidate? candidate)
    {
        ModSelectionState selectionState = candidate?.LoadModSelectionState() ?? ModSelectionState.Default;
        Dictionary<string, bool> enabledMap = selectionState.EnabledMap;
        List<InstalledMod> mods = new();
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(modsDirectory))
        {
            return mods;
        }

        foreach (string modDir in Directory.EnumerateDirectories(modsDirectory))
        {
            try
            {
                string modFolderName = Path.GetFileName(modDir);
                string manifestPath = Path.Combine(modDir, $"{modFolderName}.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

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
                bool isEnabled = enabledMap.TryGetValue(id, out bool value) ? value : selectionState.DefaultEnabled;
                mods.Add(new InstalledMod(id, name, author, manifestPath, isEnabled));
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

internal static class DetachedGitUpdater
{
    public static bool ShouldUseDetachedUpdate(string gameDirectory, string launcherExePath)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || string.IsNullOrWhiteSpace(launcherExePath))
        {
            return false;
        }

        string normalizedGameDirectory = Path.GetFullPath(gameDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedLauncherPath = Path.GetFullPath(launcherExePath);
        string gamePrefix = normalizedGameDirectory + Path.DirectorySeparatorChar;

        return normalizedLauncherPath.StartsWith(gamePrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static DetachedUpdateStartResult Start(
        string gameDirectory,
        string remoteUrl,
        string launcherExePath,
        int currentProcessId)
    {
        try
        {
            string tempScriptPath = Path.Combine(
                Path.GetTempPath(),
                $"modthespire-update-{Guid.NewGuid():N}.ps1");

            string script = BuildScript();
            File.WriteAllText(tempScriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            ProcessStartInfo startInfo = new()
            {
                FileName = "powershell.exe",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(tempScriptPath);
            startInfo.ArgumentList.Add("-GameDirectory");
            startInfo.ArgumentList.Add(gameDirectory);
            startInfo.ArgumentList.Add("-RemoteUrl");
            startInfo.ArgumentList.Add(remoteUrl);
            startInfo.ArgumentList.Add("-LauncherPath");
            startInfo.ArgumentList.Add(launcherExePath);
            startInfo.ArgumentList.Add("-WaitProcessId");
            startInfo.ArgumentList.Add(currentProcessId.ToString());

            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动外部更新器。");

            return DetachedUpdateStartResult.Ok("已启动外部更新器。");
        }
        catch (Exception ex)
        {
            return DetachedUpdateStartResult.Fail($"无法启动外部更新器: {ex.Message}");
        }
    }

    private static string BuildScript()
    {
        return """
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDirectory,
    [Parameter(Mandatory = $true)]
    [string]$RemoteUrl,
    [Parameter(Mandatory = $true)]
    [string]$LauncherPath,
    [Parameter(Mandatory = $true)]
    [int]$WaitProcessId
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms

$log = New-Object System.Text.StringBuilder

function Append-Log([string]$Text) {
    [void]$log.AppendLine($Text)
}

function Run-Git([string[]]$Args) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "git"
    $psi.WorkingDirectory = $GameDirectory
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    foreach ($arg in $Args) {
        [void]$psi.ArgumentList.Add($arg)
    }

    $process = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $process) {
        throw "无法启动 git 进程。"
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    Append-Log("> git $($Args -join ' ')")
    if (-not [string]::IsNullOrWhiteSpace($stdout)) {
        Append-Log($stdout.Trim())
    }
    if (-not [string]::IsNullOrWhiteSpace($stderr)) {
        Append-Log($stderr.Trim())
    }
    Append-Log("")

    if ($process.ExitCode -ne 0) {
        $detail = if (-not [string]::IsNullOrWhiteSpace($stderr)) { $stderr.Trim() } elseif (-not [string]::IsNullOrWhiteSpace($stdout)) { $stdout.Trim() } else { "退出码 $($process.ExitCode)" }
        throw "git $($Args -join ' ') 执行失败: $detail"
    }
}

function Try-RunGit([string[]]$Args) {
    try {
        Run-Git $Args
        return $true
    }
    catch {
        return $false
    }
}

try {
    for ($i = 0; $i -lt 600; $i++) {
        $waitingProcess = Get-Process -Id $WaitProcessId -ErrorAction SilentlyContinue
        if ($null -eq $waitingProcess) {
            break
        }
        Start-Sleep -Milliseconds 250
    }

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "未在 PATH 中找到 git，无法执行更新。"
    }

    if (-not (Test-Path -LiteralPath $GameDirectory)) {
        throw "游戏目录不存在: $GameDirectory"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory ".git")) -or -not (Try-RunGit @("rev-parse", "--is-inside-work-tree"))) {
        Run-Git @("init")
    }

    $currentRemote = ""
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "git"
        $psi.WorkingDirectory = $GameDirectory
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        [void]$psi.ArgumentList.Add("remote")
        [void]$psi.ArgumentList.Add("get-url")
        [void]$psi.ArgumentList.Add("origin")
        $process = [System.Diagnostics.Process]::Start($psi)
        if ($process -ne $null) {
            $currentRemote = $process.StandardOutput.ReadToEnd().Trim()
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) {
                $currentRemote = ""
            }
        }
    }
    catch {
        $currentRemote = ""
    }

    if ([string]::IsNullOrWhiteSpace($currentRemote)) {
        Run-Git @("remote", "add", "origin", $RemoteUrl)
    }
    elseif (-not [string]::Equals($currentRemote.Trim(), $RemoteUrl, [System.StringComparison]::OrdinalIgnoreCase)) {
        Run-Git @("remote", "set-url", "origin", $RemoteUrl)
    }

    Run-Git @("fetch", "origin", "--prune")

    if (-not (Try-RunGit @("reset", "--hard", "origin/main"))) {
        if (-not (Try-RunGit @("reset", "--hard", "origin/master"))) {
            throw "无法重置到 origin/main 或 origin/master。"
        }
    }

    Start-Process -FilePath $LauncherPath | Out-Null
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Mod 更新失败。`r`n`r`n$($_.Exception.Message)`r`n`r`n$($log.ToString())",
        "ModTheSpire 更新失败",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    ) | Out-Null
    exit 1
}
finally {
    try {
        Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
    }
    catch {
    }
}
""";
    }
}

internal sealed record UpdateModsResult(bool Success, string Message)
{
    public static UpdateModsResult Ok(string message) => new(true, message);

    public static UpdateModsResult Fail(string message) => new(false, message);
}

internal sealed record DetachedUpdateStartResult(bool Success, string Message)
{
    public static DetachedUpdateStartResult Ok(string message) => new(true, message);

    public static DetachedUpdateStartResult Fail(string message) => new(false, message);
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

        string branch = TryGetGitValue(gameDirectory, "branch", "--show-current");
        if (string.IsNullOrWhiteSpace(branch))
        {
            branch = "main";
        }

        string status = TryGetGitValue(gameDirectory, "status", "--porcelain");
        bool hasChanges = !string.IsNullOrWhiteSpace(status);

        StringBuilder log = new();
        if (hasChanges)
        {
            RunGit(gameDirectory, log, "add", "-A");
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
