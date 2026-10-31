namespace Saltdal.Sim;

/// <summary>
/// A small-arms weapon. Accuracy is the chance per round of hitting a standing
/// man in the open at 100 m, for a calm, average shooter.
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
    int Ammo)                 // rounds carried
{
    // British
    public static readonly Weapon LeeEnfield = new("Rifle No. 1 Mk III", 600, 1, 0f, 3.0f, 0.35f, 6f, 3f, 100);
    public static readonly Weapon Bren = new("Bren gun", 800, 5, 0.12f, 2.5f, 0.30f, 5f, 6f, 600);

    // German
    public static readonly Weapon Kar98k = new("Kar 98k", 600, 1, 0f, 3.0f, 0.35f, 6f, 3f, 90);
    public static readonly Weapon Mg34 = new("MG 34", 900, 10, 0.06f, 2.5f, 0.22f, 5f, 8f, 1200);
    public static readonly Weapon Mp40 = new("MP 40", 150, 5, 0.11f, 2.0f, 0.30f, 4f, 4f, 192);
}
