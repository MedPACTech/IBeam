using System;

namespace IBeam.Scaffolding.Models.API
{
    public class RegisterResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public Guid Id { get; set; }
    }
}