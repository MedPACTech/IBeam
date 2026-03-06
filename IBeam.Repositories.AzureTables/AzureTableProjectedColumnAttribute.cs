namespace IBeam.Repositories.AzureTables;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class AzureTableProjectedColumnAttribute : Attribute
{
    public AzureTableProjectedColumnAttribute()
    {
    }

    public AzureTableProjectedColumnAttribute(string columnName)
    {
        ColumnName = columnName;
    }

    public string? ColumnName { get; }
}

