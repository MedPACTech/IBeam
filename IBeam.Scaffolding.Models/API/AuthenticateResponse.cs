using System;

namespace IBeam.Scaffolding.Models.API
{
    public class AuthenticateResponse
    {
        public bool Success { get; set; }
        public string Token { get; set; }
        public Guid Id { get; set; }
    }
}