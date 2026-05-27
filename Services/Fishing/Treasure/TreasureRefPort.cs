using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
namespace Client.Services.Fishing;
internal sealed class TreasureAppraiser : IDisposable
{
    private const int Rows = 7;
    private const int Columns = 3;
    private const double PostAppraiseGoneDelaySeconds = 0.100;
    private const double PostFinalRowClickInitialWaitSeconds = 0.500;
    private const double PostFinalRowClickPollSeconds = 0.030;
    private const double PostFinalRowClickSettleSeconds = 0.220;

    private readonly Random random = new Random();
    private readonly IOffsetsSource offsetsSource;
    private Process process = null!;
    private ProcessMemory memory = null!;
    private OffsetTable offsets = null!;
    private ulong baseAddress;
    private IntPtr robloxWindow;
    private ulong playerGuiAddress;
    private TreasureTargets? targets;
    private string? listPathHint;
    private string? appraisePathHint;
    private string? weightPathHint;
    private List<List<Point>>? rowPoints;
    private int rowIndex;
    private double nextActionTime;
    private string rowCycleStartWeightText = string.Empty;
    private string finalPhaseLastWeightText = string.Empty;
    private string finalPhaseBestWeightText = string.Empty;
    private double finalPhaseLastChangeTime;
    private bool finalPhaseSawChangedWeight;
    private TreasureAppraisePhase phase;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();

    public TreasureAppraiser(IOffsetsSource offsetsSource)
    {
        this.offsetsSource = offsetsSource ?? throw new ArgumentNullException(nameof(offsetsSource));
        Settings = new TreasureAppraiseSettings(0.20, null);
    }

    public TreasureAppraiseSettings Settings { get; set; }

    public void Reset(TreasureAppraiseSettings settings)
    {
        Settings = settings ?? new TreasureAppraiseSettings(0.20, null);
        targets = null;
        listPathHint = null;
        appraisePathHint = null;
        weightPathHint = null;
        rowPoints = null;
        rowIndex = 0;
        nextActionTime = 0.0;
        rowCycleStartWeightText = string.Empty;
        finalPhaseLastWeightText = string.Empty;
        finalPhaseBestWeightText = string.Empty;
        finalPhaseLastChangeTime = 0.0;
        finalPhaseSawChangedWeight = false;
        phase = TreasureAppraisePhase.Setup;
    }

    public TreasureStepResult RunStep()
    {
        EnsureConnected();

        double now = stopwatch.Elapsed.TotalSeconds;
        if (now < nextActionTime)
            return TreasureStepResult.Running;

        if (rowPoints == null || rowPoints.Count == 0)
        {
            EnsureTargets();
            if (!EnsureTargetAddressesHealthy())
            {
                targets = null;
                throw new InvalidOperationException("Could not find List and Weight in PlayerGui.");
            }

            RectangleF listBounds = ReadScreenBounds(targets!.ListFrame);
            rowPoints = GenerateRows(listBounds);
            BeginRowCycle(now);
            phase = TreasureAppraisePhase.FindAppraise;
            return TreasureStepResult.Running;
        }

        if (phase == TreasureAppraisePhase.FindAppraise)
        {
            RefreshAppraiseTarget(false);
            RectangleF appraiseBounds;
            if (targets!.AppraiseButton != 0 && TryReadUsableBounds(targets.AppraiseButton, out appraiseBounds))
            {
                Point clickPoint = GetCenter(appraiseBounds);
                MouseInput.ClickAt(clickPoint.X, clickPoint.Y);
                BeginRowCycle(now);
                phase = TreasureAppraisePhase.WaitForAppraiseGone;
                nextActionTime = now + 0.020;
                return TreasureStepResult.Running;
            }

            BeginRowCycle(now);
            phase = TreasureAppraisePhase.ClickRows;
            return TreasureStepResult.Running;
        }

        if (phase == TreasureAppraisePhase.WaitForAppraiseGone)
        {
            RectangleF appraiseBounds;
            if (targets!.AppraiseButton != 0 && TryReadUsableBounds(targets.AppraiseButton, out appraiseBounds))
            {
                Point clickPoint = GetCenter(appraiseBounds);
                MouseInput.ClickAt(clickPoint.X, clickPoint.Y);
                nextActionTime = now + Settings.ClickDelaySeconds;
                return TreasureStepResult.Running;
            }

            BeginRowCycle(now);
            phase = TreasureAppraisePhase.ClickRows;
            nextActionTime = now + PostAppraiseGoneDelaySeconds;
            return TreasureStepResult.Running;
        }

        if (phase == TreasureAppraisePhase.WaitAfterFinalRowClick)
        {
            if (!EnsureWeightAddressHealthy())
            {
                nextActionTime = now + PostFinalRowClickPollSeconds;
                return TreasureStepResult.Running;
            }
            string current = ReadGuiText(targets!.WeightTextLabel);
            if (IsSaneWeightText(current))
            {
                if (!SameWeightValue(current, rowCycleStartWeightText))
                {
                    if (!SameWeightValue(current, finalPhaseLastWeightText))
                    {
                        finalPhaseLastWeightText = current;
                        finalPhaseLastChangeTime = now;
                    }

                    finalPhaseSawChangedWeight = true;
                    finalPhaseBestWeightText = PickBetterWeightText(finalPhaseBestWeightText, current);
                }
            }

            if (finalPhaseSawChangedWeight && (now - finalPhaseLastChangeTime) >= PostFinalRowClickSettleSeconds)
            {
                string settled = string.IsNullOrWhiteSpace(finalPhaseBestWeightText) ? current : finalPhaseBestWeightText;
                return FinishOrRepeatFromText(settled);
            }

            nextActionTime = now + PostFinalRowClickPollSeconds;
            return TreasureStepResult.Running;
        }

        if (rowIndex >= rowPoints.Count)
        {
            phase = TreasureAppraisePhase.WaitAfterFinalRowClick;
            nextActionTime = now + PostFinalRowClickInitialWaitSeconds;
            return TreasureStepResult.Running;
        }

        RectangleF rowAppraiseBounds;
        RefreshAppraiseTarget(false);
        if (targets!.AppraiseButton != 0 && TryReadUsableBounds(targets.AppraiseButton, out rowAppraiseBounds))
        {
            Point clickPoint = GetCenter(rowAppraiseBounds);
            MouseInput.ClickAt(clickPoint.X, clickPoint.Y);
            BeginRowCycle(now);
            phase = TreasureAppraisePhase.WaitForAppraiseGone;
            nextActionTime = now + 0.020;
            return TreasureStepResult.Running;
        }

        List<Point> row = rowPoints[rowIndex];
        Point target = row[random.Next(row.Count)];
        MouseInput.ClickAt(target.X, target.Y);
        rowIndex++;
        if (rowIndex >= rowPoints.Count)
        {
            phase = TreasureAppraisePhase.WaitAfterFinalRowClick;
            nextActionTime = now + PostFinalRowClickInitialWaitSeconds;
            return TreasureStepResult.Running;
        }

        nextActionTime = now + Settings.ClickDelaySeconds;
        return TreasureStepResult.Running;
    }

    private void BeginRowCycle(double now)
    {
        rowIndex = 0;
        rowCycleStartWeightText = ReadGuiText(targets!.WeightTextLabel) ?? string.Empty;
        finalPhaseLastWeightText = string.Empty;
        finalPhaseBestWeightText = string.Empty;
        finalPhaseLastChangeTime = now;
        finalPhaseSawChangedWeight = false;
        nextActionTime = now;
    }

    private TreasureStepResult FinishOrRepeat()
    {
        if (!EnsureWeightAddressHealthy())
        {
            phase = TreasureAppraisePhase.FindAppraise;
            nextActionTime = stopwatch.Elapsed.TotalSeconds + Settings.ClickDelaySeconds;
            return TreasureStepResult.Running;
        }

        string valueText = ReadGuiText(targets!.WeightTextLabel);
        if (!IsSaneWeightText(valueText))
        {
            if (!RefreshWeightTarget())
            {
                phase = TreasureAppraisePhase.FindAppraise;
                nextActionTime = stopwatch.Elapsed.TotalSeconds + Settings.ClickDelaySeconds;
                return TreasureStepResult.Running;
            }

            valueText = ReadGuiText(targets!.WeightTextLabel);
        }

        if (!IsSaneWeightText(valueText))
            valueText = "<empty>";

        return FinishOrRepeatFromText(valueText);
    }

    private TreasureStepResult FinishOrRepeatFromText(string valueText)
    {
        double? value = ParseWeightValue(valueText);
        Debug.WriteLine("Treasure appraise weight: " + valueText);

        if (!Settings.TargetWeight.HasValue)
            return TreasureStepResult.Complete(valueText);

        if (value.HasValue && value.Value >= Settings.TargetWeight.Value)
            return TreasureStepResult.Complete(valueText);

        rowIndex = 0;
        phase = TreasureAppraisePhase.FindAppraise;
        nextActionTime = stopwatch.Elapsed.TotalSeconds + Settings.ClickDelaySeconds;
        return TreasureStepResult.Running;
    }

    private static double? ParseWeightValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Match match = Regex.Match(text, @"[-+]?\d+(?:[.,]\d+)?");
        if (!match.Success)
            return null;

        string number = match.Value.Replace(',', '.');
        double value;
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return null;

        return value;
    }

    private void EnsureConnected()
    {
        if (process != null && !process.HasExited && memory != null)
            return;

        DisposeMemory();

        if (!offsetsSource.IsPopulated)
            throw new InvalidOperationException("Offsets unavailable; sign in and stay connected to receive offsets.");

        offsets = new OffsetTable(offsetsSource);
        process = FindRobloxProcess();
        robloxWindow = process.MainWindowHandle;
        if (robloxWindow == IntPtr.Zero)
            throw new InvalidOperationException("Found Roblox, but its main window handle is not ready.");

        baseAddress = GetMainModuleBase(process);
        memory = ProcessMemory.Open(process.Id);
    }

    private void EnsureTargets()
    {
        if (targets != null)
            return;

        ulong fakeDataModel = memory.ReadPtr(baseAddress + offsets.Get("FakeDataModel", "Pointer"));
        ulong dataModel = memory.ReadPtr(fakeDataModel + offsets.Get("FakeDataModel", "RealDataModel"));
        if (!ProcessMemory.IsLikelyUserModeAddress(dataModel))
            throw new InvalidOperationException("DataModel pointer resolved to an invalid address.");

        ulong playerGui = FindLocalPlayerGui(dataModel);
        if (playerGui == 0)
            throw new InvalidOperationException("Could not find LocalPlayer.PlayerGui.");

        playerGuiAddress = playerGui;
        targets = FindTreasureTargets(playerGui);
        if (targets == null)
            throw new InvalidOperationException("Could not find List/Weight in PlayerGui.");

        CacheTargetPaths();
    }

    private void RefreshAppraiseTarget(bool force)
    {
        if (targets == null || playerGuiAddress == 0)
            return;

        if (!force && targets.AppraiseButton != 0 && IsAddressStillExpected(targets.AppraiseButton, "Appraise", "Button"))
            return;

        ulong resolvedByPath = ResolveByPathHint(playerGuiAddress, appraisePathHint, "Appraise", "Button", false);
        if (resolvedByPath != 0)
        {
            targets = new TreasureTargets(targets.ListFrame, resolvedByPath, targets.WeightTextLabel);
            return;
        }

        TreasureTargets? refreshed = FindTreasureTargets(playerGuiAddress);
        if (refreshed != null && refreshed.AppraiseButton != 0)
        {
            targets = new TreasureTargets(targets!.ListFrame, refreshed.AppraiseButton, targets.WeightTextLabel);
            appraisePathHint = MakePathHint(refreshed.AppraiseButton);
        }
    }

    private bool EnsureTargetAddressesHealthy()
    {
        if (targets == null)
            return false;

        if (!IsAddressStillExpected(targets.ListFrame, "List", "Frame"))
        {
            ulong resolved = ResolveByPathHint(playerGuiAddress, listPathHint, "List", "Frame", true);
            if (resolved == 0)
                return false;

            targets = new TreasureTargets(resolved, targets.AppraiseButton, targets.WeightTextLabel);
            listPathHint = MakePathHint(resolved);
            rowPoints = null;
        }

        if (!EnsureWeightAddressHealthy())
            return false;

        if (!IsAddressStillExpected(targets.AppraiseButton, "Appraise", "Button"))
            RefreshAppraiseTarget(true);

        return targets.ListFrame != 0 && targets.WeightTextLabel != 0;
    }

    private bool EnsureWeightAddressHealthy()
    {
        if (targets == null || targets.WeightTextLabel == 0)
            return false;

        if (!IsAddressStillExpected(targets.WeightTextLabel, "Weight", "TextLabel"))
            return RefreshWeightTarget();

        string text = ReadGuiText(targets.WeightTextLabel);
        if (!IsSaneWeightText(text))
            return RefreshWeightTarget();

        return true;
    }

    private bool RefreshWeightTarget()
    {
        ulong resolved = ResolveByPathHint(playerGuiAddress, weightPathHint, "Weight", "TextLabel", false);
        if (resolved == 0)
        {
            TreasureTargets? refreshed = FindTreasureTargets(playerGuiAddress);
            if (refreshed == null || refreshed.WeightTextLabel == 0)
                return false;

            resolved = refreshed.WeightTextLabel;
            weightPathHint = MakePathHint(resolved);
        }

        targets = new TreasureTargets(targets!.ListFrame, targets!.AppraiseButton, resolved);
        return true;
    }

    private void CacheTargetPaths()
    {
        if (targets == null)
            return;

        listPathHint = targets.ListFrame != 0 ? MakePathHint(targets.ListFrame) : null;
        appraisePathHint = targets.AppraiseButton != 0 ? MakePathHint(targets.AppraiseButton) : null;
        weightPathHint = targets.WeightTextLabel != 0 ? MakePathHint(targets.WeightTextLabel) : null;
    }

    private bool IsAddressStillExpected(ulong instance, string name, string classNeedle)
    {
        if (!ProcessMemory.IsLikelyUserModeAddress(instance))
            return false;

        string actualName = ReadInstanceName(instance);
        string actualClass = ReadClassName(instance);
        if (!string.Equals(actualName, name, StringComparison.OrdinalIgnoreCase))
            return false;

        return ClassMatches(actualClass, classNeedle);
    }

    private ulong ResolveByPathHint(ulong root, string? pathHint, string expectedName, string classNeedle, bool requireUsableBounds)
    {
        if (root == 0 || string.IsNullOrWhiteSpace(pathHint))
            return 0;

        Queue<Tuple<ulong, int>> queue = new Queue<Tuple<ulong, int>>();
        HashSet<ulong> visited = new HashSet<ulong>();
        queue.Enqueue(Tuple.Create(root, 0));

        while (queue.Count > 0)
        {
            Tuple<ulong, int> item = queue.Dequeue();
            ulong instance = item.Item1;
            int depth = item.Item2;

            if (!visited.Add(instance))
                continue;

            if (IsAddressStillExpected(instance, expectedName, classNeedle))
            {
                string currentPath = MakePathHint(instance);
                if (string.Equals(currentPath, pathHint, StringComparison.OrdinalIgnoreCase))
                {
                    if (!requireUsableBounds)
                        return instance;

                    RectangleF bounds;
                    if (TryReadAnyBounds(instance, out bounds) && IsBoundsUsable(bounds))
                        return instance;
                }
            }

            if (depth >= 128)
                continue;

            foreach (ulong child in EnumerateChildren(instance))
                queue.Enqueue(Tuple.Create(child, depth + 1));
        }

        return 0;
    }

    private static bool IsSaneWeightText(string valueText)
    {
        if (string.IsNullOrWhiteSpace(valueText))
            return false;

        string trimmed = valueText.Trim();
        if (trimmed.Length > 32)
            return false;

        return ParseWeightValue(trimmed).HasValue;
    }

    private static bool SameWeightValue(string a, string b)
    {
        double? va = ParseWeightValue(a);
        double? vb = ParseWeightValue(b);
        if (!va.HasValue || !vb.HasValue)
            return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        return Math.Abs(va.Value - vb.Value) < 0.0001;
    }

    private static string PickBetterWeightText(string existing, string candidate)
    {
        double? a = ParseWeightValue(existing);
        double? b = ParseWeightValue(candidate);
        if (!a.HasValue)
            return candidate;
        if (!b.HasValue)
            return existing;

        return b.Value >= a.Value ? candidate : existing;
    }

    private TreasureTargets? FindTreasureTargets(ulong root)
    {
        TreasureTargets? scoped = FindTreasureTargetsByScopedNames(root);
        if (scoped != null)
            return scoped;

        List<TreasureTargetCandidate> lists = new List<TreasureTargetCandidate>();
        List<TreasureTargetCandidate> appraises = new List<TreasureTargetCandidate>();
        List<TreasureTargetCandidate> weights = new List<TreasureTargetCandidate>();

        Queue<Tuple<ulong, int>> queue = new Queue<Tuple<ulong, int>>();
        HashSet<ulong> visited = new HashSet<ulong>();
        queue.Enqueue(Tuple.Create(root, 0));

        while (queue.Count > 0)
        {
            Tuple<ulong, int> item = queue.Dequeue();
            ulong instance = item.Item1;
            int depth = item.Item2;

            if (!visited.Add(instance))
                continue;

            string name = ReadInstanceName(instance);
            string className = ReadClassName(instance);

            if (string.Equals(name, "List", StringComparison.OrdinalIgnoreCase)
                && string.Equals(className, "Frame", StringComparison.OrdinalIgnoreCase))
            {
                RectangleF bounds;
                if (TryReadAnyBounds(instance, out bounds) && IsBoundsUsable(bounds))
                    lists.Add(new TreasureTargetCandidate(instance, MakePathHint(instance), bounds));
            }

            if (string.Equals(name, "Appraise", StringComparison.OrdinalIgnoreCase)
                && className.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                RectangleF bounds;
                TryReadAnyBounds(instance, out bounds);
                appraises.Add(new TreasureTargetCandidate(instance, MakePathHint(instance), bounds));
            }

            if (string.Equals(name, "Weight", StringComparison.OrdinalIgnoreCase)
                && string.Equals(className, "TextLabel", StringComparison.OrdinalIgnoreCase))
            {
                RectangleF bounds;
                TryReadAnyBounds(instance, out bounds);
                weights.Add(new TreasureTargetCandidate(instance, MakePathHint(instance), bounds));
            }

            if (depth >= 128)
                continue;

            foreach (ulong child in EnumerateChildren(instance))
                queue.Enqueue(Tuple.Create(child, depth + 1));
        }

        return ChooseTreasureTargets(lists, appraises, weights);
    }

    private TreasureTargets? FindTreasureTargetsByScopedNames(ulong playerGui)
    {
        List<TreasureTargetCandidate> lists = CollectGuiCandidates(playerGui, "List", "Frame", true, false);
        List<TreasureTargetCandidate> appraises = CollectGuiCandidates(playerGui, "Appraise", "Button", false, false);
        List<TreasureTargetCandidate> weights = CollectGuiCandidates(playerGui, "Weight", "TextLabel", false, true);
        if (lists.Count == 0 || weights.Count == 0)
            return null;

        TreasureTargets? best = null;
        double bestScore = double.MaxValue;
        foreach (TreasureTargetCandidate list in lists)
        {
            foreach (TreasureTargetCandidate weight in weights)
            {
                if (!WeightLooksLikeKg(weight))
                    continue;

                if (appraises.Count == 0)
                {
                    double scoreNoAppraise = ScoreScopedSet(list, null, weight);
                    if (scoreNoAppraise < bestScore)
                    {
                        bestScore = scoreNoAppraise;
                        best = new TreasureTargets(list.Address, 0, weight.Address);
                    }
                    continue;
                }

                foreach (TreasureTargetCandidate appraise in appraises)
                {
                    double score = ScoreScopedSet(list, appraise, weight);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new TreasureTargets(list.Address, appraise.Address, weight.Address);
                    }
                }
            }
        }

        if (best != null)
            return best;

        return ChooseTreasureTargets(lists, appraises, weights);
    }

    private List<TreasureTargetCandidate> CollectGuiCandidates(ulong root, string name, string classNeedle, bool requireUsableBounds, bool preferKgText)
    {
        List<TreasureTargetCandidate> result = new List<TreasureTargetCandidate>();
        Queue<Tuple<ulong, int>> queue = new Queue<Tuple<ulong, int>>();
        HashSet<ulong> visited = new HashSet<ulong>();
        queue.Enqueue(Tuple.Create(root, 0));

        while (queue.Count > 0)
        {
            Tuple<ulong, int> item = queue.Dequeue();
            ulong instance = item.Item1;
            int depth = item.Item2;

            if (!visited.Add(instance))
                continue;

            if (string.Equals(ReadInstanceName(instance), name, StringComparison.OrdinalIgnoreCase)
                && ClassMatches(ReadClassName(instance), classNeedle))
            {
                RectangleF bounds;
                bool usable = TryReadAnyBounds(instance, out bounds) && IsBoundsUsable(bounds);
                if (requireUsableBounds && !usable)
                {
                    // Skip unusable bounds for required-on-screen candidates like List.
                }
                else
                {
                    string path = MakePathHint(instance);
                    TreasureTargetCandidate candidate = new TreasureTargetCandidate(instance, path, bounds);
                    if (!preferKgText || WeightLooksLikeKg(candidate))
                        result.Add(candidate);
                }
            }

            if (depth >= 128)
                continue;

            foreach (ulong child in EnumerateChildren(instance))
                queue.Enqueue(Tuple.Create(child, depth + 1));
        }

        return result;
    }

    private bool WeightLooksLikeKg(TreasureTargetCandidate candidate)
    {
        if (candidate == null || candidate.Address == 0)
            return false;

        string text = ReadGuiText(candidate.Address);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string lower = text.Trim().ToLowerInvariant();
        if (lower.Contains("kg"))
            return ParseWeightValue(lower).HasValue;

        return ParseWeightValue(lower).HasValue;
    }

    private static double ScoreScopedSet(TreasureTargetCandidate list, TreasureTargetCandidate? appraise, TreasureTargetCandidate weight)
    {
        double score = ScoreTargetSet(list, appraise, weight);

        int listWeightPrefix = CommonPrefixLength(list.PathHint, weight.PathHint);
        score -= Math.Min(80, listWeightPrefix) * 0.4;

        if (appraise != null)
        {
            int listAppraisePrefix = CommonPrefixLength(list.PathHint, appraise.PathHint);
            int weightAppraisePrefix = CommonPrefixLength(weight.PathHint, appraise.PathHint);
            score -= Math.Min(80, listAppraisePrefix) * 0.4;
            score -= Math.Min(80, weightAppraisePrefix) * 0.4;
        }

        return score;
    }

    private ulong FindNearestCommonGuiAncestor(ulong first, ulong second)
    {
        HashSet<ulong> firstAncestors = new HashSet<ulong>();
        ulong current = first;
        for (int i = 0; i < 64 && ProcessMemory.IsLikelyUserModeAddress(current); i++)
        {
            firstAncestors.Add(current);
            ulong parent = memory.ReadPtr(current + offsets.Get("Instance", "Parent"));
            if (!ProcessMemory.IsLikelyUserModeAddress(parent) || parent == current)
                break;

            current = parent;
        }

        current = second;
        for (int i = 0; i < 64 && ProcessMemory.IsLikelyUserModeAddress(current); i++)
        {
            if (firstAncestors.Contains(current))
                return current;

            ulong parent = memory.ReadPtr(current + offsets.Get("Instance", "Parent"));
            if (!ProcessMemory.IsLikelyUserModeAddress(parent) || parent == current)
                break;

            current = parent;
        }

        return 0;
    }

    private static bool ClassMatches(string className, string classNeedle)
    {
        if (string.IsNullOrEmpty(classNeedle))
            return true;

        if (classNeedle.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
            return className != null && className.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0;

        return string.Equals(className, classNeedle, StringComparison.OrdinalIgnoreCase);
    }

    private TreasureTargets? ChooseTreasureTargets(
        List<TreasureTargetCandidate> lists,
        List<TreasureTargetCandidate> appraises,
        List<TreasureTargetCandidate> weights)
    {
        TreasureTargets? best = null;
        double bestScore = double.MaxValue;

        foreach (TreasureTargetCandidate list in lists)
        {
            if (appraises.Count == 0)
            {
                foreach (TreasureTargetCandidate weight in weights)
                {
                    double score = ScoreTargetSet(list, null, weight);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new TreasureTargets(list.Address, 0, weight.Address);
                    }
                }
            }
            else
            {
                foreach (TreasureTargetCandidate appraise in appraises)
                {
                    foreach (TreasureTargetCandidate weight in weights)
                    {
                        double score = ScoreTargetSet(list, appraise, weight);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = new TreasureTargets(list.Address, appraise.Address, weight.Address);
                        }
                    }
                }
            }
        }

        return best;
    }

    private static double ScoreTargetSet(TreasureTargetCandidate list, TreasureTargetCandidate? appraise, TreasureTargetCandidate weight)
    {
        double score = 0.0;
        if (appraise != null)
            score += CommonPrefixLength(list.PathHint, appraise.PathHint) > 0 ? -20.0 : 0.0;
        score += CommonPrefixLength(list.PathHint, weight.PathHint) > 0 ? -20.0 : 0.0;

        if (appraise != null && !appraise.Bounds.IsEmpty)
            score += DistanceBetweenCenters(list.Bounds, appraise.Bounds) * 0.01;

        if (!weight.Bounds.IsEmpty)
            score += DistanceBetweenCenters(list.Bounds, weight.Bounds) * 0.01;

        score -= Math.Min(200.0, list.Bounds.Width) * 0.01;
        return score;
    }

    private static int CommonPrefixLength(string? a, string? b)
    {
        if (a == null || b == null)
            return 0;

        int max = Math.Min(a.Length, b.Length);
        int count = 0;
        while (count < max && a[count] == b[count])
            count++;
        return count;
    }

    private static double DistanceBetweenCenters(RectangleF a, RectangleF b)
    {
        double ax = a.Left + a.Width * 0.5;
        double ay = a.Top + a.Height * 0.5;
        double bx = b.Left + b.Width * 0.5;
        double by = b.Top + b.Height * 0.5;
        double dx = ax - bx;
        double dy = ay - by;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private string MakePathHint(ulong instance)
    {
        List<string> parts = new List<string>();
        ulong current = instance;
        for (int i = 0; i < 12 && ProcessMemory.IsLikelyUserModeAddress(current); i++)
        {
            string name = ReadInstanceName(current);
            if (!string.IsNullOrEmpty(name))
                parts.Add(name);

            ulong parent = memory.ReadPtr(current + offsets.Get("Instance", "Parent"));
            if (!ProcessMemory.IsLikelyUserModeAddress(parent) || parent == current)
                break;

            current = parent;
        }

        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    private bool TryReadAnyBounds(ulong instance, out RectangleF bounds)
    {
        bounds = RectangleF.Empty;
        Vector2 position;
        Vector2 size;
        if (!TryReadGuiBounds(instance, out position, out size))
            return false;

        Point clientOrigin = GetClientScreenOrigin(robloxWindow);
        bounds = new RectangleF(clientOrigin.X + position.X, clientOrigin.Y + position.Y, size.X, size.Y);
        return true;
    }

    private bool IsBoundsUsable(RectangleF bounds)
    {
        Rectangle clientRectangle = GetClientScreenRectangle(robloxWindow);
        return bounds.Width > 1.0f && bounds.Height > 1.0f && bounds.IntersectsWith(clientRectangle);
    }

    private ulong FindLocalPlayerGui(ulong dataModel)
    {
        ulong players = FindDescendantByClass(dataModel, "Players");
        if (players == 0)
            return 0;

        ulong localPlayer = memory.ReadPtr(players + offsets.Get("Player", "LocalPlayer"));
        if (!ProcessMemory.IsLikelyUserModeAddress(localPlayer))
            return 0;

        ulong playerGui = FindDescendantByClass(localPlayer, "PlayerGui");
        if (playerGui != 0)
            return playerGui;

        return FindDescendantByName(localPlayer, "PlayerGui");
    }

    private ulong FindDescendantByClass(ulong root, string className)
    {
        return FindDescendant(root, delegate(ulong instance)
        {
            return string.Equals(ReadClassName(instance), className, StringComparison.OrdinalIgnoreCase);
        });
    }

    private ulong FindDescendantByName(ulong root, string name)
    {
        return FindDescendant(root, delegate(ulong instance)
        {
            return string.Equals(ReadInstanceName(instance), name, StringComparison.OrdinalIgnoreCase);
        });
    }

    private ulong FindDescendant(ulong root, Predicate<ulong> predicate)
    {
        Queue<Tuple<ulong, int>> queue = new Queue<Tuple<ulong, int>>();
        HashSet<ulong> visited = new HashSet<ulong>();
        queue.Enqueue(Tuple.Create(root, 0));

        while (queue.Count > 0)
        {
            Tuple<ulong, int> item = queue.Dequeue();
            ulong instance = item.Item1;
            int depth = item.Item2;

            if (!visited.Add(instance))
                continue;

            if (predicate(instance))
                return instance;

            if (depth >= 128)
                continue;

            foreach (ulong child in EnumerateChildren(instance))
                queue.Enqueue(Tuple.Create(child, depth + 1));
        }

        return 0;
    }

    private IEnumerable<ulong> EnumerateChildren(ulong instance)
    {
        ulong childrenVector = memory.ReadPtr(instance + offsets.Get("Instance", "ChildrenStart"));
        if (!ProcessMemory.IsLikelyUserModeAddress(childrenVector))
            yield break;

        ulong start = memory.ReadPtr(childrenVector);
        ulong end = memory.ReadPtr(childrenVector + offsets.Get("Instance", "ChildrenEnd"));

        if (!LooksLikeVectorRange(start, end))
        {
            start = childrenVector;
            end = memory.ReadPtr(instance + offsets.Get("Instance", "ChildrenStart") + offsets.Get("Instance", "ChildrenEnd"));
        }

        if (!LooksLikeVectorRange(start, end))
            yield break;

        ulong thisOffset = offsets.Get("Instance", "This");
        for (ulong entry = start; entry < end; entry += 0x10UL)
        {
            ulong child = memory.ReadPtr(entry);
            if (!ProcessMemory.IsLikelyUserModeAddress(child))
                child = memory.ReadPtr(entry + thisOffset);

            if (ProcessMemory.IsLikelyUserModeAddress(child))
                yield return child;
        }
    }

    private static bool LooksLikeVectorRange(ulong start, ulong end)
    {
        return ProcessMemory.IsLikelyUserModeAddress(start)
            && ProcessMemory.IsLikelyUserModeAddress(end)
            && end >= start
            && end - start <= 1024UL * 1024UL
            && ((end - start) % 0x10UL) == 0;
    }

    private RectangleF ReadScreenBounds(ulong instance)
    {
        RectangleF bounds;
        if (!TryReadUsableBounds(instance, out bounds))
            throw new InvalidOperationException("Could not read usable bounds for 0x" + instance.ToString("X", CultureInfo.InvariantCulture));

        return bounds;
    }

    private bool TryReadUsableBounds(ulong instance, out RectangleF bounds)
    {
        bounds = RectangleF.Empty;
        Vector2 position;
        Vector2 size;
        if (!TryReadGuiBounds(instance, out position, out size))
            return false;

        bool visible;
        if (TryReadBool(instance + offsets.Get("GuiObject", "Visible"), out visible) && !visible)
            return false;

        Point clientOrigin = GetClientScreenOrigin(robloxWindow);
        bounds = new RectangleF(clientOrigin.X + position.X, clientOrigin.Y + position.Y, size.X, size.Y);
        Rectangle clientRectangle = GetClientScreenRectangle(robloxWindow);
        return bounds.Width > 1.0f && bounds.Height > 1.0f && bounds.IntersectsWith(clientRectangle);
    }

    private List<List<Point>> GenerateRows(RectangleF bounds)
    {
        List<List<Point>> rows = new List<List<Point>>();
        for (int row = 0; row < Rows; row++)
        {
            List<Point> points = new List<Point>();
            float y = bounds.Top + ((row + 0.5f) * bounds.Height / Rows);
            for (int col = 0; col < Columns; col++)
            {
                float x = bounds.Left + ((col + 0.5f) * bounds.Width / Columns);
                points.Add(new Point((int)Math.Round(x), (int)Math.Round(y)));
            }

            rows.Add(points);
        }

        return rows;
    }

    private string ReadGuiText(ulong instance)
    {
        ulong textOffset = offsets.Get("GuiObject", "Text");
        ulong textString = memory.ReadPtr(instance + textOffset);
        string value = memory.ReadRobloxString(textString);
        if (!string.IsNullOrEmpty(value))
            return value;

        return memory.ReadRobloxString(instance + textOffset);
    }

    private string ReadInstanceName(ulong instance)
    {
        ulong nameString = memory.ReadPtr(instance + offsets.Get("Instance", "Name"));
        return memory.ReadRobloxString(nameString);
    }

    private string ReadClassName(ulong instance)
    {
        ulong descriptor = memory.ReadPtr(instance + offsets.Get("Instance", "ClassDescriptor"));
        ulong classNameString = memory.ReadPtr(descriptor + offsets.Get("Instance", "ClassName"));
        return memory.ReadRobloxString(classNameString);
    }

    private bool TryReadGuiBounds(ulong instance, out Vector2 position, out Vector2 size)
    {
        position = new Vector2();
        size = new Vector2();

        if (!ProcessMemory.IsLikelyUserModeAddress(instance))
            return false;

        if (!TryReadVector2(instance + offsets.Get("GuiBase2D", "AbsolutePosition"), out position))
            return false;

        if (!TryReadVector2(instance + offsets.Get("GuiBase2D", "AbsoluteSize"), out size))
            return false;

        ApplyWindowDpiScale(ref position, ref size);

        return IsReasonableVector(position, 20000.0f)
            && IsReasonableVector(size, 10000.0f)
            && size.X > 1.0f
            && size.Y > 1.0f;
    }

    private void ApplyWindowDpiScale(ref Vector2 position, ref Vector2 size)
    {
        if (robloxWindow == IntPtr.Zero)
            return;

        uint dpi;
        try
        {
            dpi = GetDpiForWindow(robloxWindow);
        }
        catch
        {
            return;
        }

        if (dpi <= 96)
            return;

        float scale = dpi / 96f;
        position = new Vector2(position.X * scale, position.Y * scale);
        size = new Vector2(size.X * scale, size.Y * scale);
    }

    private bool TryReadBool(ulong address, out bool value)
    {
        value = false;
        byte[]? bytes = memory.ReadBytes(address, 1);
        if (bytes == null || bytes.Length == 0)
            return false;

        value = bytes[0] != 0;
        return true;
    }

    private bool TryReadVector2(ulong address, out Vector2 value)
    {
        value = new Vector2();
        byte[]? bytes = memory.ReadBytes(address, 8);
        if (bytes == null)
            return false;

        value = new Vector2(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4));
        return true;
    }

    private static bool IsReasonableVector(Vector2 value, float maxAbs)
    {
        return !float.IsNaN(value.X)
            && !float.IsNaN(value.Y)
            && !float.IsInfinity(value.X)
            && !float.IsInfinity(value.Y)
            && Math.Abs(value.X) <= maxAbs
            && Math.Abs(value.Y) <= maxAbs;
    }

    private static Point GetCenter(RectangleF bounds)
    {
        return new Point((int)Math.Round(bounds.Left + bounds.Width * 0.5f), (int)Math.Round(bounds.Top + bounds.Height * 0.5f));
    }

    private static string FormatBounds(RectangleF bounds)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:0}x{1:0}+{2:0}+{3:0}", bounds.Width, bounds.Height, bounds.Left, bounds.Top);
    }

    private static Point GetClientScreenOrigin(IntPtr window)
    {
        Point point = new Point(0, 0);
        ClientToScreen(window, ref point);
        return point;
    }

    private static Rectangle GetClientScreenRectangle(IntPtr window)
    {
        Rect client;
        if (!GetClientRect(window, out client))
            return Rectangle.Empty;

        Point origin = GetClientScreenOrigin(window);
        return new Rectangle(origin.X, origin.Y, Math.Max(0, client.Right - client.Left), Math.Max(0, client.Bottom - client.Top));
    }

    private void DisposeMemory()
    {
        if (memory != null)
        {
            memory.Dispose();
            memory = null!;
        }

        process = null!;
        targets = null;
        Reset(Settings);
    }

    public void Dispose()
    {
        DisposeMemory();
    }

    private static Process FindRobloxProcess()
    {
        Process? process = Process.GetProcessesByName("RobloxPlayerBeta")
            .OrderByDescending(p => SafeStartTimeTicks(p))
            .FirstOrDefault();

        if (process != null)
            return process;

        process = Process.GetProcesses()
            .Where(p => p.ProcessName.IndexOf("Roblox", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(p => SafeStartTimeTicks(p))
            .FirstOrDefault();

        if (process == null)
            throw new InvalidOperationException("No running Roblox process was found.");

        return process;
    }

    private static long SafeStartTimeTicks(Process process)
    {
        try { return process.StartTime.Ticks; }
        catch { return 0; }
    }

    private static ulong GetMainModuleBase(Process process)
    {
        try
        {
            return unchecked((ulong)process.MainModule!.BaseAddress.ToInt64());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not read Roblox's main module base address. Run this as x64 and, if needed, as administrator.", ex);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

internal sealed class TreasureAppraiseSettings
{
    public TreasureAppraiseSettings(
        double clickDelaySeconds,
        double? targetWeight)
    {
        ClickDelaySeconds = clickDelaySeconds;
        TargetWeight = targetWeight;
    }

    public double ClickDelaySeconds { get; private set; }
    public double? TargetWeight { get; private set; }
}

internal sealed class TreasureTargets
{
    public TreasureTargets(ulong listFrame, ulong appraiseButton, ulong weightTextLabel)
    {
        ListFrame = listFrame;
        AppraiseButton = appraiseButton;
        WeightTextLabel = weightTextLabel;
    }

    public ulong ListFrame { get; private set; }
    public ulong AppraiseButton { get; private set; }
    public ulong WeightTextLabel { get; private set; }
}

internal sealed class TreasureTargetCandidate
{
    public TreasureTargetCandidate(ulong address, string pathHint, RectangleF bounds)
    {
        Address = address;
        PathHint = pathHint ?? string.Empty;
        Bounds = bounds;
    }

    public ulong Address { get; private set; }
    public string PathHint { get; private set; }
    public RectangleF Bounds { get; private set; }
}

internal enum TreasureAppraisePhase
{
    Setup,
    FindAppraise,
    WaitForAppraiseGone,
    WaitAfterFinalRowClick,
    ClickRows
}

internal sealed class TreasureStepResult
{
    private TreasureStepResult(bool completed, string finalValue)
    {
        Completed = completed;
        FinalValue = finalValue;
    }

    public bool Completed { get; private set; }
    public string FinalValue { get; private set; }

    public static readonly TreasureStepResult Running = new TreasureStepResult(false, string.Empty);

    public static TreasureStepResult Complete(string finalValue)
    {
        return new TreasureStepResult(true, finalValue ?? string.Empty);
    }
}

internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal static class MouseInput
{
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    public static void ClickAt(int x, int y)
    {
        NotifyCursorHover(x, y);
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(8);
        LeftDown();
        System.Threading.Thread.Sleep(35);
        SetCursorPos(x, y);
        LeftUp();
    }

    private static void NotifyCursorHover(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(MouseEventMove, 1, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(10);
        mouse_event(MouseEventMove, -1, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(10);
        SetCursorPos(x, y);
    }

    private const uint MouseEventMove = 0x0001;

    public static void LeftDown()
    {
        if (!SendMouseButton(MouseEventLeftDown))
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
    }

    public static void LeftUp()
    {
        if (!SendMouseButton(MouseEventLeftUp))
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    private static bool SendMouseButton(uint flags)
    {
        Input[] inputs = new Input[1];
        inputs[0].Type = InputMouse;
        inputs[0].Mouse.Flags = flags;
        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }

    private const uint InputMouse = 0;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Input
{
    public uint Type;
    public MouseInputData Mouse;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInputData
{
    public int X;
    public int Y;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInfo;
}

internal struct Vector2
{
    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float X;
    public float Y;
}

internal sealed class OffsetTable
{
    private readonly IOffsetsSource source;

    public OffsetTable(IOffsetsSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public ulong Get(string namespaceName, string valueName)
    {
        string key = namespaceName + "." + valueName;
        if (!source.TryGetOffset(key, out var value))
            throw new KeyNotFoundException("Missing offset: Offsets::" + namespaceName + "::" + valueName);

        return value;
    }
}

internal sealed class ProcessMemory : IDisposable
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private readonly IntPtr handle;

    private ProcessMemory(IntPtr handle)
    {
        this.handle = handle;
    }

    public static ProcessMemory Open(int pid)
    {
        IntPtr handle = OpenProcess(ProcessVmRead | ProcessQueryInformation | ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("OpenProcess failed. Win32 error: " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));

        return new ProcessMemory(handle);
    }

    public ulong ReadPtr(ulong address)
    {
        return ReadUInt64(address);
    }

    public ulong ReadUInt64(ulong address)
    {
        byte[]? buffer = ReadBytes(address, 8);
        if (buffer == null)
            return 0;

        return BitConverter.ToUInt64(buffer, 0);
    }

    public string ReadRobloxString(ulong stringAddress)
    {
        if (!IsLikelyUserModeAddress(stringAddress))
            return string.Empty;

        ulong length = ReadUInt64(stringAddress + 0x10);
        if (length == 0 || length > 512)
            return string.Empty;

        ulong dataAddress = length >= 16 ? ReadPtr(stringAddress) : stringAddress;
        if (!IsLikelyUserModeAddress(dataAddress))
            return string.Empty;

        byte[]? bytes = ReadBytes(dataAddress, (int)Math.Min(length, 512));
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        int terminator = Array.IndexOf(bytes, (byte)0);
        int count = terminator >= 0 ? terminator : bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, count);
    }

    public byte[]? ReadBytes(ulong address, int count)
    {
        if (!IsLikelyUserModeAddress(address) || count <= 0)
            return null;

        byte[] buffer = new byte[count];
        IntPtr bytesRead;
        bool ok = ReadProcessMemory(handle, new IntPtr(unchecked((long)address)), buffer, buffer.Length, out bytesRead);

        if (!ok || bytesRead.ToInt64() != count)
            return null;

        return buffer;
    }

    public static bool IsLikelyUserModeAddress(ulong address)
    {
        return address >= 0x10000UL && address < 0x0000800000000000UL;
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
            CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, int size, out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

