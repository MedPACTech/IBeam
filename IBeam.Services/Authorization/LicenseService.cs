using System;
using AutoMapper;
using IBeam.DataModels;
using IBeam.Models;
using IBeam.Models.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Services.Interfaces;
using License = IBeam.Models.License;

namespace IBeam.Services
{

    public class LicenseService : ILicenseService
    {
        private readonly IMapper _mapper;
        private readonly ILicenseRepository _licenseRepository;

        public LicenseService(IMapper mapper, ILicenseRepository licenseRepository)
        {
            _mapper = mapper;
            _licenseRepository = licenseRepository;
        }

        public ILicense GetLatest()
        {
            var licenseDTO = _licenseRepository.GetLatest();
            return _mapper.Map<License>(licenseDTO);
        }

        public ILicense Fetch(Guid id)
        {

            if (id == Guid.Empty)
                return new License();
            else
            {
                var licenseDTO = _licenseRepository.GetById(id);
                return _mapper.Map<License>(licenseDTO);
            }
        }

        public void Save(ILicense license)
        {
            if (license.Id == Guid.Empty)
                license.Id = Guid.NewGuid();

            var licenseDTO = _mapper.Map<LicenseDTO>(license);
            _licenseRepository.Save(licenseDTO);
        }

    }
}
