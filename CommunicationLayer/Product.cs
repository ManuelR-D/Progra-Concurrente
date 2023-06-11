using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CommunicationLayer
{
    public class Product
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }
    }
}
