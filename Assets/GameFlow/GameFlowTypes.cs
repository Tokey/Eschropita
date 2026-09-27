using UnityEngine;

/// <summary>
/// Shared enums and small value types used across the scripted flow, the HUD,
/// the Mycari command system and Daffodil's scan.
/// </summary>
public enum GamePhase
{
    IntroVideo,
    ScanTutorial,
    MycariArrives,
    MycariDemo,
    PlayerPuzzle,
    MycariRepairs,
    TornadoIntro,
    TornadoStrike,
    Sandbox,
    ObeliskShield,
    ObeliskCore,
    Bunker,
    FinalReport,
    OutroVideo,
    End
}

/// <summary>What the player asks a recruited Mycari to do at a site.</summary>
public enum TaskCommand
{
    Repair,
    Dismantle
}

public enum ScanKind
{
    Mycari,
    Site,
    Obelisk,
    Shard,
    Bunker,
    Other
}

/// <summary>One thing Daffodil's scan found. The HUD draws one label per hit.</summary>
public struct ScanHit
{
    public Transform target;
    public ScanKind kind;
    public string label;     // e.g. "FIXER", "WINDMILL"
    public string detail;    // e.g. "Following", "72%"
    public float value01;    // bar under the label; < 0 hides it
    public Color color;
}
