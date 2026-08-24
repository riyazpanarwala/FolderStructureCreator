using System;
using System.Collections.Generic;

namespace FolderStructureCreator.Services;

/// <summary>
/// Compares strings so that numeric segments are sorted numerically (1, 2, 3 ... 10, 11)
/// followed by alphabetic strings (A-Z).
/// </summary>
public class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new NaturalStringComparer();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int lenX = x.Length;
        int lenY = y.Length;
        int i = 0, j = 0;

        while (i < lenX && j < lenY)
        {
            char c1 = x[i];
            char c2 = y[j];

            if (char.IsDigit(c1) && char.IsDigit(c2))
            {
                int startX = i;
                while (i < lenX && char.IsDigit(x[i])) i++;
                ReadOnlySpan<char> numXStr = x.AsSpan(startX, i - startX);

                int startY = j;
                while (j < lenY && char.IsDigit(y[j])) j++;
                ReadOnlySpan<char> numYStr = y.AsSpan(startY, j - startY);

                ReadOnlySpan<char> trimmedX = numXStr.TrimStart('0');
                ReadOnlySpan<char> trimmedY = numYStr.TrimStart('0');

                if (trimmedX.Length != trimmedY.Length)
                    return trimmedX.Length.CompareTo(trimmedY.Length);

                int numComp = trimmedX.CompareTo(trimmedY, StringComparison.Ordinal);
                if (numComp != 0) return numComp;

                if (numXStr.Length != numYStr.Length)
                    return numXStr.Length.CompareTo(numYStr.Length);
            }
            else
            {
                int comp = char.ToUpperInvariant(c1).CompareTo(char.ToUpperInvariant(c2));
                if (comp != 0) return comp;
                i++;
                j++;
            }
        }

        return lenX.CompareTo(lenY);
    }
}
