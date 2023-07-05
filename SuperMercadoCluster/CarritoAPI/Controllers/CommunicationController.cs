using CarritoAPI.PartitionDistributor;
using CarritoAPI.WorkloadDistributor;
using CommunicationLayer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Operation = CarritoAPI.WorkloadDistributor.ProductWorkloadDistributor.Operation;
using ServicePartitionKey = Microsoft.ServiceFabric.Services.Client.ServicePartitionKey;

namespace CarritoAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CommunicationController : ControllerBase
    {
        private const string SERVICE_ENDPOINT = "fabric:/SupermercadoCluster/CarritoBackend";
        private static Uri serviceUri = new Uri(SERVICE_ENDPOINT);
        private static IDictionary<string, int> mapCategoryToPartition = new Dictionary<string, int>
        {
            { "Alimentos", 0 },
            { "Bebidas", 1 },
            { "Tecno", 2 },
        };


        /// <summary>
        /// Get the service details executing in the partition id provided
        /// </summary>
        /// <param name="partitionId">Partition id of the service</param>
        /// <returns></returns>
        [HttpGet]
        [Route("backendcluster")]
        public async Task<string> BackendClusterGet(int partitionId)
        {
            if (partitionId > mapCategoryToPartition.Count)
            {
                return $"No existe la particion. Este cluster solo tiene {mapCategoryToPartition.Count} particiones";
            }
            var clusterProxy = ServiceProxy.Create<ICarritoBackend>(serviceUri, new ServicePartitionKey(partitionId));
            var serviceName = await clusterProxy.GetServiceDetails();

            return serviceName;
        }

        /// <summary>
        /// Get the partition that is handling the category provided
        /// </summary>
        /// <param name="categoryName">The category name</param>
        /// <returns>The partition that handles the category name</returns>
        [HttpGet]
        [Route("nodofromcategoryname")]
        public async Task<string> CategoryNameToClusterPartitionGet(string categoryName)
        {
            if (!mapCategoryToPartition.TryGetValue(categoryName, out int partitionId))
            {
                return "No hay particion asignada a la categoria elegida";
            }
            var clusterProxy = ServiceProxy.Create<ICarritoBackend>(serviceUri, new ServicePartitionKey(partitionId));
            var serviceName = await clusterProxy.GetServiceDetails();

            return serviceName;
        }

        /// <summary>
        /// Get all the available categories
        /// </summary>
        /// <returns>The available categories</returns>
        [HttpGet]
        [Route("allcategories")]
        public List<string> AllCategoriesGet() => mapCategoryToPartition.Keys.ToList();

        private static SemaphoreSlim _maxQueries = new SemaphoreSlim(1,1);
        /// <summary>
        /// Reduce the stock of the partitions from the list of products.
        /// </summary>
        /// <param name="purchaseCart">The list of products</param>
        /// <returns>Whether or not the transaction failed</returns>
        [HttpPost]
        [Route("processpurchase")]
        public async Task<bool> ProcessPurchase(IList<Product> purchaseCart)
        {
            var distributor = GetDefaultDistributorByCategory();
            _maxQueries.Wait();
            var result = await ProductWorkloadDistributor.DistributeWorkload<ICarritoBackend>(purchaseCart, Operation.PROCESS, distributor.DistributeProdcutsByCategory);
            _maxQueries.Release();
            return result;
        }

        private static ProductPartitionDistributorByCategory GetDefaultDistributorByCategory() => new ProductPartitionDistributorByCategory(mapCategoryToPartition);
    }
}