using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
public class NumberInCanvas
{
    private static readonly Dictionary<string, int> KeyTextHashCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<KeyCode, int> KeyCodeHashCache = new();

    public int Number { get; private set; }
    public Color Color { get; private set; }
    public int Seed { get; private set; }

    public NumberInCanvas(int initialSeed)
    {
        Seed = initialSeed;
    }

    public void NextGeneration(string keyPressed)
    {
        Seed = CalculateNewSeed(Seed, GetConsistentHashCode(keyPressed));
        GenerateNumberAndColor(Seed);
    }

    public void NextGeneration(KeyCode keyCode)
    {
        Seed = CalculateNewSeed(Seed, GetConsistentHashCode(keyCode));
        GenerateNumberAndColor(Seed);
    }

    private void GenerateNumberAndColor(int seed)
    {
        System.Random prng = new System.Random(seed);

        Number = prng.Next(10000000, 99999999);
        Color = new Color((float)prng.NextDouble(), (float)prng.NextDouble(), (float)prng.NextDouble(),0.5f);

    }
    private static int GetConsistentHashCode(KeyCode keyCode)
    {
        if (KeyCodeHashCache.TryGetValue(keyCode, out int hash))
        {
            return hash;
        }

        hash = GetConsistentHashCode(keyCode.ToString());
        KeyCodeHashCache[keyCode] = hash;
        return hash;
    }

    private static int GetConsistentHashCode(string str)
    {
        if (str == null)
        {
            throw new ArgumentNullException(nameof(str));
        }

        if (KeyTextHashCache.TryGetValue(str, out int hash))
        {
            return hash;
        }

        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(str);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            hash = BitConverter.ToInt32(hashBytes, 0);
        }

        KeyTextHashCache[str] = hash;
        return hash;
    }
    private int CalculateNewSeed(int currentSeed, int keyModifier)
    {
        return currentSeed * 31 + keyModifier;
    }



}
