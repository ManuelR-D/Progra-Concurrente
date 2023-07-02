using System.Collections.Concurrent;
using System.Fabric;
using System.Fabric.Management.ServiceModel;
using System.Runtime.CompilerServices;
using System.Transactions;
using CommunicationLayer;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.V2.FabricTransport.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace CarritoBackend
{
    /// <summary>
    /// El runtime de Service Fabric crea una instancia de esta clase para cada réplica de servicio.
    /// </summary>
    internal sealed class CarritoBackend : StatefulService, ICarritoBackend
    {
        /// <summary>
        /// Diccionario que mantiene un id unico de transaccion asociado con un semaforo. Se usa para esperar el ACK de CarritoAPI
        /// con el resultado de operacion de los demas nodos
        /// </summary>
        private ConcurrentDictionary<Guid, CountdownEvent> pendingTransactions = new ConcurrentDictionary<Guid, CountdownEvent>();
        private const string SERVICE_ENDPOINT = "fabric:/SupermercadoCluster/CarritoAPI";
        private static Uri serviceUri = new Uri(SERVICE_ENDPOINT);
        public CarritoBackend(StatefulServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Guarda el resultado "result" de la transaccion "transactionId" en el diccionario distribuido y levanta el semaforo asociado a dicha transaccion
        /// </summary>
        /// <param name="transactionId">Id de la transaccion</param>
        /// <param name="result">Resultado de la transaccion</param>
        /// <returns>Una Task</returns>
        public async Task AcknowledgeTransaction(Guid transactionId, bool result)
        {
            var myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, bool>>("finishedTransactions");
            using (var tx = this.StateManager.CreateTransaction())
            {
                await myDictionary.AddAsync(tx, transactionId, result);
                await tx.CommitAsync();
            }

            ServiceEventSource.Current.ServiceMessage(this.Context, $"Recibi ACK de tx {transactionId} con resultado {result}");
            pendingTransactions[transactionId].Signal();
        }

        /// <summary>
        /// Devuelve datos de relevancia de la instancia actual
        /// </summary>
        /// <returns>El nombre del servicio y la particion actual</returns>
        public async Task<string> GetServiceDetails()
        {
            var serviceName = this.Context.ServiceName.ToString();
            var partitionId = this.Context.PartitionId.ToString();

            return $"{serviceName} ::: {partitionId}";
        }

        /// <summary>
        /// Agrega un transactionId con un mutex al diccionario concurrente
        /// </summary>
        /// <param name="transactionId"></param>
        /// <returns></returns>
        public async Task AddPurchaseTransaction(Guid transactionId)
        {
            var txMutex = new CountdownEvent(1);
            ServiceEventSource.Current.ServiceMessage(this.Context, $"Agrego txID: {transactionId}");
            pendingTransactions.TryAdd(transactionId, txMutex);
        }

        /// <summary>
        /// Procesa el descuento del stock segun la lista de productos provista.
        /// Esta funcion se ejecuta en un entorno distribuido, por lo que:
        ///     1. Verifica si tiene stock suficiente para la lista de productos
        ///     2. Notifica a CarritoAPI que puede ejecutar la transaccion con el id provisto
        ///     3. Espera ACK de CarritoAPI
        ///     4. Descuenta el stock según la respuesta de CarritoAPI. 
        ///        Notar que es posible que CarritoAPI pida abortar la transacción porque algún nodo no tiene stock o se cayó
        /// 
        /// Para esperar el ACK se usa un semáforo bloqueante que se libera en AcknowledgeTransaction, que debería ser llamada por CarritoAPI
        /// luego de que todos los demas nodos terminen de verificar su stock (pasos 1 y 2)
        /// </summary>
        /// <param name="products">Productos a descontar del stock</param>
        /// <param name="transactionId">Id de la transaccion actual. Sirve para manter un registro con CarritoAPI</param>
        /// <returns>Si se pudo o no procesar la compra</returns>
        public async Task<bool> ProcessPurchase(IList<Product> products, Guid transactionId)
        {
            var txResult = false;
            try
            {
                // Esto obtiene una conexión al proceso que está ejecutando el serviccio que implemente "ICarritoAPI". Se puede usar para 
                //  llamar a métodos de dicho objeto de forma remota. Como ICarritoAPI es un Stateless Service, no es necesario indicar el
                //  id del proceso, puesto que de haber más de uno todos deberían ser indistintos y Service Fabric devuelve uno aleatorio.
                var apiService = ServiceProxy.Create<ICarritoAPI>(serviceUri);

                using (var tx = this.StateManager.CreateTransaction())
                {
                    var productDatabase = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>(tx, "ProductStock");
                    
                    var isEnoughStock = await VerifyStock(products);

                    //No tenemos suficiente stock, avisamos a API y abortamos
                    if (!isEnoughStock) { apiService.NotifyStockTransactionState(transactionId, false); tx.Abort(); }

                    //Tenemos stock de todos los productos, notificamos que podriamos continuar y esperamos respuesta del servidor
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"Tengo stock para txID: {transactionId}");
                    await apiService.NotifyStockTransactionState(transactionId, true);

                    //Esperamos a que todos los demas nodos den ACK a API y luego API a nosotros.
                    ConditionalValue<bool> distributedTxRes = await GetDistributedTransactionResultAsync(transactionId, tx);
                    if (!distributedTxRes.HasValue) { throw new Exception("Posible error de concurrencia. Se recibio ACK de todos los nodos pero el resultado de la transaccion no existe en el diccionario"); }

                    ServiceEventSource.Current.ServiceMessage(this.Context, $"La tx {transactionId} termina en {distributedTxRes.Value}");
                    //CarritoAPI nos pidio abortar, salimos
                    if (!distributedTxRes.Value) { tx.Abort(); }

                    txResult = await TryUpdateStock(products);

                    await tx.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Write(ex.Message);
            }
            return txResult;
        }

        #region CODIGO_AUTOGENERADO
        /// <summary>
        /// Reemplazo opcional para crear clientes de escucha (por ejemplo, HTTP, comunicación remota del servicio, WCF, etc.) de forma que esta réplica del servicio controle las solicitudes de cliente o de usuario.
        /// </summary>
        /// <remarks>
        /// Para obtener más información sobre la comunicación entre servicios, vea https://aka.ms/servicefabricservicecommunication.
        /// </remarks>
        /// <returns>Una colección de clientes de escucha.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            var listeners = this.CreateServiceRemotingReplicaListeners().ToList();
            listeners.Add(new ServiceReplicaListener(serviceContext =>
            new FabricTransportServiceRemotingListener(
                serviceContext,
                this,
                new FabricTransportRemotingListenerSettings
                {
                    ExceptionSerializationTechnique = FabricTransportRemotingListenerSettings.ExceptionSerialization.Default,
                }),
             "ServiceEndpointV2"));
            return this.CreateServiceRemotingReplicaListeners();
        }
        #endregion
        
        /// <summary>
        /// Este es el punto de entrada principal para la réplica del servicio.
        /// Este método se ejecuta cuando esta réplica del servicio pasa a ser principal y tiene estado de escritura.
        /// </summary>
        /// <param name="cancellationToken">Se cancela cuando Service Fabric tiene que cerrar esta réplica del servicio.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            var info = (Int64RangePartitionInformation)this.Partition.PartitionInfo;
            var lowKey = info.LowKey;
            var mapNameToPartition = new Dictionary<long, string>()
            {
                { 0, "Pan" },
                { 1 , "CocaCola"},
                { 2 , "Notebook"}
            };

            await InitializeMemoryDb();
            var myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>("ProductStock");

            //Mostramos por consola cada 5 segundos el estado de la bdd
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var tx = this.StateManager.CreateTransaction())
                {
                    var currentName = mapNameToPartition[lowKey];
                    var currentStock = 0;

                    var result = await myDictionary.TryGetValueAsync(tx, currentName);
                    currentStock = result.HasValue ? result.Value : 0;
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"{currentName} -> {currentStock}");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        /// <summary>
        /// Actualiza el stock segun la lista de productos
        /// </summary>
        /// <param name="products">Lista de productos a descontar del stock</param>
        /// <returns>Resultado de la operacion</returns>
        private async Task<bool> TryUpdateStock(IList<Product> products)
        {
            var txResult = false;
            using (var tx = this.StateManager.CreateTransaction())
            {
                var productDatabase = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>(tx, "ProductStock");
                foreach (var product in products)
                {
                    var prod = await productDatabase.TryGetValueAsync(tx, product.Name);
                    if (prod.HasValue)
                        await productDatabase.SetAsync(tx, product.Name, prod.Value - product.Quantity);
                    
                }
                txResult = true;
                await tx.CommitAsync();
            }

            return txResult;
        }

        /// <summary>
        /// Crea la bdd en memoria
        /// </summary>
        /// <returns>Task que registra el progreso de las operaciones</returns>
        private async Task InitializeMemoryDb()
        {
            var inMemoryDb = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>("ProductStock");

            using (var tx = this.StateManager.CreateTransaction())
            {

                var resultPan = await inMemoryDb.TryGetValueAsync(tx, "Pan");
                var resultCoca = await inMemoryDb.TryGetValueAsync(tx, "CocaCola");
                var resultNote = await inMemoryDb.TryGetValueAsync(tx, "Notebook");

                if (!resultPan.HasValue)
                    await inMemoryDb.SetAsync(tx, "Pan", 100);
                if (!resultCoca.HasValue)
                    await inMemoryDb.SetAsync(tx, "CocaCola", 100);
                if (!resultNote.HasValue)
                    await inMemoryDb.SetAsync(tx, "Notebook", 100);

                await tx.CommitAsync();
            }
        }

        /// <summary>
        /// Verificamos si hay suficiente stock para satisfacer la lista de productos a comprar
        /// </summary>
        /// <param name="products">La lista de productos a comprar</param>
        /// <returns>Una tarea que encapsula un flag que representa si hay o no stock</returns>
        private async Task<bool> VerifyStock(IList<Product> products)
        {
            using (var tx = this.StateManager.CreateTransaction())
            {
                var productDatabase = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>(tx, "ProductStock");

                foreach (Product product in products)
                {
                    var result = await productDatabase.TryGetValueAsync(tx, product.Name);

                    if (!result.HasValue || result.HasValue && result.Value < product.Quantity)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Informa 
        /// Espera en un mutex hasta que todos los otros nodos hayan procesado la transaccion con el id "transactionId" y lee el resultado que
        /// informó CarritoAPI
        /// </summary>
        /// <param name="transactionId"></param>
        /// <param name="apiService"></param>
        /// <param name="tx"></param>
        /// <returns></returns>
        private async Task<ConditionalValue<bool>> GetDistributedTransactionResultAsync(Guid transactionId, ITransaction tx)
        {
            ServiceEventSource.Current.ServiceMessage(this.Context, $"Espero a los demas nodos");

            var waitForOtherClustersMutex = pendingTransactions[transactionId];
            waitForOtherClustersMutex.Wait();

            ServiceEventSource.Current.ServiceMessage(this.Context, $"Recibi broadcast de API");
            var finishedTransactions = await this.StateManager.GetOrAddAsync<IReliableDictionary<Guid, bool>>("finishedTransactions");

            var txRes = await finishedTransactions.TryGetValueAsync(tx, transactionId);
            return txRes;
        }
    }
}
