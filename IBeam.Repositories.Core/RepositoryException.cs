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

