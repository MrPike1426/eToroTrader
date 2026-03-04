#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Data.Sqlite, 9.0.0"

using Microsoft.Data.Sqlite;

var dbPath = @"src\TopStepTrader.UI\bin\x64\Debug\net10.0-windows\TopStepTrader.db";

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

Console.WriteLine("=== ORDERS (last 20) ===");
using (var cmd = conn.CreateCommand()) {
    cmd.CommandText = "SELECT Id, ContractId, Side, OrderType, Quantity, Status, PlacedAt, Notes FROM Orders ORDER BY PlacedAt DESC LIMIT 20;";
    using var r = cmd.ExecuteReader();
    Console.WriteLine($"{"Id",-5} {"ContractId",-22} {"Side",-6} {"Type",-10} {"Qty",-4} {"Status",-10} {"PlacedAt",-25} Notes");
    Console.WriteLine(new string('-', 110));
    while (r.Read())
        Console.WriteLine($"{r["Id"],-5} {r["ContractId"],-22} {r["Side"],-6} {r["OrderType"],-10} {r["Quantity"],-4} {r["Status"],-10} {r["PlacedAt"],-25} {r["Notes"]}");
}

Console.WriteLine("\n=== TRADE OUTCOMES (last 10) ===");
try {
    using var cmd2 = conn.CreateCommand();
    cmd2.CommandText = "SELECT Id, ContractId, Side, EntryPrice, ExitPrice, PnL, IsOpen, IsWinner, EntryTime, ExitTime FROM TradeOutcomes ORDER BY EntryTime DESC LIMIT 10;";
    using var r2 = cmd2.ExecuteReader();
    Console.WriteLine($"{"Id",-5} {"ContractId",-22} {"Side",-6} {"Entry",-10} {"Exit",-10} {"PnL",-10} {"Open",-6} {"Winner",-7} {"EntryTime",-22} ExitTime");
    Console.WriteLine(new string('-', 120));
    while (r2.Read())
        Console.WriteLine($"{r2["Id"],-5} {r2["ContractId"],-22} {r2["Side"],-6} {r2["EntryPrice"],-10} {r2["ExitPrice"],-10} {r2["PnL"],-10} {r2["IsOpen"],-6} {r2["IsWinner"],-7} {r2["EntryTime"],-22} {r2["ExitTime"]}");
} catch (Exception ex) { Console.WriteLine($"TradeOutcomes error: {ex.Message}"); }
