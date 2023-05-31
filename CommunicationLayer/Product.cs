using System.Text.Json.Serialization;

namespace CommunicationLayer
{
    public class Product
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string Category { get; set; }

        public int Quantity { get; set; }
    }
}
