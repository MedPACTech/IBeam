using System.Text.Json.Serialization;
using IBeam.Models.Interfaces;

namespace IBeam.Models.API
{
    public class FinishAuthenticateResponse
    {
        public bool Success { get; set; }
        public string AuthToken { get; set; }
        [JsonIgnore] public IRefreshToken RefreshToken { get; set; }
        public IAccount Account { get; set; }
    }
}