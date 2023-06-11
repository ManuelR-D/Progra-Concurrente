using CommunicationLayer;
using Microsoft.ServiceFabric.Services.Remoting.Client;

namespace CarritoAPI.WorkloadDistributor
{
    public static class ProductCategoryWorkloadDistributor
    {
        public static IDictionary<string, int> CATEGORY_TO_PARTITION = new Dictionary<string, int>
        {
            { "Alimentos", 0 },
            { "Bebidas", 1 },
            { "Tecno", 2 },
        };

        public enum Operation
        {
            VERIFY,
            PROCESS
        }

        /// Distribute the workload in corresponding partitions.
        /// </summary>
        /// <typeparam name="TServiceInPartition">A Service capable of processing the products or verify them</typeparam>
        /// <typeparam name="TReturnTypeOfWork">Return type of the operation</typeparam>
        /// <param name="productsToDistribute">Products to distribute</param>
        /// <param name="op">Operation to execute</param>
        /// <returns>List of tasks with the result of each operation</returns>
        public static List<Task<bool>> DistributeWorkloadInPartition<TServiceInPartition>(IList<Product> productsToDistribute, Operation op)
            where TServiceInPartition : IServiceProductVerifier, IServiceProductWorker
        {
            var purchaseCartByCategory = new Dictionary<string, List<Product>>()
            {
                { "Alimentos", new List<Product>()},
                { "Bebidas", new List<Product>()},
                { "Tecno", new List<Product>()}
            };

            foreach (var product in productsToDistribute)
            {
                purchaseCartByCategory[product.Category].Add(product);
            }

            var taskPool = new List<Task<bool>>();
            foreach (var categoryProductPair in purchaseCartByCategory)
            {
                var category = categoryProductPair.Key;
                var products = categoryProductPair.Value;
                int partitionId = CATEGORY_TO_PARTITION[category];

                TServiceInPartition clusterPartition = ServiceProxy.Create<TServiceInPartition>(new Uri("fabric:/SupermercadoCluster/CarritoBackend"), new Microsoft.ServiceFabric.Services.Client.ServicePartitionKey(partitionId));
                switch (op)
                {
                    case Operation.VERIFY:
                        taskPool.Add(clusterPartition.IsEnoughStock(products)); break;
                    case Operation.PROCESS:
                        taskPool.Add(clusterPartition.ProcessPurchase(products)); break;
                }
            }

            return taskPool;
        }
    }
}
