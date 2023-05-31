using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunicationLayer;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace CarritoBackend
{
    /// <summary>
    /// El runtime de Service Fabric crea una instancia de esta clase para cada réplica de servicio.
    /// </summary>
    internal sealed class CarritoBackend : StatefulService, ICarritoBackend
    {
        public CarritoBackend(StatefulServiceContext context)
            : base(context)
        { }

        public async Task<string> GetServiceDetails()
        {
            var serviceName = this.Context.ServiceName.ToString();
            var partitionId = this.Context.PartitionId.ToString();

            return $"{serviceName} ::: {partitionId}";
        }

        public async Task<bool> IsEnoughStock(List<Product> products)
        {
            var myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>("ProductStock");
            using (var tx = this.StateManager.CreateTransaction())
            {
                foreach (Product product in products)
                {
                    var result = await myDictionary.TryGetValueAsync(tx, product.Name);

                    if (!result.HasValue || result.HasValue && result.Value < product.Quantity)
                        return false;

                }

                await tx.CommitAsync();
            }
            return true;
        }

        public async Task<bool> ProcessPurchase(List<Product> products)
        {
            var myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>("ProductStock");
            using (var tx = this.StateManager.CreateTransaction())
            {
                foreach (Product product in products)
                {
                    var result = await myDictionary.TryGetValueAsync(tx, product.Name);

                    if (!result.HasValue || result.HasValue && result.Value < product.Quantity)
                        return false;

                    await myDictionary.TryUpdateAsync(tx, product.Name, result.Value - product.Quantity, result.Value);
                }

                await tx.CommitAsync();
            }
            return true;
        }

        /// <summary>
        /// Reemplazo opcional para crear clientes de escucha (por ejemplo, HTTP, comunicación remota del servicio, WCF, etc.) de forma que esta réplica del servicio controle las solicitudes de cliente o de usuario.
        /// </summary>
        /// <remarks>
        /// Para obtener más información sobre la comunicación entre servicios, vea https://aka.ms/servicefabricservicecommunication.
        /// </remarks>
        /// <returns>Una colección de clientes de escucha.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return this.CreateServiceRemotingReplicaListeners();
        }

        /// <summary>
        /// Este es el punto de entrada principal para la réplica del servicio.
        /// Este método se ejecuta cuando esta réplica del servicio pasa a ser principal y tiene estado de escritura.
        /// </summary>
        /// <param name="cancellationToken">Se cancela cuando Service Fabric tiene que cerrar esta réplica del servicio.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: Reemplace el siguiente código de ejemplo por su propia lógica 
            //       o quite este reemplazo de RunAsync si no es necesario en su servicio.

            var myDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, int>>("ProductStock");

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var tx = this.StateManager.CreateTransaction())
                {
                    var resultPan = await myDictionary.TryGetValueAsync(tx, "Pan");
                    var resultCoca = await myDictionary.TryGetValueAsync(tx, "CocaCola");
                    var resultNote = await myDictionary.TryGetValueAsync(tx, "Notebook");

                    ServiceEventSource.Current.ServiceMessage(this.Context, "Pan {0}; Coca {1}; Note {2}",
                        resultPan.HasValue ? resultPan.Value.ToString() : "Value does not exist.",
                        resultCoca.HasValue ? resultCoca.Value.ToString() : "Value does not exist.",
                        resultNote.HasValue ? resultNote.Value.ToString() : "Value does not exist.");

                    if (!resultPan.HasValue)
                        await myDictionary.SetAsync(tx, "Pan", 10);
                    if (!resultCoca.HasValue)
                        await myDictionary.SetAsync(tx, "CocaCola", 10);
                    if (!resultNote.HasValue)
                        await myDictionary.SetAsync(tx, "Notebook", 10);
                    // Si se produce una excepción antes de llamar a CommitAsync, se anula la transacción, se descartan todos los cambios
                    // y no se guarda nada en las réplicas secundarias.
                    await tx.CommitAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}
