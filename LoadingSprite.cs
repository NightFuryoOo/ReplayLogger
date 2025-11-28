using UnityEngine;
using System;
using System.IO;
using System.Security.Cryptography;

public class LoadingSprite
{
    public int SecondCount { get; private set; }

    public bool Flag { get; private set; }

    private int previousSecondCount = 0;
    private int currentSeed;
    private System.Random prng;

    public LoadingSprite(string str)
    {
        currentSeed = HashToInt(CalculateStringHash(str));
        prng = new System.Random(currentSeed);
        SecondCount = prng.Next(2, 5);
        Flag = false;
        previousSecondCount = SecondCount;
    }

    public void NextGeneration()
    {
        Flag = !Flag;

        currentSeed = CalculateNewSeed(currentSeed, previousSecondCount);

        SecondCount = GenerateFrameCount(currentSeed);

        if (SecondCount == previousSecondCount)
        {
            SecondCount = GenerateFrameCount(CalculateNewSeed(currentSeed, previousSecondCount + 1));
        }
        previousSecondCount = SecondCount;

    }

    private int CalculateNewSeed(int currentSeed, int previousValue)
    {
        return currentSeed * 31 + previousValue;
    }

    private int HashToInt(string hash)
    {
        int numBytes = Math.Min(4, hash.Length);
        int result = 0;
        for (int i = 0; i < numBytes; i++)
        {
            result = (result << 8) + hash[i];
        }
        return result;
    }
    private string CalculateStringHash(string str)
    {
        try
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] stringBytes = System.Text.Encoding.UTF8.GetBytes(str);

                byte[] hashBytes = sha256.ComputeHash(stringBytes);

                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }
        catch (Exception e)
        {
            Modding.Logger.Log($"Ошибка при вычислении хэша: {e.Message}");
            return "";
        }
    }
    private int GenerateFrameCount(int seed)
    {

        System.Random prng = new System.Random(seed);

        int randomOffset = prng.Next(-5, 6);


        int frameCount = prng.Next(2, 5) + randomOffset;

        frameCount = Mathf.Clamp(frameCount, 2, 5);

        return frameCount;
    }



}