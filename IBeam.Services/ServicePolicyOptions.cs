using System.Collections.Generic;
using System;

namespace IBeam.Services.Abstractions
{
    public sealed class ServicePolicyOptions
    {
        public const string SectionName = "IBeam:Services:Policies";

        // Key: service type name (short or full name), value: operation toggles.
        public Dictionary<string, ServiceOperationAccessOptions> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ServiceOperationAccessOptions
    {
        public bool? GetById { get; set; }
        public bool? GetByIds { get; set; }
        public bool? GetAll { get; set; }
        public bool? GetAllWithArchived { get; set; }
        public bool? Save { get; set; }
        public bool? SaveAll { get; set; }
        public bool? Archive { get; set; }
        public bool? Unarchive { get; set; }
        public bool? Delete { get; set; }

        public bool? GetValue(ServiceOperation operation)
        {
            return operation switch
            {
                ServiceOperation.GetById => GetById,
                ServiceOperation.GetByIds => GetByIds,
                ServiceOperation.GetAll => GetAll,
                ServiceOperation.GetAllWithArchived => GetAllWithArchived,
                ServiceOperation.Save => Save,
                ServiceOperation.SaveAll => SaveAll,
                ServiceOperation.Archive => Archive,
                ServiceOperation.Unarchive => Unarchive,
                ServiceOperation.Delete => Delete,
                _ => null
            };
        }
    }
}
