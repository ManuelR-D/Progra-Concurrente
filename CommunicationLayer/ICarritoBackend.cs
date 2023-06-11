using Microsoft.ServiceFabric.Services.Remoting;

namespace CommunicationLayer
{
    public interface IServiceProductWorker : IService
    {
        Task<bool> ProcessPurchase(List<Product> products);
    }
    public interface IServiceProductVerifier : IService
    {
        Task<bool> IsEnoughStock(List<Product> prodcuts);
    }
    public interface ICarritoBackend : IServiceProductWorker, IServiceProductVerifier
    {
        Task<string> GetServiceDetails();
    }
}
