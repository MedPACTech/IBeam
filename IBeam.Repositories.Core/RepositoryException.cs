namespace IBeam.Repositories.Core;

public class RepositoryException : Exception
{
    public string Repository { get; }
    public string Operation { get; }

    public RepositoryException(string repository, string operation, string message, Exception? inner = null)
        : base(message, inner)
    {
        Repository = repository;
        Operation = operation;
    }
}

public sealed class RepositoryValidationException : RepositoryException
{
    public RepositoryValidationException(string repository, string operation, string message, Exception? inner = null)
        : base(repository, operation, message, inner) { }
}

public sealed class RepositoryStoreException : Exception
{
    public string RepositoryName { get; }
    public string Operation { get; }

    public RepositoryStoreException(string repositoryName, string operation, Exception inner)
        : base($"Repository store error in '{repositoryName}' during '{operation}'.", inner)
    {
        RepositoryName = repositoryName;
        Operation = operation;
    }
}