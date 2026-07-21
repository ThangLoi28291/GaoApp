namespace GaoApp.Application.Services.Invoices;

public static class VietnameseMoneyText
{
    private static readonly string[] Digits =
    {
        "không", "một", "hai", "ba", "bốn",
        "năm", "sáu", "bảy", "tám", "chín"
    };

    private static readonly string[] Units =
    {
        "", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"
    };

    public static string ReadMoney(decimal amount)
    {
        var rounded = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);

        if (rounded == 0)
            return "Không đồng";

        if (rounded < 0)
        {
            var positiveWords = ReadNumber(Math.Abs(rounded));
            return Capitalize("âm " + positiveWords + " đồng chẵn");
        }

        var words = ReadNumber(rounded);

        return Capitalize(words + " đồng chẵn");
    }

    private static string ReadNumber(long number)
    {
        var groups = new List<int>();

        while (number > 0)
        {
            groups.Add((int)(number % 1000));
            number /= 1000;
        }

        var parts = new List<string>();

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];

            if (group == 0)
                continue;

            var full = i < groups.Count - 1;
            var text = ReadThreeDigits(group, full);

            if (!string.IsNullOrWhiteSpace(Units[i]))
                text += " " + Units[i];

            parts.Add(text);
        }

        return string.Join(" ", parts).Replace("  ", " ").Trim();
    }

    private static string ReadThreeDigits(int number, bool full)
    {
        var hundred = number / 100;
        var ten = (number % 100) / 10;
        var unit = number % 10;

        var parts = new List<string>();

        if (hundred > 0 || full)
        {
            parts.Add(Digits[hundred]);
            parts.Add("trăm");
        }

        if (ten > 1)
        {
            parts.Add(Digits[ten]);
            parts.Add("mươi");

            if (unit == 1)
                parts.Add("mốt");
            else if (unit == 5)
                parts.Add("lăm");
            else if (unit > 0)
                parts.Add(Digits[unit]);
        }
        else if (ten == 1)
        {
            parts.Add("mười");

            if (unit == 5)
                parts.Add("lăm");
            else if (unit > 0)
                parts.Add(Digits[unit]);
        }
        else if (unit > 0)
        {
            if (hundred > 0 || full)
                parts.Add("lẻ");

            parts.Add(Digits[unit]);
        }

        return string.Join(" ", parts);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return char.ToUpper(value[0]) + value[1..];
    }
}