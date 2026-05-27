using System.Collections.Generic;

namespace Client.Services;

/// <summary>
/// Maps server-side lockout reason codes to user-facing title and subtext strings.
/// </summary>
public static class LockedOutCopy
{
    public sealed record Entry(string Title, string Subtext);

    private static readonly IReadOnlyDictionary<string, Entry> Map =
        new Dictionary<string, Entry>
        {
            ["revoked"] = new(
                "Access Revoked",
                "Your access has been revoked. Contact support if you believe this is a mistake."),

            ["banned"] = new(
                "Account Banned",
                "You've been permanently banned from OpenMacro. Contact support if you believe this is a mistake."),

            ["hwid_mismatch"] = new(
                "Device Not Recognized",
                "This device doesn't match the one on file for your account. Log in from your usual machine, or — if this is your usual machine — contact support to re-register it."),

            ["chargeback"] = new(
                "Nice Try, accountant",
                "Your access has been suspended due to a disputed payment. Reverse the chargeback to regain access."),

            ["compromised"] = new(
                "Account Compromised",
                "Your account was compromised, so we've locked it down. Contact support to recover your account."),

            ["unlicensed"] = new(
                "Unlicensed Access",
                "Your account isn't linked to any purchases."),

            ["maintenance"] = new(
                "Maintenance In Progress",
                "OpenMacro is temporarily offline while we tune things up. Please check back shortly."),
        };

    /// <summary>
    /// Returns the copy entry for the given reason, falling back to "revoked" for unknown or null reasons.
    /// </summary>
    public static Entry Resolve(string? reason) =>
        reason is not null && Map.TryGetValue(reason, out var entry) ? entry : Map["revoked"];
}
