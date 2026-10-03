using System;
using System.IO;

internal static class TestPaths
{
    internal static string ApplicationBin
    {
        get
        {
            return Environment.GetEnvironmentVariable("AZUR_TEST_BIN") ??
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Outil_Azur_complet", "bin", "Debug"));
        }
    }

    internal static string Work
    {
        get
        {
            string path = Environment.GetEnvironmentVariable("AZUR_TEST_WORK") ??
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
