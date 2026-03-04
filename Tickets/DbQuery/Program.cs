using Microsoft.Data.Sqlite;
using Spectre.Console;
using System.Text;

// ── Paths ────────────────────────────────────────────────────────────────────
// BaseDirectory = Tickets/DbQuery/bin/Debug/net10.0/ → go up 4 levels to Tickets/
var ticketsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var dbPath     = Path.Combine(ticketsDir, "tickets.db");
var schemaPath = Path.Combine(ticketsDir, "schema.sql");

// ── Entry ────────────────────────────────────────────────────────────────────
var cmd  = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var rest = args.Skip(1).ToArray();

switch (cmd)
{
    case "init":    CmdInit();               break;
    case "seed":    CmdSeed();               break;
    case "list":    CmdList(rest);           break;
    case "show":    CmdShow(rest);           break;
    case "new":     CmdNew();                break;
    case "update":  CmdUpdate(rest);         break;
    case "comment": CmdComment(rest);        break;
    case "export":  CmdExport();             break;
    case "pipeline":CmdPipeline();           break;
    default:        ShowHelp();              break;
}

// ── Commands ──────────────────────────────────────────────────────────────────

void CmdInit()
{
    if (!File.Exists(schemaPath))
    {
        AnsiConsole.MarkupLine($"[red]schema.sql not found at {schemaPath}[/]");
        return;
    }
    using var con = Open();
    foreach (var stmt in SplitSql(File.ReadAllText(schemaPath)))
        con.Execute(stmt);
    AnsiConsole.MarkupLine($"[green]✓[/] Database initialised at [dim]{dbPath}[/]");
}

void CmdSeed()
{
    var seedPath = Path.Combine(ticketsDir, "seed.sql");
    if (!File.Exists(seedPath)) { AnsiConsole.MarkupLine("[red]seed.sql not found.[/]"); return; }
    using var con = Open();
    foreach (var stmt in SplitSql(File.ReadAllText(seedPath)))
        con.Execute(stmt);
    AnsiConsole.MarkupLine("[green]✓[/] Seed data applied.");
}

static IEnumerable<string> SplitSql(string sql)
{
    var buf   = new StringBuilder();
    int depth = 0;
    foreach (var rawLine in sql.Split('\n'))
    {
        var upper = rawLine.Trim().ToUpperInvariant();
        if (upper.StartsWith("BEGIN")) depth++;
        buf.AppendLine(rawLine);
        if (upper is "END;" or "END") depth--;
        if (depth == 0 && rawLine.TrimEnd().EndsWith(";"))
        {
            var s = buf.ToString().Trim();
            buf.Clear();
            // Only skip buffers that are entirely comments or whitespace
            var hasSql = s.Split('\n').Any(l => { var t = l.Trim(); return t.Length > 0 && !t.StartsWith("--"); });
            if (hasSql) yield return s;
        }
    }
}

void CmdList(string[] args)
{
    var filter = args.Length > 0 ? args[0] : null;
    using var con = Open();

    var sql = "SELECT TicketId, Status, Priority, Severity, Title, AssignedTo, DueDate, TokenEstimate, TokensBurned, Labels FROM Tickets";
    if (filter != null && !filter.Equals("all", StringComparison.OrdinalIgnoreCase))
        sql += $" WHERE Status = '{filter.Replace("'", "''")}'";
    sql += " ORDER BY CASE Status WHEN 'In Development' THEN 1 WHEN 'SIT Testing' THEN 2 WHEN 'For Development' THEN 3 WHEN 'Backlog' THEN 4 WHEN 'Complete' THEN 5 ELSE 6 END, Priority DESC";

    var table = new Table().Border(TableBorder.Rounded).Expand();
    table.AddColumn("ID");
    table.AddColumn("Status");
    table.AddColumn("P");
    table.AddColumn("Sev");
    table.AddColumn("Title");
    table.AddColumn("Assigned");
    table.AddColumn("Due");
    table.AddColumn("Tkn");

    using var reader = con.Query(sql);
    int count = 0;
    while (reader.Read())
    {
        count++;
        var status   = reader.GetStringSafe(1);
        var priority = reader.GetStringSafe(2);
        var sev      = reader.GetStringSafe(3);
        var title    = reader.GetStringSafe(4);
        if (title.Length > 48) title = title[..45] + "…";

        table.AddRow(
            $"[bold]{reader.GetStringSafe(0)}[/]",
            StatusMarkup(status),
            PriorityMarkup(priority),
            SeverityMarkup(sev),
            Markup.Escape(title),
            Markup.Escape(reader.GetStringSafe(5) ?? "—"),
            reader.GetStringSafe(6) ?? "—",
            $"{reader.GetStringSafe(7)}/{reader.GetStringSafe(8)}"
        );
    }
    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($"[dim]{count} ticket(s)[/]");
}

void CmdShow(string[] args)
{
    if (args.Length == 0) { AnsiConsole.MarkupLine("[red]Usage: show <TICKET-ID>[/]"); return; }
    var id = args[0].ToUpperInvariant();
    using var con = Open();

    using var r = con.Query("SELECT * FROM Tickets WHERE TicketId=@id", ("@id", id));
    if (!r.Read()) { AnsiConsole.MarkupLine($"[red]Ticket {id} not found.[/]"); return; }

    var panel = new Panel(BuildTicketBody(r))
        .Header($"[bold cyan]{id}[/]")
        .Border(BoxBorder.Rounded)
        .Expand();
    AnsiConsole.Write(panel);

    // Comments
    using var cr = con.Query("SELECT Author, Body, CreatedAt FROM TicketComments WHERE TicketId=@id ORDER BY CreatedAt", ("@id", id));
    bool hasComments = false;
    while (cr.Read())
    {
        if (!hasComments) { AnsiConsole.MarkupLine("\n[bold]Comments[/]"); hasComments = true; }
        AnsiConsole.MarkupLine($"[dim]{cr.GetStringSafe(2)} — {cr.GetStringSafe(0) ?? "anon"}[/]");
        AnsiConsole.MarkupLine($"  {Markup.Escape(cr.GetStringSafe(1) ?? "")}");
    }
}

void CmdNew()
{
    AnsiConsole.MarkupLine("[bold cyan]New Ticket[/]");

    var id       = AnsiConsole.Ask<string>("Ticket ID [dim](e.g. TICKET-032)[/]:");
    var title    = AnsiConsole.Ask<string>("Title:");
    var status   = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Status:")
        .AddChoices("Backlog","For Development","In Development","SIT Testing","Complete","Cancelled"));
    var priority = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Priority:")
        .AddChoices("Critical","High","Medium","Low"));
    var severity = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("Severity:")
        .AddChoices("Critical","High","Medium","Low"));
    var assignee = AnsiConsole.Ask<string>("Assigned to [dim](Enter to skip)[/]:", "");
    var desc     = AnsiConsole.Ask<string>("Description [dim](Enter to skip)[/]:", "");
    var due      = AnsiConsole.Ask<string>("Due date [dim]YYYY-MM-DD or blank[/]:", "");
    var est      = AnsiConsole.Ask<int>("Token estimate:", 0);
    var labels   = AnsiConsole.Ask<string>("Labels [dim]comma-separated or blank[/]:", "");

    using var con = Open();
    con.Execute(@"
        INSERT INTO Tickets (TicketId,Status,Priority,Severity,Title,Description,AssignedTo,DueDate,TokenEstimate,Labels)
        VALUES (@id,@st,@pr,@sv,@ti,@de,@as,@du,@te,@la)",
        ("@id",id),("@st",status),("@pr",priority),("@sv",severity),("@ti",title),
        ("@de",desc),("@as",assignee),("@du",due),("@te",est),("@la",labels));

    AnsiConsole.MarkupLine($"[green]✓[/] Created {id}: {Markup.Escape(title)}");
}

void CmdUpdate(string[] args)
{
    // Usage: update TICKET-001 status "In Development"
    //        update TICKET-001 assignee "Damo"
    //        update TICKET-001 tokens-burned 12
    if (args.Length < 3) { AnsiConsole.MarkupLine("[red]Usage: update <ID> <field> <value>[/]"); return; }
    var id    = args[0].ToUpperInvariant();
    var field = args[1].ToLowerInvariant();
    var value = string.Join(" ", args.Skip(2));

    var colMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["status"]                = "Status",
        ["priority"]              = "Priority",
        ["severity"]              = "Severity",
        ["assignee"]              = "AssignedTo",
        ["assigned"]              = "AssignedTo",
        ["due"]                   = "DueDate",
        ["start"]                 = "StartDate",
        ["target"]                = "TargetCompletionDate",
        ["tokens-estimate"]       = "TokenEstimate",
        ["tokens-burned"]         = "TokensBurned",
        ["labels"]                = "Labels",
        ["notes"]                 = "Notes",
        ["blockedby"]             = "BlockedBy",
        ["blocks"]                = "Blocks",
        ["attempts"]              = "Attempts",
    };

    if (!colMap.TryGetValue(field, out var col))
    {
        AnsiConsole.MarkupLine($"[red]Unknown field '{field}'. Valid: {string.Join(", ", colMap.Keys)}[/]");
        return;
    }

    using var con = Open();
    con.Execute($"UPDATE Tickets SET {col}=@v WHERE TicketId=@id", ("@v", value), ("@id", id));
    AnsiConsole.MarkupLine($"[green]✓[/] {id}.{col} = [cyan]{Markup.Escape(value)}[/]");
}

void CmdComment(string[] args)
{
    if (args.Length < 2) { AnsiConsole.MarkupLine("[red]Usage: comment <ID> <text>[/]"); return; }
    var id   = args[0].ToUpperInvariant();
    var body = string.Join(" ", args.Skip(1));
    using var con = Open();
    con.Execute("INSERT INTO TicketComments(TicketId,Body) VALUES(@id,@b)", ("@id",id),("@b",body));
    AnsiConsole.MarkupLine($"[green]✓[/] Comment added to {id}");
}

void CmdExport()
{
    using var con = Open();
    var sb = new StringBuilder();
    sb.AppendLine("TicketId,Status,Priority,Severity,Title,AssignedTo,DueDate,TokenEstimate,TokensBurned,Labels,BlockedBy,Blocks,LastUpdated");
    using var r = con.Query("SELECT TicketId,Status,Priority,Severity,Title,AssignedTo,DueDate,TokenEstimate,TokensBurned,Labels,BlockedBy,Blocks,LastUpdated FROM Tickets ORDER BY TicketId");
    while (r.Read())
    {
        var cols = Enumerable.Range(0, r.FieldCount).Select(i => CsvEscape(r.GetStringSafe(i) ?? ""));
        sb.AppendLine(string.Join(",", cols));
    }
    var path = Path.Combine(Path.GetDirectoryName(dbPath)!, "tickets_export.csv");
    File.WriteAllText(path, sb.ToString());
    AnsiConsole.MarkupLine($"[green]✓[/] Exported to [dim]{path}[/]");
}

void CmdPipeline()
{
    using var con = Open();
    var statuses = new[] { "In Development","SIT Testing","For Development","Backlog","Complete","Cancelled" };
    AnsiConsole.MarkupLine("\n[bold cyan]📋 Ticket Pipeline[/]\n");
    foreach (var s in statuses)
    {
        using var r = con.Query("SELECT TicketId, Priority, Severity, Title FROM Tickets WHERE Status=@s ORDER BY Priority", ("@s", s));
        var rows = new List<(string id, string pri, string sev, string title)>();
        while (r.Read()) rows.Add((r.GetStringSafe(0), r.GetStringSafe(1), r.GetStringSafe(2), r.GetStringSafe(3)));
        AnsiConsole.MarkupLine($"{StatusMarkup(s)} [bold]{s}[/] [dim]({rows.Count})[/]");
        foreach (var (id, pri, sev, title) in rows)
        {
            var t = title.Length > 55 ? title[..52] + "…" : title;
            AnsiConsole.MarkupLine($"  [dim]{id}[/]  {PriorityMarkup(pri)} {Markup.Escape(t)}");
        }
        AnsiConsole.WriteLine();
    }
}

void ShowHelp()
{
    AnsiConsole.MarkupLine("[bold]eToroTrader Ticket Manager[/]");
    AnsiConsole.MarkupLine("[dim]Usage: dotnet run --project Tickets/DbQuery -- <command>[/]\n");
    AnsiConsole.MarkupLine("  [cyan]init[/]                         Create/reset tickets.db");
    AnsiConsole.MarkupLine("  [cyan]seed[/]                         Apply seed.sql initial data");
    AnsiConsole.MarkupLine("  [cyan]list [status|all][/]            List tickets");
    AnsiConsole.MarkupLine("  [cyan]pipeline[/]                     Pipeline grouped view");
    AnsiConsole.MarkupLine("  [cyan]show <ID>[/]                    Full ticket + comments");
    AnsiConsole.MarkupLine("  [cyan]new[/]                          Create ticket interactively");
    AnsiConsole.MarkupLine("  [cyan]update <ID> <field> <value>[/]  Update a field");
    AnsiConsole.MarkupLine("  [cyan]comment <ID> <text>[/]          Add a comment");
    AnsiConsole.MarkupLine("  [cyan]export[/]                       Export to tickets_export.csv");
    AnsiConsole.MarkupLine("\n[dim]Fields: status priority severity assignee due start target tokens-estimate tokens-burned labels notes blockedby blocks attempts[/]");
}

// ── Helpers ───────────────────────────────────────────────────────────────────

SqliteConnection Open()
{
    var con = new SqliteConnection($"Data Source={dbPath}");
    con.Open();
    con.Execute("PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
    return con;
}

string BuildTicketBody(SqliteDataReader r)
{
    var sb = new StringBuilder();
    for (int i = 0; i < r.FieldCount; i++)
    {
        var name = r.GetName(i);
        var val  = r.IsDBNull(i) ? "" : r.GetString(i);
        if (string.IsNullOrWhiteSpace(val)) continue;
        sb.AppendLine($"[bold]{name,-24}[/] {Markup.Escape(val)}");
    }
    return sb.ToString();
}

static string StatusMarkup(string s) => s switch
{
    "In Development"  => "[yellow]🔨[/]",
    "SIT Testing"     => "[blue]✅[/]",
    "For Development" => "[cyan]🎯[/]",
    "Backlog"         => "[dim]📦[/]",
    "Complete"        => "[green]🚀[/]",
    "Cancelled"       => "[red]❌[/]",
    _                 => "[dim]?[/]"
};

static string PriorityMarkup(string p) => p switch
{
    "Critical" => "[red bold]C[/]",
    "High"     => "[red]H[/]",
    "Medium"   => "[yellow]M[/]",
    "Low"      => "[dim]L[/]",
    _          => "[dim]?[/]"
};

static string SeverityMarkup(string s) => PriorityMarkup(s);

static string CsvEscape(string v)
{
    if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
        return $"\"{v.Replace("\"", "\"\"")}\"";
    return v;
}

// ── SqliteConnection extensions ───────────────────────────────────────────────

static class SqliteExtensions
{
    public static void Execute(this SqliteConnection con, string sql, params (string, object?)[] p)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, val) in p) cmd.Parameters.AddWithValue(name, val ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static SqliteDataReader Query(this SqliteConnection con, string sql, params (string, object?)[] p)
    {
        var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, val) in p) cmd.Parameters.AddWithValue(name, val ?? DBNull.Value);
        return cmd.ExecuteReader();
    }

    public static string? GetStringSafe(this SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : r.GetString(i);
}
