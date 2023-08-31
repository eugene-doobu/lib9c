using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Bencodex.Types;
using Lib9c.Model.Order;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using Nekoyume.Action.Extensions;
using Nekoyume.Battle;
using Nekoyume.Model;
using Nekoyume.Model.Exceptions;
using Nekoyume.Model.Item;
using Nekoyume.Model.Mail;
using Nekoyume.Model.Market;
using Nekoyume.Model.State;
using Nekoyume.Module;
using Nekoyume.TableData;
using static Lib9c.SerializeKeys;

namespace Nekoyume.Action
{
    [ActionType("cancel_product_registration")]
    public class CancelProductRegistration : GameAction
    {
        public const int CostAp = 5;
        public const int Capacity = 100;
        public Address AvatarAddress;
        public List<IProductInfo> ProductInfos;
        public bool ChargeAp;
        public override IWorld Execute(IActionContext context)
        {
            context.UseGas(1);
            if (context.Rehearsal)
            {
                return context.PreviousState;
            }

            var world = context.PreviousState;

            if (!ProductInfos.Any())
            {
                throw new ListEmptyException("ProductInfos was empty.");
            }

            if (ProductInfos.Count > Capacity)
            {
                throw new ArgumentOutOfRangeException($"{nameof(ProductInfos)} must be less than or equal {Capacity}.");
            }

            foreach (var productInfo in ProductInfos)
            {
                productInfo.ValidateType();
                if (productInfo.AvatarAddress != AvatarAddress ||
                    productInfo.AgentAddress != context.Signer)
                {
                    throw new InvalidAddressException();
                }
            }

            if (!AvatarModule.TryGetAvatarStateV2(
                    world,
                    context.Signer,
                    AvatarAddress,
                    out var avatarState,
                    out var migrationRequired))
            {
                throw new FailedLoadStateException("failed to load avatar state");
            }

            if (!avatarState.worldInformation.IsStageCleared(GameConfig.RequireClearedStageLevel.ActionsInShop))
            {
                avatarState.worldInformation.TryGetLastClearedStageId(out var current);
                throw new NotEnoughClearedStageLevelException(AvatarAddress.ToHex(),
                    GameConfig.RequireClearedStageLevel.ActionsInShop, current);
            }

            avatarState.UseAp(
                CostAp,
                ChargeAp,
                LegacyModule.GetSheet<MaterialItemSheet>(world),
                context.BlockIndex,
                LegacyModule.GetGameConfigState(world));
            var productsStateAddress = ProductsState.DeriveAddress(AvatarAddress);
            ProductsState productsState;
            if (LegacyModule.TryGetState(world, productsStateAddress, out List rawProductList))
            {
                productsState = new ProductsState(rawProductList);
            }
            else
            {
                // cancel order before product registered case.
                var marketState = LegacyModule.TryGetState(
                    world,
                    Addresses.Market,
                    out List rawMarketList)
                    ? new MarketState(rawMarketList)
                    : new MarketState();
                productsState = new ProductsState();
                marketState.AvatarAddresses.Add(AvatarAddress);
                world = LegacyModule.SetState(world, Addresses.Market, marketState.Serialize());
            }
            var addressesHex = GetSignerAndOtherAddressesHex(context, AvatarAddress);
            foreach (var productInfo in ProductInfos)
            {
                if (productInfo is ItemProductInfo {Legacy: true})
                {
                    var productType = productInfo.Type;
                    var orderAddress = Order.DeriveAddress(productInfo.ProductId);
                    if (!LegacyModule.TryGetState(world, orderAddress, out Dictionary rawOrder))
                    {
                        throw new FailedLoadStateException(
                            $"{addressesHex} failed to load {nameof(Order)}({orderAddress}).");
                    }

                    var order = OrderFactory.Deserialize(rawOrder);
                    switch (order)
                    {
                        case FungibleOrder _:
                            if (productInfo.Type == ProductType.NonFungible)
                            {
                                throw new InvalidProductTypeException($"FungibleOrder not support {productType}");
                            }

                            break;
                        case NonFungibleOrder _:
                            if (productInfo.Type == ProductType.Fungible)
                            {
                                throw new InvalidProductTypeException($"NoneFungibleOrder not support {productType}");
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(order));
                    }

                    world = SellCancellation.Cancel(
                        context,
                        world,
                        avatarState,
                        addressesHex,
                        order);
                }
                else
                {
                    world = Cancel(productsState, productInfo, world, avatarState, context);
                }
            }

            world = LegacyModule.SetState(world, productsStateAddress, productsState.Serialize());

            if (migrationRequired)
            {
                world = AvatarModule.SetAvatarStateV2(world, AvatarAddress, avatarState);
            }
            else
            {
                world = AvatarModule.SetAvatarV2(world, AvatarAddress, avatarState);
                world = AvatarModule.SetInventory(world, AvatarAddress.Derive(LegacyInventoryKey), avatarState.inventory);
            }

            return world;
        }

        public static IWorld Cancel(
            ProductsState productsState,
            IProductInfo productInfo,
            IWorld world,
            AvatarState avatarState,
            IActionContext context)
        {
            var productId = productInfo.ProductId;
            if (!productsState.ProductIds.Contains(productId))
            {
                throw new ProductNotFoundException($"can't find product {productId}");
            }

            productsState.ProductIds.Remove(productId);

            var productAddress = Product.DeriveAddress(productId);
            var product = ProductFactory.DeserializeProduct(
                (List)LegacyModule.GetState(world, productAddress));
            product.Validate(productInfo);

            switch (product)
            {
                case FavProduct favProduct:
                    world = LegacyModule.TransferAsset(
                        world,
                        context,
                        productAddress,
                        avatarState.address,
                        favProduct.Asset);
                    break;
                case ItemProduct itemProduct:
                    switch (itemProduct.TradableItem)
                    {
                        case Costume costume:
                            avatarState.UpdateFromAddCostume(costume, true);
                            break;
                        case ItemUsable itemUsable:
                            avatarState.UpdateFromAddItem(itemUsable, true);
                            break;
                        case TradableMaterial tradableMaterial:
                        {
                            avatarState.UpdateFromAddItem(
                                tradableMaterial,
                                itemProduct.ItemCount,
                                true);
                            break;
                        }
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(product));
            }

            var mail = new ProductCancelMail(
                context.BlockIndex,
                productId,
                context.BlockIndex,
                productId);
            avatarState.Update(mail);
            return LegacyModule.SetState(world, productAddress, Null.Value);
        }


        protected override IImmutableDictionary<string, IValue> PlainValueInternal =>
            new Dictionary<string, IValue>
            {
                ["a"] = AvatarAddress.Serialize(),
                ["p"] = new List(ProductInfos.Select(p => p.Serialize())),
                ["c"] = ChargeAp.Serialize(),
            }.ToImmutableDictionary();

        protected override void LoadPlainValueInternal(IImmutableDictionary<string, IValue> plainValue)
        {
            AvatarAddress = plainValue["a"].ToAddress();
            ProductInfos = plainValue["p"].ToList(s => ProductFactory.DeserializeProductInfo((List) s));
            ChargeAp = plainValue["c"].ToBoolean();
        }
    }
}
