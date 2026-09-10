using System.Security.Cryptography;
using System.Text;

namespace DemoApiB;

/// <summary>Deterministic fake "third-party partner" data keyed off the subject.</summary>
public static class PartnerLedger
{
    private static readonly string[] Counterparties =
    {
        "Northwind Traders", "Contoso Ltd", "Fabrikam Inc", "Adventure Works", "Tailspin Toys",
    };

    private static readonly string[] Kinds = { "Invoice", "Credit note", "Payment", "Adjustment" };

    public static IReadOnlyList<object> For(string subject)
    {
        var seed = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(subject ?? "anon")), 0);
        var rng = new Random(seed ^ 0x5f3759df);
        var count = 3 + rng.Next(3);

        return Enumerable.Range(0, count).Select(_ => (object)new
        {
            reference = $"PL-{rng.Next(100000, 999999)}",
            counterparty = Counterparties[rng.Next(Counterparties.Length)],
            kind = Kinds[rng.Next(Kinds.Length)],
            amount = Math.Round((rng.NextDouble() * 2 - 1) * 5000, 2),
            currency = "USD",
            postedOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-rng.Next(90))),
        }).ToList();
    }
}
