using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using IBeam.Models;
using IBeam.Models.API;
using IBeam.Services.Interfaces;
using IBeam.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using IBeam.Repositories.Interfaces;
using IBeam.DataModels.System;

namespace IBeam.Services.System
{
    public abstract class BaseService<TDTO, IBaseModel> where TDTO : class, IDTO //: IBaseService
    {
        //Exception handling
        //Within a service
        //--- 1. AuthorizationException (you cannot do this based on role) -- Expected Exception
        //  - throw and send up (perfer base service to do the work
        //--- 2. RepositoryException (something happened in the repository) -- UnExpected Exception
        //  - just send up
        //--- 3. RuleException (Account submitted bad data / business rule happend) -- Expected Exception
        //  - - throw and send up (perfer base service to do the work
        //--- 2. ServiceException (something happened in the service) -- UnExpected Exception
        //  - just send up

        private readonly IBaseRepository<TDTO> _repository;
        private readonly IMapper _mapper;
        public readonly  IServiceAuthorizationService _systemAuthorizationService;
        private readonly ISystemAuditService _systemAuditService;
        //private readonly IErrorLogService _errorLogService;

        public BaseService(IBaseServices baseServices, IBaseRepository<TDTO> repository)
        {
            _repository = repository;
            _mapper = baseServices.Mapper;
            _systemAuditService = baseServices.SystemAuditService;
            _systemAuthorizationService = baseServices.SystemAuthorizationService;
            //_errorLogService = baseServices._errorLogService;

            SetApplicationId(Guid.Parse("64be5868-f934-4dcf-953e-322ff22ee839"));

        }

        ////////// MAPPINGS ////////////////////
        public TDTO ModelToEntity(IBaseModel model)
        {
            return _mapper.Map<TDTO>(model);
        }

        public IBaseModel EntityToModel(TDTO dto)
        {
            return _mapper.Map<IBaseModel>(dto);
        }

        public List<TDTO> ModelToEntity(IEnumerable<IBaseModel> model)
        {
            return _mapper.Map<List<TDTO>>(model);
        }

        public List<IBaseModel> EntityToModel(IEnumerable<TDTO> dto)
        {
            return _mapper.Map<List<IBaseModel>>(dto);
        }

        ///////////////// DEFAULT REPOSITORY CALLS /////////////////////////
        public List<IBaseModel> GetAll()
        {
            return EntityToModel(_repository.GetAll());
        }

        public IBaseModel GetById(Guid id)
        {
            return EntityToModel(_repository.GetById(id));
        }

        public List<IBaseModel> GetByIds(List<Guid> ids)
        {
            return EntityToModel(_repository.GetByIds(ids));
        }

        //todo: add return args
        public bool Save(IBaseModel model)
        {
            var dto = ModelToEntity(model);
            _repository.Save(dto);
            return true;
        }

        public bool SaveAll(List<IBaseModel> models)
        {
            var entities = ModelToEntity(models);
            _repository.SaveAll(entities);
            return true;
        }

        public bool Archive(IBaseModel model)
        {
            var dto = ModelToEntity(model);
            return _repository.Archive(dto);
        }

        public void ArchiveAll(List<IBaseModel> models)
        {
            var dtos = ModelToEntity(models);
            _repository.ArchiveAll(dtos);
        }

        public void Delete(IBaseModel model)
        {
            var dto = ModelToEntity(model);
            _repository.Delete(dto);
        }

        public void DeleteById(Guid id)
        {
            var dtoToDelete = _repository.GetById(id);
            _repository.Delete(dtoToDelete);
        }

        ///////////////// AUDITING /////////////////////////
        //public bool CreateAuditLog()
       // {
       //     return _systemAuditService.CreateAuditLog();
       // }

        ///////////////// LOGGING /////////////////////////
        //public bool CreateErrorLog()
        //{
        //    return _errorLogService.CreateErrorLog();
        //}

        private IDTO _idto;
        private bool _isNewId = false;
        private bool _ignoreContext = false;
        private protected string _entityName;
        private protected string _serviceName;
        private protected IAccountContext _AccountContext;
        private protected Guid _applicationId;
        public readonly IMemoryCache _memoryCache;
        //public readonly IMapper _mapper;
       // public readonly IServiceAuthorizationService _systemAuthorizationService;
       // private readonly ISystemAuditService _systemAuditService;
        // private readonly IAccountContextService _AccountContextService;

        //public readonly bool _defaultAllowAccess;

        //public BaseService(IBaseServices baseServices)
        //{
            //_mapper = baseServices.Mapper;
            //_systemAuditService = baseServices.SystemAuditService;
           // _systemAuthorizationService = baseServices.SystemAuthorizationService;
            //_AccountContextService = baseServices.AccountContextService;
           
            //AuthorizeService();
            //SetAccountContext();
            //_defaultAllowAccess = false;
        //}

        public IAccountContext GetAccountContext()
        {
            return _AccountContext;
        }

        public void IgnoreContext()
        {
            _ignoreContext = true;
        }

        //TODO: switch to expected exception, move response string to config.
        public void SetAccountContext(IAccountContext AccountContext)
        {
            try
            {
                _AccountContext = AccountContext;
                //if (AccountContext == null) //|| AccountContext.AccountId == Guid.Empty)
                //{
                //    var exception = new Exception("AccountContext has not been set, or AccountId is empty.");
                //    throw new ServiceException(exception);
                //}
            }
            catch (Exception e)
            {
                throw new Exception("Error Setting AccountContext on Service", e);
            }
        }

        public void SetApplicationId(Guid applicationId)
        {
            _applicationId = applicationId;

            if (_applicationId == Guid.Empty)
            {
                var exception = new Exception("ApplicationId has not been set");
                throw new ServiceException(exception);
            }
        }

        public void SetId(IDTO idto)
        {
            if (idto.Id == Guid.Empty)
            {
                idto.Id = Guid.NewGuid();
                _isNewId = true;
            }
        }

        //TODO: this serice should make all the calls to hand audit, and error logging for services
        public void BeginAudit(IDTO idto, string entityName, bool setNewId = true)
        {
            _idto = idto;
            _entityName = entityName;

            if (setNewId)
                SetId(_idto);
        }

        public void EndAudit()
        {
            var canArchive = _idto.GetType().IsAssignableFrom(typeof(IDTOArchive));

            if (_isNewId)
            {
                _systemAuditService.LogCreate(_idto.Id, _entityName, _idto);
            }
            else
            {
                if (canArchive)
                {
                    _idto = (IDTO)(_idto as IDTOArchive);
                    _systemAuditService.LogArchive(_idto.Id, _entityName, _idto);
                }
                else
                    _systemAuditService.LogUpdate(_idto.Id, _entityName, _idto);
            }
        }

        //todo: lets bind services to guids to be safer
        //todo: derrive method and service from base class (try not to use reflection)
        //todo: defaultAllowAll should be in config
        private protected void AuthorizeService(bool defaultAllowAll = true)
        {
            if (!defaultAllowAll)
            {

                //Global Has or denies
                //todo: derrive applicationID from service
                // IEnumerable<IApplicationRoleAccess> applicationRoles = _applicationRoleAccessService.FetchByApplication(_applicationId);
                IEnumerable<IServiceAuthorization> applicationRoles = _systemAuthorizationService.Fetch();

                //todo: getting by ID would add some consistency 
                var rolesAllowed = applicationRoles.Where(x => x.ServiceName == _serviceName).Select(y => y.ApplicationRoleId);

                IEnumerable<Guid> AccountRoleIDs = _AccountContext.RoleIds;

                var hasAccess = AccountRoleIDs.Any(rolesAllowed.Contains);
                //var hasAny = AccountRoleIDs.Any(rolesAllowed.Contains);

                if (!hasAccess)
                    throw new ServiceException();
            }
        }

        //string serviceName, string serviceAction, IEnumerable<Guid> AccountRoleIDs, string[] actionRoles
        private protected void AuthorizeAction(string serviceAction)
        {
            if (!_ignoreContext)
            {
                //Global Has or denies
                IEnumerable<IServiceAuthorization> applicationRoles = _systemAuthorizationService.Fetch();

                //select the roles that apply to this
                //todo: getting by ID would add some consistency 
                //todo: StringComparison.OrdinalIgnoreCase
                var rolesAllowed = applicationRoles.Where(x =>
                    string.Equals(x.ServiceName, _serviceName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.ActionName, serviceAction, StringComparison.OrdinalIgnoreCase))
                    .Select(y => y.ApplicationRoleId);

                IEnumerable<Guid> AccountRoleIDs = _AccountContext.RoleIds;
                var hasAccess = AccountRoleIDs.Any(rolesAllowed.Contains);
            }
            //if (!hasAccess)
            //    throw new ServiceException();
        }

        private bool IsAuthorized()
        {
            return true;
            //AuthorizationAttribute athorizationAttribute = (AuthorizationAttribute)Attribute.GetCustomAttribute(propertyInfo, typeof(AuthorizationAttribute));
        }

        //TODO: extend filter functions like this to extensible framework, we do this thing on every search
        private List<string> ParseSearchWords(string searchString)
        {
            if (string.IsNullOrEmpty(searchString))
                return new List<string>();

            char[] delimiterChars = { ' ', ',', '|', '\t' };
            var searchWords = searchString.Split(delimiterChars).ToList();

            return searchWords;
        }

        private List<string> ConstrainSearchWords(List<string> searchWords, int minWordLength)
        {
            if (searchWords.Count == 0)
                return searchWords;

            var validatedWords = searchWords.Where(i => i.Length > minWordLength).ToList().ConvertAll(i => i.ToLower());
            return validatedWords;
        }
    }
}
