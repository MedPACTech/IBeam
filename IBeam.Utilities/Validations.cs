using IBeam.Models.API;
using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IBeam.Utilities
{
    public static class Validations
    {

       public static bool IsEmail(string email)
        {
            var trimmedEmail = email.Trim();

            if (trimmedEmail.EndsWith("."))
            {
                return false; 
            }
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == trimmedEmail;
            }
            catch
            {
                return false;
            }
            
        }

        public static bool IsPhone(string phoneNumber)
        {

            string pattern = @"^(?:\(?)(?<AreaCode>\d{3})(?:[\).\s]?)(?<Prefix>\d{3})(?:[-\.\s]?)(?<Suffix>\d{4})(?!\d)";
            Match match = Regex.Match(phoneNumber, pattern);
            if (match.Success)
                return true;

            return false;

        }


    }
}
