using CarritoAPI.Model;
using CommunicationLayer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Product = CommunicationLayer.Product;

namespace CarritoAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CommunicationController : ControllerBase
    {
        private IDictionary<string, int> CATEGORY_TO_PARTITION = new Dictionary<string, int>
        {
            { "Alimentos", 0 },
            { "Bebidas", 1 },
            { "Tecno", 2 },
        };

        [HttpGet]
        [Route("backendcluster")]
        public async Task<string> BackendClusterGet(int partitionId)
        {
            if (partitionId > CATEGORY_TO_PARTITION.Count)
            {
                return $"No existe la particion. Este cluster solo tiene {CATEGORY_TO_PARTITION.Count} particiones";
            }
            var clusterProxy = ServiceProxy.Create<ICarritoBackend>(new Uri("fabric:/SupermercadoCluster/CarritoBackend"), new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(partitionId));
            var serviceName = await clusterProxy.GetServiceDetails();

            return serviceName;
        }

        [HttpGet]
        [Route("nodofromcategoryname")]
        public async Task<string> CategoryNameToClusterPartitionGet(string categoryName)
        {
            if(!CATEGORY_TO_PARTITION.TryGetValue(categoryName, out int partitionId))
            {
                return "No hay particion asignada a la categoria elegida";
            }
            var clusterProxy = ServiceProxy.Create<ICarritoBackend>(new Uri("fabric:/SupermercadoCluster/CarritoBackend"), new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(partitionId));
            var serviceName = await clusterProxy.GetServiceDetails();

            return serviceName;
        }

        [HttpGet]
        [Route("allcategories")]
        public List<string> AllCategoriesGet()
        {
            return CATEGORY_TO_PARTITION.Keys.ToList();
        }

        [HttpPost]
        [Route("verifystock")]
        public bool VerifyStock(IList<Product> purchaseCart)
        {
            var purchaseCartByCategory = new Dictionary<string, List<Product>>()
            {
                { "Alimentos", new List<Product>()},
                { "Bebidas", new List<Product>()},
                { "Tecno", new List<Product>()}
            };

            foreach (var product in purchaseCart)
            {
                purchaseCartByCategory[product.Category].Add(product);
            }

            List<Task<bool>> taskPool = new List<Task<bool>>();
            foreach (var categoryProductPair in purchaseCartByCategory)
            {
                var category = categoryProductPair.Key;
                var products = categoryProductPair.Value;

                int partitionId = CATEGORY_TO_PARTITION[category];
                var clusterPartition = ServiceProxy.Create<ICarritoBackend>(new Uri("fabric:/SupermercadoCluster/CarritoBackend"), new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(partitionId));
                taskPool.Add(clusterPartition.IsEnoughStock(products));
            }

            taskPool.ForEach(task => task.Wait());
            foreach (var task in taskPool)
            {
                bool enoughStock = task.Result;
                if (!enoughStock)
                    return false;
            }
            return true;
        }

        [HttpPost]
        [Route("processpurchase")]
        public bool ProcessPurchase(IList<Product> purchaseCart)
        {
            var purchaseCartByCategory = new Dictionary<string, List<Product>>()
            {
                { "Alimentos", new List<Product>()},
                { "Bebidas", new List<Product>()},
                { "Tecno", new List<Product>()}
            };

            foreach (var product in purchaseCart)
            {
                purchaseCartByCategory[product.Category].Add(product);
            }

            List<Task<bool>> taskPool = new List<Task<bool>>();
            foreach (var categoryProductPair in purchaseCartByCategory)
            {
                var category = categoryProductPair.Key;
                var products = categoryProductPair.Value;

                int partitionId = CATEGORY_TO_PARTITION[category];
                var clusterPartition = ServiceProxy.Create<ICarritoBackend>(new Uri("fabric:/SupermercadoCluster/CarritoBackend"), new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(partitionId));
                taskPool.Add(clusterPartition.ProcessPurchase(products));
            }

            taskPool.ForEach(task => task.Wait());
            foreach (var task in taskPool)
            {
                bool processed = task.Result;
                if (!processed)
                    return false;
            }
            return true;
        }
    }
}