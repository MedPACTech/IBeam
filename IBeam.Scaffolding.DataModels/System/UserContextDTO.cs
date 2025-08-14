using IBeam.DataModels.System;
using ServiceStack.DataAnnotations;

namespace IBeam.Scaffolding.DataModels
{

    [Serializable]
	[Alias("AccountContext")]
	public class AccountContextDTO : IDTO
	{

		public Guid Id { get; set; }
		public Guid AccountId { get; set; }
		public string Demographics { get; set; }
		public string ApplicationSettings { get; set; }
		public string ApplicationContext { get; set; }
        public bool IsDeleted { get; set; }
    }
}
