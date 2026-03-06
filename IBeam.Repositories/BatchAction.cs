namespace IBeam.Repositories.Abstractions;

public enum BatchActionType
{
    Add = 0,
    UpsertReplace = 1,
    UpdateReplace = 2,
    Delete = 3
}

public sealed record BatchAction<T>(
    BatchActionType ActionType,
    T? Entity = default,
    string? PartitionKey = null,
    string? RowKey = null)
    where T : class;

