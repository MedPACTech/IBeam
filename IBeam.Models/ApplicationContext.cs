using System;
using System.Text.Json.Serialization;

namespace IBeam.Models
{
    public class ApplicationContext
    {
        [JsonPropertyName("customerId")]
        public Guid CustomerId { get; set; }
    }
}