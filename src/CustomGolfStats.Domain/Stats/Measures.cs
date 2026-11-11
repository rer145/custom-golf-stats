namespace CustomGolfStats.Domain.Stats;

public readonly record struct Ratio(int Numerator, int Denominator)
{
    public double? Value => Denominator == 0 ? null : (double)Numerator / Denominator;
}

public readonly record struct Average(int Total, int Count)
{
    public double? Value => Count == 0 ? null : (double)Total / Count;
}
