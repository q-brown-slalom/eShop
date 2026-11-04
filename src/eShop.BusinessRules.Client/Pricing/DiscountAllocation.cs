namespace eShop.BusinessRules.Client.Pricing;

public static class DiscountAllocation
{
    /// <summary>
    /// Splits <paramref name="amount"/> over <paramref name="capacities"/> in whole cents. Each share is proportional to its
    /// capacity, floored to the cent; leftover cents go one at a time by largest remainder, ties to the lower index.
    /// The shares sum to <paramref name="amount"/> and no share exceeds its capacity.
    /// </summary>
    public static decimal[] Allocate(decimal amount, IReadOnlyList<decimal> capacities)
    {
        ArgumentNullException.ThrowIfNull(capacities);

        var amountCents = ToCents(amount, nameof(amount));
        var capacityCents = new Int128[capacities.Count];
        Int128 totalCapacity = 0;
        for (var i = 0; i < capacityCents.Length; i++)
        {
            capacityCents[i] = ToCents(capacities[i], nameof(capacities));
            if (capacityCents[i] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacities), capacities[i], "Capacities must not be negative.");
            }

            totalCapacity += capacityCents[i];
        }

        if (amountCents < 0 || amountCents > totalCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be between 0 and the sum of the capacities.");
        }

        var shares = new Int128[capacityCents.Length];
        var remainders = new Int128[capacityCents.Length];
        Int128 allocated = 0;
        if (totalCapacity > 0)
        {
            for (var i = 0; i < shares.Length; i++)
            {
                (shares[i], remainders[i]) = Int128.DivRem(amountCents * capacityCents[i], totalCapacity);
                allocated += shares[i];
            }
        }

        // Σ remainders = leftover × totalCapacity and each is < totalCapacity, so more lines have a remainder than there are
        // leftover cents, and a line with a remainder always has room for one more cent.
        var leftover = (int)(amountCents - allocated);
        foreach (var i in Enumerable.Range(0, shares.Length).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take(leftover))
        {
            shares[i]++;
        }

        return [.. shares.Select(s => (decimal)s / 100m)];
    }

    private static Int128 ToCents(decimal value, string paramName)
    {
        if (decimal.Round(value, 2) != value)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must have at most 2 decimal places.");
        }

        return (Int128)(value * 100m);
    }
}
