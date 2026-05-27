using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Services;
using Client.Services.Fishing;

namespace Client.ViewModels;

public sealed class HuntDetectViewModel : ViewModelBase
{
    private static readonly TimeSpan DedupeResetIdleWindow = TimeSpan.FromMinutes(20);
    private static readonly IReadOnlyDictionary<string, string?> HuntMatchPhraseMap =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sovereign Surge"] = null,
            ["Sovereign Storm"] = null,
            ["Sovereign Reckoning"] = null,
            ["Wisp Haunt"] = "The spirits have converged",
            ["Soul Scourge"] = "The dark spirits surge",
            ["Styx Angler"] = "stirs in the dark waters",
            ["Storm Flood"] = "The waters are rising",
            ["Tidecrasher Archon"] = "stirs",
            ["Megalodon"] = "has been spotted",
            ["Ancient Megalodon"] = "has been spotted",
            ["Phantom Megalodon"] = "has been spotted",
            ["Solar Chorus"] = "blazes through",
            ["Helios Sunray"] = "blazes through",
            ["Olympian Devil"] = "has been summoned",
            ["Kerauno Wyrm"] = "has emerged",
            ["War Surge"] = "The waters run red",
            ["Legionnaire Lamprey"] = "stirs",
            ["Kraken"] = "has been spotted",
            ["Ancient Kraken"] = "has been spotted",
            ["Leviathan"] = "has been summoned",
            ["Profane Leviathan"] = "has been summoned",
            ["Skeletal Leviathan"] = "stirs",
            ["Beluga"] = "has been spotted",
            ["Narwhal"] = "has been spotted",
            ["Magician Narwhal"] = "has been spotted",
            ["Mosslurker"] = "has been spotted",
            ["Dreadfin"] = null,
            ["Megamouth Shark"] = null,
            ["Orca Migration"] = "has begun",
            ["Whale Migration"] = "has begun",
            ["Humpback Whale"] = "has begun",
            ["Great White Shark"] = "has been spotted",
            ["Great Hammerhead Shark"] = "has been spotted",
            ["Whale Shark"] = "has been spotted",
            ["Plesiosaur"] = "prowls",
            ["Pliosaur"] = "stalks",
            ["Ancestral Pliosaur"] = "stalks",
            ["Reef Titan"] = "awakens",
            ["Colossus Reef Titan"] = "awakens",
            ["Omnithal"] = "manifests",
            ["Awakened Omnithal"] = "manifests",
            ["Goldwraith"] = "awakens",
            ["Ancient Goldwraith"] = "awakens",
            ["Colossal Ancient Dragon"] = "has begun",
            ["Colossal Blue Dragon"] = "has begun",
            ["Colossal Ethereal Dragon"] = "has begun",
            ["Scylla"] = "has begun",
            ["Mossjaw"] = "has emerged",
            ["Elder Mossjaw"] = "has emerged",
            ["Flower Guardian"] = "has appeared",
            ["Rotbloom"] = "stirs",
            ["Toxic Guardian"] = "has appeared",
            ["Ashclaw"] = "Roslit Volcano",
            ["Frostwyrm"] = "stirs",
            ["Wyvern"] = "has been spotted",
            ["Baby Bloop Fish"] = "have been spotted",
            ["Bloop Fish"] = "has emerged",
            ["Sunken Chests"] = null,
            ["Earthquake"] = null,
        };

    private static readonly IReadOnlyDictionary<string, string> CanonicalNameMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Orca"] = "Orca Migration",
            ["Whale"] = "Whale Migration",
        };

    private static readonly string[] DefaultTargets =
    [
        "Sovereign Surge",
        "Sovereign Storm",
        "Sovereign Reckoning",
        "Wisp Haunt",
        "Soul Scourge",
        "Styx Angler",
        "Storm Flood",
        "Tidecrasher Archon",
        "Megalodon",
        "Ancient Megalodon",
        "Phantom Megalodon",
        "Solar Chorus",
        "Helios Sunray",
        "Olympian Devil",
        "Kerauno Wyrm",
        "War Surge",
        "Legionnaire Lamprey",
        "Kraken",
        "Ancient Kraken",
        "Leviathan",
        "Profane Leviathan",
        "Skeletal Leviathan",
        "Beluga",
        "Narwhal",
        "Magician Narwhal",
        "Mosslurker",
        "Dreadfin",
        "Megamouth Shark",
        "Orca Migration",
        "Whale Migration",
        "Humpback Whale",
        "Great White Shark",
        "Great Hammerhead Shark",
        "Whale Shark",
        "Plesiosaur",
        "Pliosaur",
        "Ancestral Pliosaur",
        "Reef Titan",
        "Colossus Reef Titan",
        "Omnithal",
        "Awakened Omnithal",
        "Goldwraith",
        "Ancient Goldwraith",
        "Colossal Ancient Dragon",
        "Colossal Blue Dragon",
        "Colossal Ethereal Dragon",
        "Scylla",
        "Mossjaw",
        "Elder Mossjaw",
        "Flower Guardian",
        "Rotbloom",
        "Toxic Guardian",
        "Ashclaw",
        "Frostwyrm",
        "Wyvern",
        "Baby Bloop Fish",
        "Bloop Fish",
        "Sunken Chests",
        "Earthquake",
    ];

    private readonly List<HuntDetectTargetOptionViewModel> _allTargets;
    private readonly object _scanSync = new();
    private readonly HuntDetectWebhookManager _webhookManager = new();
    private readonly HashSet<string> _knownEntryKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastSentTextByTitle = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastDetectedTitle;
    private DateTimeOffset _lastDetectedTitleAtUtc;
    private Timer? _scanTimer;
    private RobloxMemory? _memory;
    private bool _scanInProgress;
    private bool _enabled;
    private bool _useHuntColors = true;
    private string _discordWebhook = string.Empty;
    private string _searchText = string.Empty;
    private string _newTargetText = string.Empty;

    public HuntDetectViewModel()
    {
        _allTargets = DefaultTargets
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new HuntDetectTargetOptionViewModel(name))
            .ToList();
        foreach (var target in _allTargets)
        {
            target.UseHuntColors = _useHuntColors;
            target.PropertyChanged += HandleTargetOptionChanged;
        }

        AvailableTargets = new ObservableCollection<HuntDetectTargetOptionViewModel>(_allTargets);
        SelectedTargets = new ObservableCollection<HuntDetectSelectedTargetViewModel>();
        AddTargetCommand = new RelayCommand(_ => AddTargetAsync());
        UnselectAllCommand = new RelayCommand(_ => UnselectAllAsync());
        RemoveSelectedTargetCommand = new RelayCommand(RemoveSelectedTargetAsync);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value))
            {
                return;
            }

            if (value)
            {
                StartScanner();
            }
            else
            {
                StopScanner();
            }
        }
    }

    public string DiscordWebhook
    {
        get => _discordWebhook;
        set => SetProperty(ref _discordWebhook, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefilterTargets();
            }
        }
    }

    public string NewTargetText
    {
        get => _newTargetText;
        set => SetProperty(ref _newTargetText, value);
    }

    public bool UseHuntColors
    {
        get => _useHuntColors;
        set
        {
            if (!SetProperty(ref _useHuntColors, value))
            {
                return;
            }

            foreach (var target in _allTargets)
            {
                target.UseHuntColors = value;
            }

            foreach (var selected in SelectedTargets)
            {
                selected.UseHuntColors = value;
            }
        }
    }

    public string SelectedSummary
    {
        get
        {
            var selectedCount = _allTargets.Count(x => x.IsSelected);
            return selectedCount == 0 ? "None" : selectedCount == 1 ? "1 selected" : $"{selectedCount} selected";
        }
    }

    public ObservableCollection<HuntDetectTargetOptionViewModel> AvailableTargets { get; }

    public ObservableCollection<HuntDetectSelectedTargetViewModel> SelectedTargets { get; }

    public bool HasSelectedTargets => SelectedTargets.Count > 0;
    public bool ShowEmptySelectedState => !HasSelectedTargets;

    public RelayCommand AddTargetCommand { get; }

    public RelayCommand UnselectAllCommand { get; }

    public RelayCommand RemoveSelectedTargetCommand { get; }

    public void RestoreSelectedTargets(IEnumerable<string>? values)
    {
        SelectedTargets.Clear();
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            foreach (var option in _allTargets.Where(x => x.IsSelected))
            {
                option.IsSelected = false;
            }
            return;
        }

        foreach (var value in values)
        {
            var normalized = NormalizeName(value);
            if (normalized.Length == 0)
            {
                continue;
            }

            selectedNames.Add(normalized);
            EnsureTargetExists(normalized);
        }

        foreach (var target in _allTargets.Where(x => x.IsSelected))
        {
            target.IsSelected = false;
        }

        foreach (var target in _allTargets)
        {
            if (selectedNames.Contains(target.Name))
            {
                target.IsSelected = true;
            }
        }
    }

    private Task AddTargetAsync()
    {
        var normalized = NormalizeName(NewTargetText);
        if (normalized.Length == 0)
        {
            return Task.CompletedTask;
        }

        var option = EnsureTargetExists(normalized);
        option.IsSelected = true;
        NewTargetText = string.Empty;
        return Task.CompletedTask;
    }

    private Task UnselectAllAsync()
    {
        foreach (var option in _allTargets.Where(x => x.IsSelected))
        {
            option.IsSelected = false;
        }

        OnPropertyChanged(nameof(SelectedSummary));
        return Task.CompletedTask;
    }

    private Task RemoveSelectedTargetAsync(object? parameter)
    {
        if (parameter is HuntDetectSelectedTargetViewModel selected)
        {
            var option = _allTargets.FirstOrDefault(x => string.Equals(x.Name, selected.Name, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                option.IsSelected = false;
            }
            else
            {
                SelectedTargets.Remove(selected);
                OnPropertyChanged(nameof(SelectedSummary));
                OnPropertyChanged(nameof(HasSelectedTargets));
                OnPropertyChanged(nameof(ShowEmptySelectedState));
            }
        }

        return Task.CompletedTask;
    }

    private HuntDetectTargetOptionViewModel EnsureTargetExists(string name)
    {
        var existing = _allTargets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var added = new HuntDetectTargetOptionViewModel(name);
        added.UseHuntColors = UseHuntColors;
        added.PropertyChanged += HandleTargetOptionChanged;
        _allTargets.Add(added);
        _allTargets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        RefilterTargets();
        return added;
    }

    private void RefilterTargets()
    {
        var query = (SearchText ?? string.Empty).Trim();
        var filtered = _allTargets
            .Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        AvailableTargets.Clear();
        foreach (var target in filtered)
        {
            AvailableTargets.Add(target);
        }
    }

    private void HandleTargetOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HuntDetectTargetOptionViewModel.IsSelected))
        {
            if (sender is HuntDetectTargetOptionViewModel changed)
            {
                if (changed.IsSelected)
                {
                    if (!SelectedTargets.Any(x => string.Equals(x.Name, changed.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var selectedTarget = new HuntDetectSelectedTargetViewModel(changed.Name)
                        {
                            UseHuntColors = UseHuntColors,
                        };
                        SelectedTargets.Add(selectedTarget);
                    }
                }
                else
                {
                    var existing = SelectedTargets.FirstOrDefault(x => string.Equals(x.Name, changed.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                    {
                        SelectedTargets.Remove(existing);
                    }
                }
            }

            OnPropertyChanged(nameof(SelectedSummary));
            OnPropertyChanged(nameof(HasSelectedTargets));
            OnPropertyChanged(nameof(ShowEmptySelectedState));
        }
    }

    public IReadOnlyList<string> GetSelectedTargetNames()
    {
        return SelectedTargets.Select(x => x.Name).ToList();
    }

    private static string NormalizeName(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var split = text.IndexOf(" - ", StringComparison.Ordinal);
        if (split > 0)
        {
            text = text[..split].Trim();
        }

        if (CanonicalNameMap.TryGetValue(text, out var canonical))
        {
            return canonical;
        }

        return text;
    }

    private void StartScanner()
    {
        lock (_scanSync)
        {
            StopScanner_NoLock();
            _knownEntryKeys.Clear();
            _lastSentTextByTitle.Clear();
            _lastDetectedTitle = null;
            _lastDetectedTitleAtUtc = DateTimeOffset.MinValue;
            try
            {
                _memory = new RobloxMemory(OffsetsSourceProvider.Current);
                SnapshotCurrentEntriesAsKnown_NoLock();
            }
            catch (Exception ex)
            {
                AppLog.Error("HuntDetect", "Failed to initialize scanner.", ex);
            }

            _scanTimer = new Timer(_ => ScanTick(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));
        }
    }

    private void StopScanner()
    {
        lock (_scanSync)
        {
            StopScanner_NoLock();
        }
    }

    private void StopScanner_NoLock()
    {
        _scanTimer?.Dispose();
        _scanTimer = null;
        _memory?.Dispose();
        _memory = null;
        _scanInProgress = false;
        _lastDetectedTitle = null;
        _lastDetectedTitleAtUtc = DateTimeOffset.MinValue;
    }

    private void SnapshotCurrentEntriesAsKnown_NoLock()
    {
        var entries = ReadChatBodyEntries_NoLock();
        foreach (var entry in entries)
        {
            _knownEntryKeys.Add(entry.Key);
        }
    }

    private void ScanTick()
    {
        lock (_scanSync)
        {
            if (!_enabled || _scanInProgress)
            {
                return;
            }

            _scanInProgress = true;
        }

        try
        {
            ScanOnce();
        }
        catch (Exception ex)
        {
            AppLog.Error("HuntDetect", "Scan tick failed.", ex);
        }
        finally
        {
            lock (_scanSync)
            {
                _scanInProgress = false;
            }
        }
    }

    private void ScanOnce()
    {
        List<ChatEntry> entries;
        List<HuntMatchRule> selectedRules;
        string webhook;

        lock (_scanSync)
        {
            if (!_enabled)
            {
                return;
            }

            entries = ReadChatBodyEntries_NoLock();
            selectedRules = GetSelectedRules_NoLock();
            webhook = _discordWebhook.Trim();
        }

        if (entries.Count == 0 || selectedRules.Count == 0 || webhook.Length == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var shouldEvaluate = false;
            lock (_scanSync)
            {
                if (!_knownEntryKeys.Contains(entry.Key))
                {
                    _knownEntryKeys.Add(entry.Key);
                    shouldEvaluate = true;
                }
            }

            if (!shouldEvaluate)
            {
                continue;
            }

            foreach (var rule in selectedRules)
            {
                if (!IsMatch(entry.Text, rule))
                {
                    continue;
                }

                var normalizedText = NormalizeForDedup(entry.Text);
                var shouldSend = false;
                lock (_scanSync)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (!string.IsNullOrWhiteSpace(_lastDetectedTitle) &&
                        now - _lastDetectedTitleAtUtc >= DedupeResetIdleWindow)
                    {
                        _lastSentTextByTitle.Clear();
                        _lastDetectedTitle = null;
                        _lastDetectedTitleAtUtc = DateTimeOffset.MinValue;
                    }

                    if (!_lastSentTextByTitle.TryGetValue(rule.Title, out var lastText) ||
                        !string.Equals(lastText, normalizedText, StringComparison.Ordinal))
                    {
                        _lastSentTextByTitle[rule.Title] = normalizedText;
                        if (!string.Equals(_lastDetectedTitle, rule.Title, StringComparison.OrdinalIgnoreCase))
                        {
                            _lastDetectedTitle = rule.Title;
                        }
                        _lastDetectedTitleAtUtc = now;
                        shouldSend = true;
                    }
                }

                if (!shouldSend)
                {
                    continue;
                }

                _ = _webhookManager.SendDetectedAsync(
                    webhook,
                    rule.Title,
                    entry.Text,
                    DateTimeOffset.UtcNow,
                    serverInfo: null);
            }
        }
    }

    private List<HuntMatchRule> GetSelectedRules_NoLock()
    {
        return SelectedTargets
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(title =>
            {
                HuntMatchPhraseMap.TryGetValue(title, out var phrase);
                return new HuntMatchRule(title, phrase);
            })
            .ToList();
    }

    private List<ChatEntry> ReadChatBodyEntries_NoLock()
    {
        var results = new List<ChatEntry>();
        if (_memory is null)
        {
            return results;
        }

        _memory.EnsureAttached();
        var dataModel = _memory.GetDataModel();
        if (dataModel == 0)
        {
            return results;
        }

        // Always scan TextChatService, even when ExperienceChat UI is hidden/absent.
        CollectTextChatServiceMessages(results, dataModel);

        // UI scan is best-effort and may be unavailable when chat window is closed.
        var coreGui = _memory.FindDescendantByClass(dataModel, "CoreGui");
        if (coreGui != 0)
        {
            var experienceChat = _memory.FindDescendantByName(coreGui, "ExperienceChat");
            if (experienceChat != 0)
            {
                CollectExperienceChatUiBodyText(results, experienceChat);
            }
        }

        return results;
    }

    private void CollectExperienceChatUiBodyText(List<ChatEntry> results, ulong experienceChat)
    {
        if (_memory is null || experienceChat == 0)
        {
            return;
        }

        var stack = new Stack<ulong>();
        var seen = new HashSet<ulong>();
        stack.Push(experienceChat);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            var name = _memory.ReadName(current);
            if (string.Equals(name, "BodyText", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_memory.ReadClass(current), "TextLabel", StringComparison.OrdinalIgnoreCase))
            {
                var text = _memory.ReadGuiText(current)?.Trim() ?? string.Empty;
                if (text.Length > 0)
                {
                    results.Add(new ChatEntry(current, text));
                }
            }

            foreach (var child in _memory.ReadChildren(current))
            {
                stack.Push(child);
            }
        }
    }

    private void CollectTextChatServiceMessages(List<ChatEntry> results, ulong dataModel)
    {
        if (_memory is null || dataModel == 0)
        {
            return;
        }

        var textChatService = _memory.FindDescendantByClass(dataModel, "TextChatService");
        if (textChatService == 0)
        {
            return;
        }

        var stack = new Stack<ulong>();
        var seen = new HashSet<ulong>();
        stack.Push(textChatService);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            var className = _memory.ReadClass(current);
            if (className.Contains("TextChatMessage", StringComparison.OrdinalIgnoreCase))
            {
                var text = ReadTextChatMessageText(current);
                if (text.Length > 0)
                {
                    results.Add(new ChatEntry(current, text));
                }
            }

            foreach (var child in _memory.ReadChildren(current))
            {
                stack.Push(child);
            }
        }
    }

    private string ReadTextChatMessageText(ulong address)
    {
        if (_memory is null || address == 0)
        {
            return string.Empty;
        }

        var text = (_memory.ReadGuiText(address) ?? string.Empty).Trim();
        if (text.Length > 0)
        {
            return text;
        }

        try
        {
            var textOffset = _memory.GetOffset("Text");
            var indirect = (_memory.ReadString(_memory.ReadPtr(address + textOffset)) ?? string.Empty).Trim();
            if (indirect.Length > 0)
            {
                return indirect;
            }

            var direct = (_memory.ReadString(address + textOffset) ?? string.Empty).Trim();
            if (direct.Length > 0)
            {
                return direct;
            }
        }
        catch
        {
            // Offsets can differ by build; best-effort fallback is GUI text only.
        }

        return string.Empty;
    }

    private static bool IsMatch(string entryText, HuntMatchRule rule)
    {
        if (entryText.Length == 0)
        {
            return false;
        }

        if (!entryText.Contains(rule.Title, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Phrase))
        {
            return true;
        }

        return entryText.Contains(rule.Phrase, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record HuntMatchRule(string Title, string? Phrase);

    private static string NormalizeForDedup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
                continue;
            }

            previousWasSpace = false;
            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString().Trim();
    }

    private readonly record struct ChatEntry(ulong Address, string Text)
    {
        public string Key => $"{Address:X}:{Text}";
    }
}

public sealed class HuntDetectTargetOptionViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _useHuntColors;

    public HuntDetectTargetOptionViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public Avalonia.Media.IBrush Brush => HuntDetectColors.GetBrush(Name, UseHuntColors);

    public bool UseHuntColors
    {
        get => _useHuntColors;
        set
        {
            if (SetProperty(ref _useHuntColors, value))
            {
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class HuntDetectSelectedTargetViewModel : ViewModelBase
{
    private bool _useHuntColors;

    public HuntDetectSelectedTargetViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public Avalonia.Media.IBrush Brush => HuntDetectColors.GetBrush(Name, UseHuntColors);

    public bool UseHuntColors
    {
        get => _useHuntColors;
        set
        {
            if (SetProperty(ref _useHuntColors, value))
            {
                OnPropertyChanged(nameof(Brush));
            }
        }
    }
}
