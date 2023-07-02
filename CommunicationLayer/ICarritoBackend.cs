using Microsoft.ServiceFabric.Services.Remoting;

namespace CommunicationLayer
{
    public interface IServiceProductWorker : IService
    {
        Task<bool> ProcessPurchase(IList<Product> products, Guid transactionId);

        Task AcknowledgeTransaction(Guid transactionId, bool result);

        Task AddPurchaseTransaction(Guid transactionId);
    }
    public interface ICarritoBackend : IServiceProductWorker
    {
        Task<string> GetServiceDetails();
    }
}
