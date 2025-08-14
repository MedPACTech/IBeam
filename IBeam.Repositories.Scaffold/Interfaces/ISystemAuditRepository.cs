using IBeam.Scaffolding.DataModels;
using IBeam.Repositories.Interfaces;
namespace IBeam.Scaffolding.Repositories.Interfaces
{ 
	public interface ISystemAuditRepository : IBaseRepository<SystemAuditDTO>
	{
		//IEnumerable<CustomerEquipmentDTO> GetByCustomer(Guid customerId);
		//IEnumerable<CustomerEquipmentDTO> GetByLocation(Guid locationId);
	}

}