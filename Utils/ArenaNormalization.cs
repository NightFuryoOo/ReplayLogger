namespace ReplayLogger
{
    internal static class ArenaNormalization
    {
        internal static string NormalizeStrict(string arenaName)
        {
            return string.IsNullOrEmpty(arenaName) ? "UnknownArena" : arenaName;
        }

        internal static string NormalizeLenient(string arenaName)
        {
            return string.IsNullOrWhiteSpace(arenaName) ? "UnknownArena" : arenaName;
        }
    }
}
