using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Bencodex.Types;
using Libplanet;
using Libplanet.Action;
using Libplanet.Assets;
using Nekoyume.Model.Item;
using Nekoyume.Model.Mail;
using Nekoyume.Model.State;
using Serilog;

namespace Nekoyume.Action
{
    [Serializable]
    [ActionType("sell4")]
    public class Sell : GameAction
    {
        public const long ExpiredBlockIndex = 16000;
        public Address sellerAvatarAddress;
        public Guid itemId;
        public FungibleAssetValue price;

        protected override IImmutableDictionary<string, IValue> PlainValueInternal => new Dictionary<string, IValue>
        {
            ["sellerAvatarAddress"] = sellerAvatarAddress.Serialize(),
            ["itemId"] = itemId.Serialize(),
            ["price"] = price.Serialize(),
        }.ToImmutableDictionary();

        protected override void LoadPlainValueInternal(IImmutableDictionary<string, IValue> plainValue)
        {
            sellerAvatarAddress = plainValue["sellerAvatarAddress"].ToAddress();
            itemId = plainValue["itemId"].ToGuid();
            price = plainValue["price"].ToFungibleAssetValue();
        }

        public override IAccountStateDelta Execute(IActionContext context)
        {
            IActionContext ctx = context;
            var states = ctx.PreviousStates;
            if (ctx.Rehearsal)
            {
                states = states.SetState(ShopState.Address, MarkChanged);
                states = states.SetState(sellerAvatarAddress, MarkChanged);
                return states.SetState(ctx.Signer, MarkChanged);
            }

            var addressesHex = GetSignerAndOtherAddressesHex(context, sellerAvatarAddress);

            var sw = new Stopwatch();
            sw.Start();
            var started = DateTimeOffset.UtcNow;
            Log.Verbose("{AddressesHex}Sell exec started", addressesHex);


            if (price.Sign < 0)
            {
                throw new InvalidPriceException($"{addressesHex}Aborted as the price is less than zero: {price}.");
            }

            if (!states.TryGetAgentAvatarStates(ctx.Signer, sellerAvatarAddress, out AgentState agentState, out AvatarState avatarState))
            {
                throw new FailedLoadStateException($"{addressesHex}Aborted as the avatar state of the signer was failed to load.");
            }
            sw.Stop();
            Log.Verbose("{AddressesHex}Sell Get AgentAvatarStates: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            if (!avatarState.worldInformation.IsStageCleared(GameConfig.RequireClearedStageLevel.ActionsInShop))
            {
                avatarState.worldInformation.TryGetLastClearedStageId(out var current);
                throw new NotEnoughClearedStageLevelException(addressesHex, GameConfig.RequireClearedStageLevel.ActionsInShop, current);
            }

            Log.Verbose("{AddressesHex}Sell IsStageCleared: {Elapsed}", addressesHex, sw.Elapsed);

            sw.Restart();

            if (!states.TryGetState(ShopState.Address, out Bencodex.Types.Dictionary shopStateDict))
            {
                throw new FailedLoadStateException($"{addressesHex}Aborted as the shop state was failed to load.");
            }

            Log.Verbose("{AddressesHex}Sell Get ShopState: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            Log.Verbose("{AddressesHex}Execute Sell; seller: {SellerAvatarAddress}", addressesHex, sellerAvatarAddress);

            var productId = context.Random.GenerateRandomGuid();
            long expiredBlockIndex = context.BlockIndex + ExpiredBlockIndex;

            // Select an item to sell from the inventory and adjust the quantity.
            if (!avatarState.inventory.TryGetNonFungibleItem(itemId, out INonFungibleItem nonFungibleItem))
            {
                throw new ItemDoesNotExistException(
                    $"{addressesHex}Aborted as the NonFungibleItem ({itemId}) was failed to load from avatar's inventory.");
            }

            if (nonFungibleItem.RequiredBlockIndex > context.BlockIndex)
            {
                throw new RequiredBlockIndexException(
                    $"{addressesHex}Aborted as the itemUsable to enhance ({itemId}) is not available yet; it will be available at the block #{nonFungibleItem.RequiredBlockIndex}.");
            }

            if (nonFungibleItem is Equipment equipment)
            {
                equipment.Unequip();
            }
            nonFungibleItem.Update(expiredBlockIndex);
            var shopItem = new ShopItem(ctx.Signer, sellerAvatarAddress, productId, price, expiredBlockIndex, nonFungibleItem);

            IValue shopItemSerialized = shopItem.Serialize();
            IKey productIdSerialized = (IKey)productId.Serialize();

            Dictionary products = (Dictionary)shopStateDict["products"];
            products = (Dictionary)products.Add(productIdSerialized, shopItemSerialized);
            shopStateDict = shopStateDict.SetItem("products", products);

            sw.Stop();
            Log.Verbose("{AddressesHex}Sell Get Register Item: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            avatarState.updatedAt = ctx.BlockIndex;
            avatarState.blockIndex = ctx.BlockIndex;

            var result = new SellCancellation.Result
            {
                shopItem = shopItem,
                itemUsable = shopItem.ItemUsable,
                costume = shopItem.Costume
            };
            var mail = new SellCancelMail(result, ctx.BlockIndex, ctx.Random.GenerateRandomGuid(), expiredBlockIndex);
            result.id = mail.id;
            avatarState.UpdateV4(mail, context.BlockIndex);

            states = states.SetState(sellerAvatarAddress, avatarState.Serialize());
            sw.Stop();
            Log.Verbose("{AddressesHex}Sell Set AvatarState: {Elapsed}", addressesHex, sw.Elapsed);
            sw.Restart();

            states = states.SetState(ShopState.Address, shopStateDict);
            sw.Stop();
            var ended = DateTimeOffset.UtcNow;
            Log.Verbose("{AddressesHex}Sell Set ShopState: {Elapsed}", addressesHex, sw.Elapsed);
            Log.Verbose("{AddressesHex}Sell Total Executed Time: {Elapsed}", addressesHex, ended - started);

            return states;
        }
    }
}
