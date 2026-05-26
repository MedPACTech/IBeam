namespace IBeam.Repositories.AzureTables;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AzureTableStorageModelAttribute : Attribute
{
    public AzureTableStorageModelAttribute(AzureTableStorageModel storageModel)
    {
        StorageModel = storageModel;
    }

    public AzureTableStorageModel StorageModel { get; }
}

