namespace IBeam.Services.Abstractions;

public enum ServiceAuditOperation
{
    Create = 1,
    Update = 2,
    Delete = 3,
    GetAll = 10,
    GetAllWithArchived = 11,
    GetById = 12,
    GetByIds = 13
}

