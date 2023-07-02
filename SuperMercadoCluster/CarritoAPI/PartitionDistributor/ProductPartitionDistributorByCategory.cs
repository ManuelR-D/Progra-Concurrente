using CommunicationLayer;

namespace CarritoAPI.PartitionDistributor
{
    /// <summary>
    /// Clase responsable de separar una lista de productos en N listas de productos, una para cada particion.
    /// </summary>
    public class ProductPartitionDistributorByCategory
    {
        private IDictionary<string, int> keyToPartition;

        public ProductPartitionDistributorByCategory(IDictionary<string, int> keyToPartition) 
        {
            this.keyToPartition = keyToPartition;
        }

        /// <summary>
        /// Distribuye una lista de productos en N listas segun categoria.
        ///  Por ejemplo
        ///  Con una keyToPartition
        ///     {
        ///       {V1, 0}
        ///       {V2, 1}
        ///       {V3, 2}
        ///     }
        ///  Y la siguiente lista de productos:
        ///     [{Product1: 10, Category: V1}, {Product2: 20, Category: V1}, {Product3: 20, Category: V2}, {Product4: 10, Category: V2}, {Prodcut5: 5, Category: V3}]
        ///  Devolvemos:
        ///     [
        ///       [{Product1: 10, Category: V1}, {Product2: 20, Category: V1}],
        ///       [{Product3: 20, Category: V2}, {Product4: 10, Category: V2}],
        ///       [{Prodcut5: 5, Category: V3}]
        ///     ]
        /// </summary>
        /// <param name="productsToDistribute">Productos a distribuir</param>
        /// <returns>N Listas de prodcutos</returns>
        public List<IList<Product>> DistributeProdcutsByCategory(IList<Product> productsToDistribute)
        {
            var indexedPurchaseCarts = new List<IList<Product>>(keyToPartition.Count);

            for(int i = 0; i < keyToPartition.Count; i++)
            {
                indexedPurchaseCarts.Add(new List<Product>());
            }

            foreach (var product in productsToDistribute)
            {
                int partitionKey = keyToPartition[product.Category];
                indexedPurchaseCarts[partitionKey].Add(product);
            }

            return indexedPurchaseCarts;
        }
    }
}
