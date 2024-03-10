using System;

namespace IBeam.Services
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    internal class AuthorizeMethodAttribute : Attribute
    {
        public string id;
        public string name;

        public AuthorizeMethodAttribute()
        {

        }

        //public AuthorizeActionAttribute(Guid id)
        //{
        //    this._id = id;
        //}
    }
}