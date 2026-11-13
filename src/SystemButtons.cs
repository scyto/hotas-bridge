namespace HotasBridge;

/// <summary>
/// Buttons HOTAS Bridge acts on itself while it runs, in every game: the Xbox controller's Guide and Share. Windows or
/// Steam handle those and games never see them, so no binding file can give them a meaning. Because the app reads the
/// device directly, any button works, including ones a game can't read (AC7 stops at 32). Bindings live in
/// AppSettings.System, keyed by action key, not in game profiles.
/// </summary>
static class SystemButtons
{
    public const string Group = "System";
    public static readonly GameAction Guide = new("Guide", Group, "Guide (Xbox Game Bar)", ActionKind.Button);
    public static readonly GameAction Share = new("Share", Group, "Share (screenshot; hold: save clip)", ActionKind.Button);
    public static readonly IReadOnlyList<GameAction> All = new[] { Guide, Share };

    /// <summary>Milliseconds Share must be held to save a clip instead of taking a screenshot.</summary>
    public const long HoldMs = 600;

    public static string Tip(GameAction a) => (a == Share
        ? "A tap presses Win+Alt+PrtScn (Game Bar screenshot); holding it presses Win+Alt+G (save the last 30 seconds, " +
          "if Game Bar's background recording is on), like Share on an Xbox controller."
        : "Presses Win+G, which opens the Xbox Game Bar, like Guide on an Xbox controller.") +
        " Works in every game while HOTAS Bridge is running (minimized is fine). Games never see this button.";

    /// <summary>Win+G opens the Xbox Game Bar, which is what an Xbox controller's Guide button does on Windows.</summary>
    public static void OpenGameBar() => KeyInput.Press(KeyInput.Win, KeyInput.G);
    /// <summary>Win+Alt+PrtScn: Game Bar screenshot (Share, tapped).</summary>
    public static void Screenshot() => KeyInput.Press(KeyInput.Win, KeyInput.Alt, KeyInput.PrintScreen);
    /// <summary>Win+Alt+G: Game Bar "record what happened" (Share, held).</summary>
    public static void SaveClip() => KeyInput.Press(KeyInput.Win, KeyInput.Alt, KeyInput.G);
}
