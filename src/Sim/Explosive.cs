namespace Saltdal.Sim;

/// <summary>
/// A shell, mortar bomb or aircraft bomb. Men inside KillRadius are almost
/// certainly hit; the chance falls off to nothing at WoundRadius. Everyone
/// inside SuppressionRadius is shaken.
/// </summary>
public sealed record Explosive(
    string Name,
    float KillRadius,
    float WoundRadius,
    float SuppressionRadius,
    float Suppression)
{
    public static readonly Explosive Mortar2Inch = new("2-inch mortar bomb", 2.5f, 8f, 15f, 25f);
    public static readonly Explosive Mortar81 = new("81 mm mortar bomb", 4f, 13f, 25f, 35f);
    public static readonly Explosive MountainGun75 = new("75 mm mountain gun shell", 4f, 14f, 25f, 35f);
    public static readonly Explosive Gun25Pounder = new("25-pounder shell", 5f, 17f, 30f, 40f);
    public static readonly Explosive Bomb250Kg = new("250 kg bomb", 14f, 35f, 70f, 70f);
}
