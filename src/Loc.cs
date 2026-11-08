namespace ModePuzzle
{
    /// <summary>Strings from this mod's Localization/*.csv (current locale, then en).</summary>
    static class Loc
    {
        public static string T(string key, params object[] args)
        {
#if DEV
            if (Dev.DevLoc.TryGet(key, out var format)) return args.Length == 0 ? format : string.Format(format, args);
#endif
            return ModHelper.GetLocalized(ModePuzzleMod.ModId, key, args);
        }
    }
}
