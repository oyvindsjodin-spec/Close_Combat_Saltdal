namespace Saltdal.Sim;

/// <summary>
/// A weapon carried by one man. For small arms, Accuracy is the chance per
/// round of hitting a standing man in the open at 100 m, for a calm, average
/// shooter on a range; in battle only Simulation.CombatAccuracy of it is left. A weapon with a Shell is a mortar: it lobs bombs that explode
/// where they land, needs no line of sight of its own, and has a minimum range.
/// </summary>
public sealed record Weapon(
    string Name,
    float Range,              // metres; beyond this the weapon does not fire
    int Burst,                // rounds per trigger pull
    float RoundInterval,      // seconds between rounds in a burst
    float AimTime,            // seconds between bursts
    float Accuracy,
    float Suppression,        // stress added per round landing next to a man
    float SuppressionRadius,  // metres
    int Ammo,                 // rounds carried
    Explosive? Shell = null,
    float MinRange = 0f)
{
    public bool IsIndirect => Shell != null;

    // British
    public static readonly Weapon LeeEnfield = new("Rifle No. 1 Mk III", 600, 1, 0f, 3.0f, 0.35f, 6f, 5f, 100);
    public static readonly Weapon Bren = new("Bren gun", 800, 5, 0.12f, 2.5f, 0.30f, 5f, 8f, 600);
    public static readonly Weapon Mortar2Inch = new("2-inch mortar", 460, 1, 0f, 6f, 0f, 0f, 0f, 12, Explosive.Mortar2Inch, 90f);
    public static readonly Weapon Mortar3Inch = new("3-inch mortar", 1500, 1, 0f, 6f, 0f, 0f, 0f, 20, Explosive.Mortar81, 120f);

    // Norwegian
    public static readonly Weapon Krag = new("Krag-Jørgensen", 600, 1, 0f, 3.2f, 0.35f, 6f, 5f, 80);
    public static readonly Weapon Mortar81Norwegian = new("8 cm bombekaster", 2000, 1, 0f, 6f, 0f, 0f, 0f, 20, Explosive.Mortar81, 150f);

    // German
    public static readonly Weapon Kar98k = new("Kar 98k", 600, 1, 0f, 3.0f, 0.35f, 6f, 5f, 90);
    public static readonly Weapon Mg34 = new("MG 34", 900, 10, 0.06f, 2.5f, 0.22f, 5f, 10f, 1200);
    public static readonly Weapon Mp40 = new("MP 40", 150, 5, 0.11f, 2.0f, 0.30f, 4f, 5f, 192);
    public static readonly Weapon Grw34 = new("8 cm Granatwerfer 34", 2000, 1, 0f, 6f, 0f, 0f, 0f, 40, Explosive.Mortar81, 150f);
}
