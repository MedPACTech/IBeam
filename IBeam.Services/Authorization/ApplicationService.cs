using System;
using AutoMapper;
using IBeam.DataModels;
using IBeam.Models;
using IBeam.Models.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Services.Interfaces;

namespace IBeam.Services.Authorization
{

	public class ApplicationService : IApplicationService
	{
        private readonly IMapper _mapper;
        private readonly IApplicationRepository _applicationRepository;

        public ApplicationService(IMapper mapper, IApplicationRepository applicationRepository)
        {
            _mapper = mapper;
            _applicationRepository = applicationRepository;
        }

        public IApplication Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new Application();
            else
            {
                var applicationDTO = _applicationRepository.GetById(id);
                return _mapper.Map<Application>(applicationDTO);
            }
        }

        public void Save(IApplication application)
        {
            if (application.Id == Guid.Empty)
                application.Id = Guid.NewGuid();

            var applicationDTO = _mapper.Map<ApplicationDTO>(application);
            _applicationRepository.Save(applicationDTO);
        }

	}
}
