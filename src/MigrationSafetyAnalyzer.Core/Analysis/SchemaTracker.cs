using MigrationSafetyAnalyzer.Core.Model;

namespace MigrationSafetyAnalyzer.Core.Analysis;

/// <summary>Tracks the evolving schema so rules can reason across migrations.</summary>
public sealed class SchemaTracker
{
    private readonly Dictionary<string, HashSet<string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _droppedColumns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Columns dropped earlier, keyed by table.</summary>
    public IReadOnlyDictionary<string, HashSet<string>> DroppedColumns => _droppedColumns;

    /// <summary>Records a table creation.</summary>
    public void CreateTable(string table, IEnumerable<string> columns)
    {
        _tables[table] = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Records a table drop.</summary>
    public void DropTable(string table)
    {
        _tables.Remove(table);
    }

    /// <summary>Returns whether a table is known.</summary>
    public bool HasTable(string table) => _tables.ContainsKey(table);

    /// <summary>Returns whether a column is known on a table.</summary>
    public bool HasColumn(string table, string column)
        => _tables.TryGetValue(table, out var columns) && columns.Contains(column);

    /// <summary>Records a column addition.</summary>
    public void AddColumn(string table, string column)
    {
        if (!_tables.TryGetValue(table, out var columns))
        {
            columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _tables[table] = columns;
        }

        columns.Add(column);
    }

    /// <summary>Records a column drop.</summary>
    public void DropColumn(string table, string column)
    {
        if (_tables.TryGetValue(table, out var columns))
        {
            columns.Remove(column);
        }

        if (!_droppedColumns.TryGetValue(table, out var dropped))
        {
            dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _droppedColumns[table] = dropped;
        }

        dropped.Add(column);
    }

    /// <summary>Records a column rename.</summary>
    public void RenameColumn(string table, string oldName, string newName)
    {
        if (_tables.TryGetValue(table, out var columns))
        {
            columns.Remove(oldName);
            columns.Add(newName);
        }
    }

    /// <summary>Clears all state. Called before each analysis run.</summary>
    public void Reset()
    {
        _tables.Clear();
        _droppedColumns.Clear();
    }
}
