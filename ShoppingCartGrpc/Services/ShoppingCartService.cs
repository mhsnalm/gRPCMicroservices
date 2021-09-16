using AutoMapper;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShoppingCartGrpc.Data;
using ShoppingCartGrpc.Models;
using ShoppingCartGrpc.Protos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShoppingCartGrpc.Services
{
    public class ShoppingCartService : ShoppingCartProtoService.ShoppingCartProtoServiceBase
    {
        private readonly ShoppingCartContext _shoppingCartDbContext;
        private readonly DiscountService _discountservice;
        private readonly IMapper _mapper;
        private readonly ILogger<ShoppingCartService> _logger;

        public ShoppingCartService(ShoppingCartContext shoppingCartDbContext, DiscountService discountservice, IMapper mapper, ILogger<ShoppingCartService> logger )
        {
            _shoppingCartDbContext = shoppingCartDbContext ?? throw new ArgumentNullException(nameof(shoppingCartDbContext));
            _discountservice = discountservice ?? throw new ArgumentNullException(nameof(discountservice));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override async Task<ShoppingCartModel> GetShoppingCart(GetShoppingCartRequest request, ServerCallContext context)
        {
            var shoppingCart = await _shoppingCartDbContext.ShoppingCart.FirstOrDefaultAsync(s => s.UserName == request.Username);
            if (shoppingCart == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"ShoppingCart with UserName={request.Username} is not found."));
            }

            var shoppingCartModel = _mapper.Map<ShoppingCartModel>(shoppingCart);
            return shoppingCartModel;
        }

        public override async Task<ShoppingCartModel> CreateShoppingCart(ShoppingCartModel request, ServerCallContext context)
        {
            var shoppingCart = _mapper.Map<ShoppingCart>(request);

            var isExist = await _shoppingCartDbContext.ShoppingCart.AnyAsync(s => s.UserName == shoppingCart.UserName);
            if (isExist)
            {
                _logger.LogError("Invalid UserName for ShoppingCart creation. UserName : {userName}", shoppingCart.UserName);
                throw new RpcException(new Status(StatusCode.NotFound, $"ShoppingCart with UserName={request.Username} is already exist."));
            }

            _shoppingCartDbContext.ShoppingCart.Add(shoppingCart);
            await _shoppingCartDbContext.SaveChangesAsync();

            _logger.LogInformation("ShoppingCart is successfully created.UserName : {userName}", shoppingCart.UserName);

            var shoppingCartModel = _mapper.Map<ShoppingCartModel>(shoppingCart);
            return shoppingCartModel;
        }


        public override async Task<AddItemIntoShoppingCartResponse> AddItemIntoShoppingCart(IAsyncStreamReader<AddItemIntoShoppingCartRequest> requestStream, ServerCallContext context)
        {
            while (await requestStream.MoveNext())
            {
                // Get sc if exist or not
                // Check item if exist in sc or not
                // if item exist +1 quantity
                // if not exist add new item into sc
                // check discount and set the item price

                var shoppingCart = await _shoppingCartDbContext.ShoppingCart.FirstOrDefaultAsync(s => s.UserName == requestStream.Current.Username);
                if (shoppingCart == null)
                {
                    throw new RpcException(new Status(StatusCode.NotFound, $"ShoppingCart with UserName={requestStream.Current.Username} is not found."));
                }

                var newAddedCartItem = _mapper.Map<ShoppingCartItem>(requestStream.Current.NewCartItem);
                var cartItem = shoppingCart.Items.FirstOrDefault(i => i.ProductId == newAddedCartItem.ProductId);
                if (null != cartItem)
                {
                    cartItem.Quantity++;
                }
                else
                {
                    // GRPC CALL DISCOUNT SERVICE -- check discount and set the item price

                    var discount = await _discountservice.GetDiscount(requestStream.Current.DiscountCode);
                    newAddedCartItem.Price -= discount.Amount;

                    shoppingCart.Items.Add(newAddedCartItem);
                }
            }

            var insertCount = await _shoppingCartDbContext.SaveChangesAsync();

            var response = new AddItemIntoShoppingCartResponse
            {
                Success = insertCount > 0,
                InsertCount = insertCount
            };

            return response;
        }
    }
}
