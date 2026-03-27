namespace MultiplayerCard.Extensions;

public static class StringExtensions
{
    public static string CardImagePath(this string path) =>
        "res://" + Path.Join(MainFile.ModId, "images", "card_portraits", path).Replace("\\", "/");

    public static string BigCardImagePath(this string path) =>
        "res://" + Path.Join(MainFile.ModId, "images", "card_portraits", "big", path).Replace("\\", "/");
}
