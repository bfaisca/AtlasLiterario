namespace AtlasLiterario.Domain.Common;

/// <summary>ISBN normalization and check-digit validation (ISBN-10 and ISBN-13).</summary>
public static class Isbn
{
    /// <summary>Removes separators (spaces, hyphens) and upper-cases a trailing 'x'. Returns null for blank input.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var chars = raw.Where(c => char.IsAsciiDigit(c) || c is 'X' or 'x')
            .Select(char.ToUpperInvariant)
            .ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }

    public static bool IsValidIsbn13(string? isbn)
    {
        if (isbn is null || isbn.Length != 13 || !isbn.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = isbn[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        var check = (10 - (sum % 10)) % 10;
        return check == isbn[12] - '0';
    }

    public static bool IsValidIsbn10(string? isbn)
    {
        if (isbn is null || isbn.Length != 10)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var c = isbn[i];
            int value;
            if (char.IsAsciiDigit(c))
            {
                value = c - '0';
            }
            else if (c == 'X' && i == 9)
            {
                value = 10;
            }
            else
            {
                return false;
            }

            sum += (10 - i) * value;
        }

        return sum % 11 == 0;
    }

    /// <summary>Converts a valid ISBN-10 to its ISBN-13 (978 prefix). Input must be normalized.</summary>
    public static string ConvertIsbn10To13(string isbn10)
    {
        if (!IsValidIsbn10(isbn10))
        {
            throw new ArgumentException("Invalid ISBN-10.", nameof(isbn10));
        }

        var body = "978" + isbn10[..9];
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = body[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return body + ((10 - (sum % 10)) % 10);
    }

    /// <summary>
    /// Returns a valid, normalized ISBN-13 from an ISBN-10 or ISBN-13 input, or null when the input is not a valid ISBN.
    /// </summary>
    public static string? ToIsbn13(string? raw)
    {
        var normalized = Normalize(raw);
        return normalized switch
        {
            null => null,
            { Length: 13 } when IsValidIsbn13(normalized) => normalized,
            { Length: 10 } when IsValidIsbn10(normalized) => ConvertIsbn10To13(normalized),
            _ => null,
        };
    }
}
