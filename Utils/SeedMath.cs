namespace ReplayLogger
{
    internal static class SeedMath
    {
        internal static int CombineSeed(int currentSeed, int modifier)
        {
            return currentSeed * 31 + modifier;
        }
    }
}
