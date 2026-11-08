namespace ModePuzzle
{
    /// <summary>Strings from this mod's Localization/*.csv (current locale, then en).</summary>
    static class Loc
    {
        public static string T(string key, params object[] args) =>
            ModHelper.GetLocalized(ModePuzzleMod.ModId, key, args);
    }
}
