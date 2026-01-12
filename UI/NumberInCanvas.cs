using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
public class NumberInCanvas
{
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

    private void GenerateNumberAndColor(int seed)
    {
        System.Random prng = new System.Random(seed);

        Number = prng.Next(10000000, 99999999);
        Color = new Color((float)prng.NextDouble(), (float)prng.NextDouble(), (float)prng.NextDouble(),0.5f);

    }
    private int GetConsistentHashCode(string str)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(str);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            return BitConverter.ToInt32(hashBytes, 0);
        }
    }
    private int CalculateNewSeed(int currentSeed, int keyModifier)
    {
        return currentSeed * 31 + keyModifier;
    }



}