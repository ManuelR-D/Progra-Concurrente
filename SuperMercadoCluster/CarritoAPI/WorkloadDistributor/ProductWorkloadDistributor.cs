using CommunicationLayer;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using System.Collections.Concurrent;
using CarritoAPI.PartitionDistributor;
using ServicePartitionKey = Microsoft.ServiceFabric.Services.Client.ServicePartitionKey;

namespace CarritoAPI.WorkloadDistributor
{
    /// <summary>
    /// Clase responsable de separar el procesamiento de una lista de productos entre los nodos disponibles
    /// </summary>
    public static class ProductWorkloadDistributor
    {
        private static string SERVICE_ENDPOINT = "fabric:/SupermercadoCluster/CarritoBackend";
        private static Uri SERVICE_URI = new Uri(SERVICE_ENDPOINT);

        /// <summary>
        /// Diccionario que relaciona un id de transaccion con un bloque de control compuesto por un semaforo y un token de cancelacion.
        ///  El semaforo se va a usar para esperar el resultado de la transaccion en el CarritoBackend. El token de cancelacion se va a activar
        ///  por CarritoAPI si algún nodo de CarritoBackend no tiene stock
        /// </summary>
        public static ConcurrentDictionary<Guid, Tuple<CountdownEvent, CancellationTokenSource>> pendingTransactions = new ConcurrentDictionary<Guid, Tuple<CountdownEvent, CancellationTokenSource>>();
        
        public static IDictionary<Guid, bool> finishedTransactions = new Dictionary<Guid, bool>();

        public enum Operation
        {
            VERIFY,
            PROCESS
        }

        /// <summary>
        /// Estrategia para separar una lista de productos en N listas de productos, una por particion.
        ///  Por ejemplo, un ProductDistributor por categoria debería recivir una lista de productos y retornar una lista con N listas de productos, una por categoria:
        ///     [Bimbo: 10, Lactal: 20, Coca: 20, Pepsi: 10, Notebook: 5]
        ///  Debería devolver:
        ///     [
        ///       [Bimbo: 10, Lactal: 20],
        ///       [Coca: 20, Pepsi: 10],
        ///       [Notebook: 5]
        ///     ]
        /// Para ver un ejemplo de implementacion, revisar <see cref="ProductPartitionDistributorByCategory"/>
        /// </summary>
        /// <param name="products">Productos a distribuir</param>
        /// <returns>N Listas de productos</returns>
        public delegate List<IList<Product>> ProductDistributor (IList<Product> products);

        /// <summary>
        /// Distribuye la carga entre las particiones.
        /// </summary>
        /// <typeparam name="TServiceInPartition">Cualquier servicio capaz de procesar compras</typeparam>
        /// <param name="productsToDistribute">Productos a distribuir entre las particiones</param>
        /// <param name="op">Operacion a ejecutar</param>
        /// <param name="productDistributorStrategy">Un metodo capaz de separar una lista de productos en N listas de productos, una para cada paritcion</param>
        /// <returns>List of tasks with the result of each operation</returns>
        public async static Task<bool> DistributeWorkload<TServiceInPartition>(IList<Product> productsToDistribute, Operation op, ProductDistributor productDistributorStrategy)
            where TServiceInPartition : IServiceProductWorker
        {
            List<IList<Product>> distributedPurchaseCartsByIndex = productDistributorStrategy(productsToDistribute);

            var transactionId = await CreateDistributedTransaction<TServiceInPartition>(distributedPurchaseCartsByIndex.Count);

            /* Inicializamos un semaforo en un valor igual a la cantidad de nodos en el cluster. Este semaforo va a reducir 1 cada vez que un nodo de ACK a CarritoAPI. 
             * Ver CarritoAPI->NotifyStockTransactionState()
             */
            var transactionSemaphore = new CountdownEvent(distributedPurchaseCartsByIndex.Count);
            var notEnoughStockCancelToken = new CancellationTokenSource();
            var tuple = Tuple.Create(transactionSemaphore, notEnoughStockCancelToken);
            pendingTransactions.TryAdd(transactionId, tuple);

            var microServices = new List<TServiceInPartition>();
            for(int partitionId = 0; partitionId < distributedPurchaseCartsByIndex.Count; partitionId++)
            {
                IList<Product> products = distributedPurchaseCartsByIndex[partitionId];
                
                var clusterPartition = ServiceProxy.Create<TServiceInPartition>(SERVICE_URI, new ServicePartitionKey(partitionId));
                microServices.Add(clusterPartition);
                switch (op)
                {
                    case Operation.PROCESS:
                        clusterPartition.ProcessPurchase(products, transactionId); break;
                }
            }

            var txResult = true;
            //Esperamos a que el semaforo llegue a cero (el ACK de todos los nodos del cluster)
            try
            {
                transactionSemaphore.Wait(timeout: TimeSpan.FromSeconds(15), cancellationToken: notEnoughStockCancelToken.Token);
            }
            catch (ObjectDisposedException ex) 
            {
                ServiceEventSource.Current.Write($"WorkloadDistributor: Semaforo destruido. Algun nodo no pudo completar su transaccion o hubo timeout");
                txResult = false;
            }
            catch (OperationCanceledException ex)
            {
                ServiceEventSource.Current.Write($"WorkloadDistributor: Operacion cancelada o token destruido. Algun nodo no pudo completar su transaccion por falta de stock o hubo timeout");
                txResult = false;
            }

            foreach (var workerService in microServices)
            {
                /* Broadcast del resultado a todos los nodos para que finalicen o aborten la transaccion. 
                 * Si cualquiera de los nodos no tenia suficiente stock, txResult va a ser falso.
                */
                await workerService.AcknowledgeTransaction(transactionId, txResult);
            }

            return txResult;
        }

        /// <summary>
        /// Genera el Id unico y lo envia al servicio "TServiceInPartition" en cada nodo
        /// </summary>
        /// <typeparam name="TServiceInPartition">El servicio con el cual comunicarse</typeparam>
        /// <param name="participants">La cantidad de nodos</param>
        /// <returns>El id generado</returns>
        private static async Task<Guid> CreateDistributedTransaction<TServiceInPartition>(int participants) 
            where TServiceInPartition : IServiceProductWorker
        {
            Guid transactionId = Guid.NewGuid();
            try
            {

                for (int partitionId = 0; partitionId < participants; partitionId++)
                {
                    var clusterPartition = ServiceProxy.Create<TServiceInPartition>(SERVICE_URI, new ServicePartitionKey(partitionId));
                    await clusterPartition.AddPurchaseTransaction(transactionId);
                }
            }
            catch(Exception ex) 
            {
                
            }


            return transactionId;
        }
    }
}
