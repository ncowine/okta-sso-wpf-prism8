using System.Security.Cryptography;
using System.Text;

namespace DemoApi;

/// <summary>Produces deterministic fake "business data" keyed off the caller's identity,
/// so different tokens visibly return different data.</summary>
public static class OrderGenerator
{
    private static readonly string[] Items =
    {
        "Standing desk", "Mechanical keyboard", "27\" monitor", "Noise-cancelling headset",
        "Webcam", "Docking station", "Ergonomic chair", "USB-C hub", "Desk lamp", "Laptop stand",
    };

    private static readonly string[] Statuses = { "Delivered", "Shipped", "Processing", "Backordered" };

    public static IReadOnlyList<object> For(string owner)
    {
        var seed = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes(owner ?? "anon")), 0);
        var rng = new Random(seed);
        var count = 2 + rng.Next(3);

        return Enumerable.Range(0, count).Select(i => (object)new
        {
            id = $"ORD-{10000 + rng.Next(90000)}",
            item = Items[rng.Next(Items.Length)],
            status = Statuses[rng.Next(Statuses.Length)],
            total = Math.Round(29 + rng.NextDouble() * 470, 2),
            placedOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-rng.Next(60))),
        }).ToList();
    }
}
