using System.Numerics;

namespace GaoApp.Application.Services.Purchases;

/// <summary>
/// Exact, decimal-only conversion boundary for purchase receipts.
/// Product conversion factors are direct-to-base snapshots; conversion chains
/// and client-calculated values are never authoritative.
/// </summary>
public static class PurchaseReceiptQuantityConversionPolicy
{
    public const int QuantityScale = 3;
    public const int FactorScale = 4;
    public const decimal MaximumStoredQuantity = 999_999_999_999_999.999m;
    public const decimal MaximumStoredFactor = 99_999_999_999_999.9999m;

    private static readonly BigInteger QuantityStorageMultiplier = 1_000;
    private static readonly BigInteger FactorStorageMultiplier = 10_000;
    private static readonly BigInteger MaximumStoredQuantityUnits =
        new(999_999_999_999_999_999L);

    public static decimal NormalizeReceiptQuantity(decimal quantity)
    {
        if (quantity <= 0m)
            throw new PurchaseReceiptQuantityException("Số lượng nhận phải lớn hơn 0.");

        var normalized = RoundQuantity(quantity);
        if (normalized <= 0m)
            throw new PurchaseReceiptQuantityException("Số lượng nhận sau làm tròn không hợp lệ.");
        if (normalized > MaximumStoredQuantity)
            throw new PurchaseReceiptQuantityException("Số lượng nhận vượt giới hạn cho phép.");
        return normalized;
    }

    public static decimal ValidateFactor(decimal factor)
    {
        if (factor <= 0m)
            throw new PurchaseReceiptQuantityException("Hệ số quy đổi phải lớn hơn 0.");

        var normalized = decimal.Round(factor, FactorScale, MidpointRounding.AwayFromZero);
        if (normalized != factor)
            throw new PurchaseReceiptQuantityException("Hệ số quy đổi vượt độ chính xác cho phép.");
        if (normalized > MaximumStoredFactor)
            throw new PurchaseReceiptQuantityException("Hệ số quy đổi vượt giới hạn cho phép.");
        return normalized;
    }

    public static decimal ToCanonical(decimal receiptQuantity, decimal conversionFactor)
    {
        var quantity = NormalizeReceiptQuantity(receiptQuantity);
        var factor = ValidateFactor(conversionFactor);
        decimal raw;
        try
        {
            raw = checked(quantity * factor);
        }
        catch (OverflowException)
        {
            throw new PurchaseReceiptQuantityException("Số lượng sau quy đổi vượt giới hạn cho phép.");
        }

        var canonical = RoundQuantity(raw);
        if (canonical <= 0m)
            throw new PurchaseReceiptQuantityException("Số lượng sau quy đổi không hợp lệ.");
        if (canonical > MaximumStoredQuantity)
            throw new PurchaseReceiptQuantityException("Số lượng sau quy đổi vượt giới hạn cho phép.");
        return canonical;
    }

    public static decimal ToOrderedEquivalent(decimal canonicalQuantity, decimal orderedConversionFactor)
    {
        var canonical = NormalizeCanonicalQuantity(canonicalQuantity);
        var factor = ValidateFactor(orderedConversionFactor);
        var equivalent = RoundQuantity(canonical / factor);
        if (equivalent <= 0m)
            throw new PurchaseReceiptQuantityException("Số lượng quy đổi về đơn vị đặt hàng không hợp lệ.");
        if (equivalent > MaximumStoredQuantity)
            throw new PurchaseReceiptQuantityException("Số lượng quy đổi về đơn vị đặt hàng vượt giới hạn cho phép.");
        if (ToCanonical(equivalent, factor) != canonical)
            throw new PurchaseReceiptQuantityException(
                "Số lượng nhận không thể biểu diễn chính xác theo đơn vị đặt hàng.");
        return equivalent;
    }

    public static decimal EnsureCumulativeOrderedInvariant(
        decimal currentOrderedQuantity,
        decimal receiptOrderedEquivalent,
        decimal receiptCanonicalQuantity,
        decimal orderedConversionFactor)
    {
        if (currentOrderedQuantity < 0m)
            throw new PurchaseReceiptQuantityException("Số lượng đã nhận theo đơn vị đặt hàng không hợp lệ.");

        var currentOrdered = RoundQuantity(currentOrderedQuantity);
        var receiptOrdered = NormalizeReceiptQuantity(receiptOrderedEquivalent);
        var receiptCanonical = NormalizeCanonicalQuantity(receiptCanonicalQuantity);
        var factor = ValidateFactor(orderedConversionFactor);
        decimal updatedOrdered;
        decimal expectedCanonical;
        try
        {
            updatedOrdered = checked(currentOrdered + receiptOrdered);
            var currentCanonical = currentOrdered == 0m ? 0m : ToCanonical(currentOrdered, factor);
            expectedCanonical = checked(currentCanonical + receiptCanonical);
        }
        catch (OverflowException)
        {
            throw new PurchaseReceiptQuantityException("Số lượng nhận tích lũy vượt giới hạn cho phép.");
        }

        updatedOrdered = NormalizeCanonicalQuantity(updatedOrdered);
        expectedCanonical = NormalizeCanonicalQuantity(expectedCanonical);
        if (ToCanonical(updatedOrdered, factor) != expectedCanonical)
        {
            throw new PurchaseReceiptQuantityException(
                "Số lượng nhận tích lũy không thể biểu diễn chính xác theo đơn vị đặt hàng.");
        }

        return updatedOrdered;
    }

    public static decimal MaximumReceiptQuantity(decimal availableCanonicalQuantity, decimal receiptFactor)
    {
        if (availableCanonicalQuantity <= 0m) return 0m;
        var available = NormalizeCanonicalQuantity(availableCanonicalQuantity);
        var factor = ValidateFactor(receiptFactor);
        var maximumUnits = MaximumInputUnitsForCanonicalLimit(
            ToQuantityUnits(available), ToFactorUnits(factor));
        return FromQuantityUnits(maximumUnits);
    }

    public static decimal MaximumReceiptQuantity(
        decimal availableCanonicalQuantity,
        decimal receiptFactor,
        decimal orderedFactor)
    {
        if (availableCanonicalQuantity <= 0m) return 0m;
        var available = NormalizeCanonicalQuantity(availableCanonicalQuantity);
        var receipt = ValidateFactor(receiptFactor);
        var ordered = ValidateFactor(orderedFactor);
        var availableUnits = ToQuantityUnits(available);
        var receiptFactorUnits = ToFactorUnits(receipt);
        var orderedFactorUnits = ToFactorUnits(ordered);
        var maximumReceiptUnits = MaximumInputUnitsForCanonicalLimit(
            availableUnits, receiptFactorUnits);
        var maximumOrderedUnits = MaximumInputUnitsForCanonicalLimit(
            availableUnits, orderedFactorUnits);
        if (maximumReceiptUnits <= 0 || maximumOrderedUnits <= 0) return 0m;

        // When the ordered factor is at most one base unit per ordered input
        // unit, consecutive rounded canonical values differ by at most one.
        // The ordered lattice therefore contains every positive canonical unit
        // up to its maximum, so the exact receipt maximum is available directly.
        if (orderedFactorUnits <= FactorStorageMultiplier)
        {
            var orderedCanonicalLimit = RoundProductToQuantityUnits(
                maximumOrderedUnits, orderedFactorUnits);
            var commonCanonicalLimit = BigInteger.Min(
                availableUnits, orderedCanonicalLimit);
            var directMaximum = BigInteger.Min(
                maximumReceiptUnits,
                MaximumInputUnitsForCanonicalLimit(commonCanonicalLimit, receiptFactorUnits));
            return directMaximum > 0 && RoundProductToQuantityUnits(
                    directMaximum, receiptFactorUnits) > 0
                ? FromQuantityUnits(directMaximum)
                : 0m;
        }

        var maximum = MaximumCommonRoundedInput(
            maximumReceiptUnits,
            receiptFactorUnits,
            maximumOrderedUnits,
            orderedFactorUnits,
            availableUnits);
        return FromQuantityUnits(maximum);
    }

    public static decimal NormalizeCanonicalQuantity(decimal quantity)
    {
        if (quantity < 0m)
            throw new PurchaseReceiptQuantityException("Số lượng chuẩn không được âm.");
        var normalized = RoundQuantity(quantity);
        if (quantity > 0m && normalized == 0m)
            throw new PurchaseReceiptQuantityException("Số lượng chuẩn sau làm tròn không hợp lệ.");
        if (normalized > MaximumStoredQuantity)
            throw new PurchaseReceiptQuantityException("Số lượng chuẩn vượt giới hạn cho phép.");
        return normalized;
    }

    public static decimal RoundQuantity(decimal value)
        => decimal.Round(value, QuantityScale, MidpointRounding.AwayFromZero);

    private static BigInteger ToQuantityUnits(decimal quantity)
        => new(quantity * (decimal)QuantityStorageMultiplier);

    private static BigInteger ToFactorUnits(decimal factor)
        => new(factor * (decimal)FactorStorageMultiplier);

    private static decimal FromQuantityUnits(BigInteger quantityUnits)
        => (decimal)quantityUnits / (decimal)QuantityStorageMultiplier;

    private static BigInteger MaximumInputUnitsForCanonicalLimit(
        BigInteger canonicalLimit,
        BigInteger factorUnits)
    {
        var strictUpperNumerator = (2 * canonicalLimit + 1) * FactorStorageMultiplier;
        var maximum = CeilingDivide(strictUpperNumerator, 2 * factorUnits) - 1;
        return BigInteger.Min(BigInteger.Max(maximum, BigInteger.Zero), MaximumStoredQuantityUnits);
    }

    private static BigInteger MaximumCommonRoundedInput(
        BigInteger maximumReceiptUnits,
        BigInteger receiptFactorUnits,
        BigInteger maximumOrderedUnits,
        BigInteger orderedFactorUnits,
        BigInteger canonicalLimitUnits)
    {
        var gcd = BigInteger.GreatestCommonDivisor(receiptFactorUnits, orderedFactorUnits);
        var (bezoutReceipt, bezoutOrdered) = ExtendedGreatestCommonDivisor(
            receiptFactorUnits, orderedFactorUnits);
        var receiptStep = orderedFactorUnits / gcd;
        var orderedStep = receiptFactorUnits / gcd;
        var commonRawStep = receiptStep * receiptFactorUnits;
        var residueDivisor = BigInteger.GreatestCommonDivisor(
            commonRawStep, FactorStorageMultiplier);
        var roundingPeriod = (int)(FactorStorageMultiplier / residueDivisor);
        var normalizedRawStep = (int)((commonRawStep / residueDivisor) % roundingPeriod);
        var inverseRawStep = ModularInverse(normalizedRawStep, roundingPeriod);
        var modularPredecessors = new ModularPredecessorIndex(roundingPeriod, inverseRawStep);
        var bestReceipt = BigInteger.Zero;

        // If two positive raw products round to the same quantity-scale value,
        // their integer difference is strictly smaller than the factor scale
        // denominator (10,000). Enumerating that fixed rounding-error band is
        // independent of the decimal(18,3) quantity-domain magnitude.
        for (var rawDifference = -9_999; rawDifference <= 9_999; rawDifference++)
        {
            var difference = new BigInteger(rawDifference);
            if (difference % gcd != 0) continue;
            var multiplier = difference / gcd;
            var receiptBase = bezoutReceipt * multiplier;
            var orderedBase = -bezoutOrdered * multiplier;
            var minimumParameter = BigInteger.Max(
                CeilingDivideSigned(BigInteger.One - receiptBase, receiptStep),
                CeilingDivideSigned(BigInteger.One - orderedBase, orderedStep));
            var maximumParameter = BigInteger.Min(
                FloorDivideSigned(maximumReceiptUnits - receiptBase, receiptStep),
                FloorDivideSigned(maximumOrderedUnits - orderedBase, orderedStep));
            if (minimumParameter > maximumParameter) continue;

            // For positive values, round(x / D) == round((x-k) / D) iff the
            // residue of x + D/2 stays on the same side of the one rounding
            // boundary spanned by k. The Diophantine solution changes that
            // residue by a fixed coprime modular step. Query its inverse orbit
            // directly instead of walking as many as 10,000 parameters.
            var minimumResidue = rawDifference >= 0 ? rawDifference : 0;
            var maximumResidue = rawDifference >= 0
                ? (int)FactorStorageMultiplier - 1
                : (int)FactorStorageMultiplier + rawDifference - 1;
            var roundedRawBase = receiptBase * receiptFactorUnits +
                FactorStorageMultiplier / 2;
            var residueClass = (int)PositiveModulo(roundedRawBase, residueDivisor);
            var minimumNormalizedResidue = Math.Max(0,
                CeilingDivideInt(minimumResidue - residueClass, (int)residueDivisor));
            var maximumNormalizedResidue = Math.Min(roundingPeriod - 1,
                FloorDivideInt(maximumResidue - residueClass, (int)residueDivisor));
            if (minimumNormalizedResidue > maximumNormalizedResidue) continue;

            var normalizedBase = (int)(
                PositiveModulo(roundedRawBase - residueClass, FactorStorageMultiplier) /
                residueDivisor);
            var baseShift = (int)((long)inverseRawStep * normalizedBase % roundingPeriod);
            var parameterResidue = (int)PositiveModulo(maximumParameter, roundingPeriod);
            var predecessorTarget = (parameterResidue + baseShift) % roundingPeriod;
            var predecessor = modularPredecessors.FindPredecessor(
                minimumNormalizedResidue, maximumNormalizedResidue, predecessorTarget);
            if (predecessor < 0)
                predecessor = modularPredecessors.FindMaximum(
                    minimumNormalizedResidue, maximumNormalizedResidue);
            if (predecessor < 0) continue;

            var offset = predecessor <= predecessorTarget
                ? predecessorTarget - predecessor
                : predecessorTarget - predecessor + roundingPeriod;
            var parameter = maximumParameter - offset;
            if (parameter < minimumParameter) continue;

            var receiptInput = receiptBase + receiptStep * parameter;
            if (receiptInput <= bestReceipt) continue;
            var receiptCanonical = RoundProductToQuantityUnits(
                receiptInput, receiptFactorUnits);
            if (receiptCanonical <= 0 || receiptCanonical > canonicalLimitUnits) continue;
            var orderedInput = orderedBase + orderedStep * parameter;
            if (receiptCanonical == RoundProductToQuantityUnits(
                    orderedInput, orderedFactorUnits))
                bestReceipt = receiptInput;
        }

        return bestReceipt;
    }

    private static (BigInteger Left, BigInteger Right) ExtendedGreatestCommonDivisor(
        BigInteger left,
        BigInteger right)
    {
        var oldRemainder = left;
        var remainder = right;
        var oldLeft = BigInteger.One;
        var currentLeft = BigInteger.Zero;
        var oldRight = BigInteger.Zero;
        var currentRight = BigInteger.One;
        while (remainder != 0)
        {
            var quotient = oldRemainder / remainder;
            (oldRemainder, remainder) = (remainder, oldRemainder - quotient * remainder);
            (oldLeft, currentLeft) = (currentLeft, oldLeft - quotient * currentLeft);
            (oldRight, currentRight) = (currentRight, oldRight - quotient * currentRight);
        }
        return (oldLeft, oldRight);
    }

    private static BigInteger RoundProductToQuantityUnits(
        BigInteger inputQuantityUnits,
        BigInteger factorUnits)
    {
        var numerator = inputQuantityUnits * factorUnits;
        var quotient = BigInteger.DivRem(numerator, FactorStorageMultiplier, out var remainder);
        return remainder * 2 >= FactorStorageMultiplier ? quotient + 1 : quotient;
    }

    private static BigInteger CeilingDivide(BigInteger numerator, BigInteger denominator)
        => BigInteger.DivRem(numerator, denominator, out var remainder) +
            (remainder == 0 ? BigInteger.Zero : BigInteger.One);

    private static BigInteger FloorDivideSigned(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static BigInteger CeilingDivideSigned(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        return remainder > 0 ? quotient + 1 : quotient;
    }

    private static BigInteger PositiveModulo(BigInteger value, BigInteger modulus)
    {
        var remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }

    private static int FloorDivideInt(int numerator, int denominator)
    {
        var quotient = Math.DivRem(numerator, denominator, out var remainder);
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int CeilingDivideInt(int numerator, int denominator)
    {
        var quotient = Math.DivRem(numerator, denominator, out var remainder);
        return remainder > 0 ? quotient + 1 : quotient;
    }

    private static int ModularInverse(int value, int modulus)
    {
        if (modulus == 1) return 0;
        var oldRemainder = value;
        var remainder = modulus;
        var oldCoefficient = 1;
        var coefficient = 0;
        while (remainder != 0)
        {
            var quotient = oldRemainder / remainder;
            (oldRemainder, remainder) = (remainder, oldRemainder - quotient * remainder);
            (oldCoefficient, coefficient) =
                (coefficient, oldCoefficient - quotient * coefficient);
        }
        var inverse = oldCoefficient % modulus;
        return inverse < 0 ? inverse + modulus : inverse;
    }

    private sealed class ModularPredecessorIndex
    {
        private readonly int _leafCount;
        private readonly int[][] _values;

        public ModularPredecessorIndex(int period, int inverseStep)
        {
            _leafCount = 1;
            while (_leafCount < period) _leafCount *= 2;
            _values = new int[_leafCount * 2][];
            for (var index = 0; index < _leafCount; index++)
                _values[_leafCount + index] = index < period
                    ? [(int)((long)inverseStep * index % period)]
                    : [];
            for (var index = _leafCount - 1; index > 0; index--)
                _values[index] = MergeSorted(_values[index * 2], _values[index * 2 + 1]);
        }

        public int FindPredecessor(int start, int end, int target)
            => Query(start, end, target);

        public int FindMaximum(int start, int end)
            => Query(start, end, int.MaxValue);

        private int Query(int start, int end, int target)
        {
            var result = -1;
            for (int left = start + _leafCount, right = end + _leafCount;
                 left <= right;
                 left /= 2, right /= 2)
            {
                if ((left & 1) == 1) result = Math.Max(result, Predecessor(_values[left++], target));
                if ((right & 1) == 0) result = Math.Max(result, Predecessor(_values[right--], target));
            }
            return result;
        }

        private static int Predecessor(int[] values, int target)
        {
            var index = Array.BinarySearch(values, target);
            if (index < 0) index = ~index - 1;
            return index >= 0 ? values[index] : -1;
        }

        private static int[] MergeSorted(int[] left, int[] right)
        {
            var merged = new int[left.Length + right.Length];
            var leftIndex = 0;
            var rightIndex = 0;
            for (var index = 0; index < merged.Length; index++)
                merged[index] = rightIndex >= right.Length ||
                    leftIndex < left.Length && left[leftIndex] <= right[rightIndex]
                        ? left[leftIndex++]
                        : right[rightIndex++];
            return merged;
        }
    }
}

public sealed class PurchaseReceiptQuantityException(string message)
    : InvalidOperationException(message);
