using CarritoAPI.WorkloadDistributor;
using CommunicationLayer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Operation = CarritoAPI.WorkloadDistributor.ProductCategoryWorkloadDistributor.Operation;

namespace CarritoAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CommunicationController : ControllerBase
    {
        private const string SERVICE_ENDPOINT = "fabric:/SupermercadoCluster/CarritoBackend";
        private static Uri serviceUri = new Uri(SERVICE_ENDPOINT);

        [HttpGet]
        [Route("backendcluster")]
        public async Task<string> BackendClusterGet(int partitionId)
        {
            if (partitionId > ProductCategoryWorkloadDistributor.CATEGORY_TO_PARTITION.Count)
            {
                return $"No existe la particion. Este cluster solo tiene {ProductCategoryWorkloadDistributor.CATEGORY_TO_PARTITION.Count} particiones";
            }
            var clusterProxy = ServiceProxy.Create<ICarritoBackend>(serviceUri, new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(partitionId));
            var serviceName = await clusterProxy.GetServiceDetails();

            return serviceName;
        }

        [HttpGet]
        [Route("nodofromcategoryname")]
        public async Task<string> CategoryNameToClusterPartitionGet(string categoryName)
        {
            if(!ProductCategoryWorkloadDistributor.CATEGORY_TO_PARTITION.TryGetValue(categoryName, out int partitionId))
            {
                return "No hay particion asignada a la categoria elegida";
            }
            var clusterProxy = ServiceProxy.Create<ICarritoBackend>(serviceUri, new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(partitionId));
            var serviceName = await clusterProxy.GetServiceDetails();

            return serviceName;
        }

        [HttpGet]
        [Route("allcategories")]
        public List<string> AllCategoriesGet() => ProductCategoryWorkloadDistributor.CATEGORY_TO_PARTITION.Keys.ToList();

        [HttpPost]
        [Route("verifystock")]
        public async Task<bool> VerifyStock(IList<Product> purchaseCart)
        {
            var taskPool = ProductCategoryWorkloadDistributor.DistributeWorkloadInPartition<ICarritoBackend>(purchaseCart, Operation.VERIFY);
            await Task.WhenAll(taskPool);
            return taskPool.All(task => task.Result);
        }

        [HttpPost]
        [Route("processpurchase")]
        public async Task<bool> ProcessPurchase(IList<Product> purchaseCart)
        {
            var taskPool = ProductCategoryWorkloadDistributor.DistributeWorkloadInPartition<ICarritoBackend>(purchaseCart, Operation.PROCESS);
            await Task.WhenAll(taskPool);
            return taskPool.All(task => task.Result);
        }
    }
}