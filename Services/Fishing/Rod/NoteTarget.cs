namespace Client.Services.Fishing;

// A note read from a Pinion's Aria reel: Sx = X scale (0..1), Sy = Y scale.
internal readonly record struct NoteTarget(double Sx, double Sy);
