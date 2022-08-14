using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text;
using Microsoft.Azure.ServiceBus;
using System.Threading;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;

    private readonly string url = "https://eshoponwebfunction.azurewebsites.net/api/OrderItemsReserver?code=1LvrCYD0kgbMMnXOl2czFddz8pNnPazYQhWSotzmm4mXAzFu_lc1yg==";
    private readonly string serviceBusConnectionString = "Endpoint=sb://eshoponwebservicebus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SDw4M6tcGNm0snK0vPVYHuD1KgkE7aA00//keMMfWGM=";
    private readonly string queueName = "queue1";
    static IQueueClient queueClient;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.GetBySpecAsync(basketSpec);

        Guard.Against.NullBasket(basketId, basket);
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        HttpClient client = new HttpClient();
        var json = JsonConvert.SerializeObject(order);
        var message = new Message(Encoding.UTF8.GetBytes(json.ToString()));
        queueClient = new QueueClient(serviceBusConnectionString, queueName);
        var data = new StringContent(json, Encoding.UTF8, "application/json");
        await queueClient.SendAsync(message);
        await queueClient.CloseAsync();
        var response = await client.PostAsync(url, data);
        var statusCode = response.StatusCode.ToString();

        if(statusCode == "Internal Server Error")
        {
            var jsonData = JsonConvert.SerializeObject(new
            {
                email = "liliia_khimiak@epam.com",
                due = "4/1/2020",
                task = "My new task!"
            });
            HttpResponseMessage result = await client.PostAsync(
           "https://eshoponwebloggicapp.azurewebsites.net:443/api/mYlOGGICaPP/triggers/manual/invoke?api-version=2022-05-01&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=FPPNzH-9yboFCE64YyiIzztUVXs7ANe7udfUVNvYDPs",
           new StringContent(jsonData, Encoding.UTF8, "application/json"));
        }
       

        var text = await response.Content.ReadAsStringAsync();

        await _orderRepository.AddAsync(order);
    }
}
