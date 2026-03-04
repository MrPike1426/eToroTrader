using System;
using System.IO;
using Microsoft.Data.Sqlite;

var dbPath = Path.GetFullPath(@"src\TopStepTrader.UI\bin\x64\Debug\net10.0-windows\TopStepTrader.db");
Console.WriteLine($"DB: {dbPath}\n");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// Show full Orders schema first
Console.WriteLine("=== ORDERS TABLE COLUMNS ===");
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "PRAGMA table_info(Orders);";
    using var r = cmd.ExecuteReader();
    while (r.Read()) Console.WriteLine($"  col[{r["cid"]}] {r["name"]} {r["type"]}");
}

Console.WriteLine("\n=== ALL ORDERS (newest first) ===");
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "SELECT * FROM Orders ORDER BY PlacedAt DESC LIMIT 20;";
    using var r = cmd.ExecuteReader();
    // Print header from schema
    for (int i = 0; i < r.FieldCount; i++) Console.Write($"{r.GetName(i),-22}");
    Console.WriteLine();
    Console.WriteLine(new string('-', r.FieldCount * 22));
    while (r.Read()) {
        for (int i = 0; i < r.FieldCount; i++) Console.Write($"{r[i],-22}");
        Console.WriteLine();
    }
}
