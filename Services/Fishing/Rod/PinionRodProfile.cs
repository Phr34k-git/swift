using System;

namespace Client.Services.Fishing;

// Port of AHK PinionController (Fish.ahk). Counts notes that pass through the
// playerbar; after 7 it enters "resonance" and tracks the note directly.
// Otherwise it targets the fish/note midpoint when the note is close enough.
internal sealed class PinionRodProfile : RodProfile
{
    private const double NoteDeadzone = -19.5;

    private int _notesCaught;
    private bool _noteCounted;
    private bool _resonanceActive;

    public override RodKind Kind => RodKind.Pinion;

    public override void Reset()
    {
        _notesCaught = 0;
        _noteCounted = false;
        _resonanceActive = false;
    }

    public override ReelMetrics AdjustTarget(ReelMetrics metrics, NoteTarget? note)
    {
        if (note is not { } n)
        {
            return metrics;
        }

        if (n.Sy < NoteDeadzone)
        {
            return metrics;
        }

        UpdateNoteCount(n, metrics);

        if (_resonanceActive)
        {
            return metrics with { FishCenter = n.Sx };
        }

        var halfWidth = metrics.PlayerbarWidth / 2.0;
        var both = GetBothTargets(metrics.FishCenter, n.Sx, halfWidth);
        return metrics with { FishCenter = both ?? n.Sx };
    }

    private void UpdateNoteCount(NoteTarget note, ReelMetrics metrics)
    {
        // Counting window: only count if latch is clear and we're in the Y range.
        if (!_noteCounted && note.Sy >= -0.8 && note.Sy <= 0.53)
        {
            if (IsNoteInPlayerBar(note.Sx, metrics, 0.1))
            {
                _noteCounted = true;
                _notesCaught += 1;
            }
            else
            {
                // Missed note (outside bar during counting window): reset and break resonance.
                _notesCaught = 0;
                _resonanceActive = false;
                _noteCounted = true;
            }
        }

        // Latch clearing: any note below -8 clears the per-pass latch.
        if (note.Sy < -8)
        {
            _noteCounted = false;
        }

        // Activate resonance if we've caught 7 notes.
        if (_notesCaught >= 7)
        {
            _resonanceActive = true;
        }
    }

    private static double? GetBothTargets(double fishX, double noteX, double halfWidth)
    {
        var distance = Math.Abs(noteX - fishX);
        var fullWidth = halfWidth * 2.0;
        if (distance > fullWidth)
        {
            return null;
        }

        return (fishX + noteX) / 2.0;
    }

    private static bool IsNoteInPlayerBar(double x, ReelMetrics metrics, double padding)
    {
        var halfWidth = metrics.PlayerbarWidth / 2.0;
        return x >= metrics.PlayerbarCenter - halfWidth - padding &&
               x <= metrics.PlayerbarCenter + halfWidth + padding;
    }
}
