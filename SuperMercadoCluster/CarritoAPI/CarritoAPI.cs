using System.Fabric;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using CommunicationLayer;
using CarritoAPI.WorkloadDistributor;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.V2.FabricTransport.Runtime;

namespace CarritoAPI
{
    /// <summary>
    /// FabricRuntime crea una instancia de esta clase para cada instancia de tipo de servicio.
    /// </summary>
    internal sealed class CarritoAPI : StatelessService, ICarritoAPI
    {
        public CarritoAPI(StatelessServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Reemplazo opcional para crear clientes de escucha (como TCP, HTTP) para esta instancia del servicio.
        /// </summary>
        /// <returns>La colección de clientes de escucha.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            var listener = this.CreateServiceRemotingInstanceListeners().ToList();
            var defaultListener =  new ServiceInstanceListener[]
            {
                new ServiceInstanceListener(serviceContext =>
                    new KestrelCommunicationListener(serviceContext, "ServiceEndpoint", (url, listener) =>
                    {
                        ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                        var builder = WebApplication.CreateBuilder();

                        builder.Services.AddSingleton<StatelessServiceContext>(serviceContext);
                        builder.WebHost
                                    .UseKestrel()
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                    .UseUrls(url);
                        
                        // Add services to the container.
                        
                        builder.Services.AddControllers();
                        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
                        builder.Services.AddEndpointsApiExplorer();
                        builder.Services.AddSwaggerGen();
                        
                        var app = builder.Build();
                        
                        // Configure the HTTP request pipeline.
                        if (app.Environment.IsDevelopment())
                        {
                            app.UseSwagger();
                            app.UseSwaggerUI();
                        }
                        
                        app.UseAuthorization();
                        
                        app.MapControllers();
                        
                        return app;


                    }))
            };
            listener.Add(defaultListener[0]);
            listener.Add(new ServiceInstanceListener(serviceContext =>
            new FabricTransportServiceRemotingListener(
                serviceContext,
                this,
                new FabricTransportRemotingListenerSettings
                {
                    ExceptionSerializationTechnique = FabricTransportRemotingListenerSettings.ExceptionSerialization.Default,
                }),
             "ServiceEndpointV2"));
            return listener;
        }

        /// <summary>
        /// Un nodo nos notifica si tiene o no suficiente stock para procesar la transaccion con id "transactionId".
        /// Cada vez que seamos notificados bajamos un semaforo asociado a esa transacción por el cual debería estar esperando ProductWorkloadDistributor
        /// </summary>
        /// <param name="transactionid">La transaccion verificada</param>
        /// <param name="result">El resultado de la verificacion</param>
        /// <returns>Una tarea que sigue el progreso de la función</returns>
        public async Task NotifyStockTransactionState(Guid transactionid, bool result)
        {
            Tuple<CountdownEvent, CancellationTokenSource> controlBlock = ProductWorkloadDistributor.pendingTransactions[transactionid];
            var pendingClustersSemaphore = controlBlock.Item1;
            if (result)
            {
                pendingClustersSemaphore.Signal();
                ServiceEventSource.Current.ServiceMessage(this.Context, $"API: Bajo cde, queda en {pendingClustersSemaphore.CurrentCount}");
            }
            else
            {
                //Algun nodo fallo su transaccion
                ServiceEventSource.Current.ServiceMessage(this.Context, $"API: Fallo un nodo. Cancelamos el semaforo asociado a tx {transactionid}");
                controlBlock.Item2.Cancel();
            }
        }
    }
}
