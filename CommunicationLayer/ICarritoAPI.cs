using System;
using Microsoft.ServiceFabric.Services.Remoting;

namespace CommunicationLayer
{
    public interface ICarritoAPI : IService
    {
        Task NotifyStockTransactionState(Guid transactionid, bool result);
    }
}
