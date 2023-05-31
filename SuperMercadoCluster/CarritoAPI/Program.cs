using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CarritoAPI
{
    internal static class Program
    {
        /// <summary>
        /// Este es el punto de entrada del proceso de host del servicio.
        /// </summary>
        private static void Main()
        {
            try
            {
                // El archivo ServiceManifest.XML define uno o varios nombres de tipo de servicio.
                // Cuando se registra un servicio, se asigna un nombre de tipo de servicio a un tipo .NET.
                // Cuando Service Fabric crea una instancia de este tipo de servicio,
                // se crea una instancia de la clase en este proceso de host.

                ServiceRuntime.RegisterServiceAsync("CarritoAPIType",
                    context => new CarritoAPI(context)).GetAwaiter().GetResult();

                ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(CarritoAPI).Name);

                // Impide que termine este proceso de host para que los servicios continúen ejecutándose. 
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}
