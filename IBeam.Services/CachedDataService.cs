using System;
using System.Collections.Generic;
using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using IBeam.DataModels;
using IBeam.Models;
using IBeam.Models.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Services.Interfaces;

namespace IBeam.Services
{

	public class CachedDataService
	{
        public readonly IMemoryCache _memoryCache;
        private readonly IMapper _mapper;
        //private readonly IApplicationRepository _applicationRepository;
        //A read only repo for data that only changes when system is rebooted
        //private IStaticDataRepository<SystemTypeDto> _systemTypeDtoRepository;

        public CachedDataService(IMapper mapper)
        {
            _mapper = mapper;
            
            //var results = FetchFromCache<DiagnosisCode, DiagnosisCodeDto>(_diagnosisCodeRepository, "diagnosisCodes");
        }

        //private IEnumerable<T1> FetchFromCache<T1, T2>(IStaticDataRepository<T2> staticRepository, string cacheName) where T2 : IStaticDTO
        //{
        //    var results = _memoryCache.Get<IEnumerable<T1>>(cacheName);

        //    if (results == null)
        //    {
        //        IEnumerable<T2> dtos = staticRepository.Get();
        //        results = _mapper.Map<IEnumerable<T1>>(dtos);
        //        _memoryCache.Set(cacheName, results);
        //    }

        //    return results;
        //}

    }
}
