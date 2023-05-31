using Microsoft.ServiceFabric.Services.Remoting;

namespace CommunicationLayer
{
    public interface ICarritoBackend : IService
    {
        Task<string> GetServiceDetails();
        Task<bool> IsEnoughStock(List<Product> products);
        Task<bool> ProcessPurchase(List<Product> products);
    }
}
