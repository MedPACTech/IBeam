using System;

namespace IBeam.Models.API
{
    public class PasswordChangeRequest
    {
        public Guid Id { get; set; }
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }
}