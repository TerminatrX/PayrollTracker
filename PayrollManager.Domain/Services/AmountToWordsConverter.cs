namespace PayrollManager.Domain.Services;

/// <summary>
/// Converts a decimal amount (US currency) to English words.
/// Example: 503.16 -> "Five hundred three dollars and sixteen cents"
/// </summary>
public static class AmountToWordsConverter
{
    private static readonly string[] Ones = {
        "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
        "seventeen", "eighteen", "nineteen"
    };

    private static readonly string[] Tens = {
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    };

    /// <summary>
    /// Converts a decimal amount to words in US currency format.
    /// </summary>
    /// <param name="amount">The amount to convert (e.g., 503.16)</param>
    /// <returns>Words representation (e.g., "Five hundred three dollars and sixteen cents")</returns>
    public static string ConvertToWords(decimal amount)
    {
        if (amount == 0)
            return "Zero dollars and zero cents";

        var dollars = (long)Math.Floor(amount);
        var cents = (int)Math.Round((amount - dollars) * 100);

        var dollarsWords = ConvertNumberToWords(dollars);
        var centsWords = ConvertNumberToWords(cents);

        var result = dollarsWords;
        if (dollars == 1)
            result += " dollar";
        else
            result += " dollars";

        if (cents > 0)
        {
            result += " and " + centsWords;
            if (cents == 1)
                result += " cent";
            else
                result += " cents";
        }
        else
        {
            result += " and zero cents";
        }

        // Capitalize first letter
        if (result.Length > 0)
        {
            result = char.ToUpper(result[0]) + result.Substring(1);
        }

        return result;
    }

    private static string ConvertNumberToWords(long number)
    {
        if (number == 0)
            return "zero";

        if (number < 20)
            return Ones[number];

        if (number < 100)
        {
            var tens = number / 10;
            var ones = number % 10;
            if (ones == 0)
                return Tens[tens];
            return Tens[tens] + "-" + Ones[ones];
        }

        if (number < 1000)
        {
            var hundreds = number / 100;
            var remainder = number % 100;
            var result = Ones[hundreds] + " hundred";
            if (remainder > 0)
                result += " " + ConvertNumberToWords(remainder);
            return result;
        }

        if (number < 1000000)
        {
            var thousands = number / 1000;
            var remainder = number % 1000;
            var result = ConvertNumberToWords(thousands) + " thousand";
            if (remainder > 0)
                result += " " + ConvertNumberToWords(remainder);
            return result;
        }

        if (number < 1000000000)
        {
            var millions = number / 1000000;
            var remainder = number % 1000000;
            var result = ConvertNumberToWords(millions) + " million";
            if (remainder > 0)
                result += " " + ConvertNumberToWords(remainder);
            return result;
        }

        var billions = number / 1000000000;
        var billionsRemainder = number % 1000000000;
        var billionsResult = ConvertNumberToWords(billions) + " billion";
        if (billionsRemainder > 0)
            billionsResult += " " + ConvertNumberToWords(billionsRemainder);
        return billionsResult;
    }
}
