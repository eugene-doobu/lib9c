namespace Lib9c.Tests.Action
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Bencodex.Types;
    using Libplanet.Action;
    using Libplanet.Action.State;
    using Libplanet.Crypto;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Model.Collection;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.State;
    using Nekoyume.Module;
    using Nekoyume.TableData;
    using Xunit;
    using static SerializeKeys;

    public class ActivateCollectionTest
    {
        private readonly IWorld _initialState;
        private readonly Address _agentAddress;
        private readonly Address _avatarAddress;
        private readonly TableSheets _tableSheets;

        public ActivateCollectionTest()
        {
            var sheets = TableSheetsImporter.ImportSheets();
            _tableSheets = new TableSheets(sheets);

            var privateKey = new PrivateKey();
            _agentAddress = privateKey.PublicKey.Address;
            var agentState = new AgentState(_agentAddress);

            _avatarAddress = _agentAddress.Derive("avatar");
            var gameConfigState = new GameConfigState(sheets[nameof(GameConfigSheet)]);
            var avatarState = new AvatarState(
                _avatarAddress,
                _agentAddress,
                0,
                _tableSheets.GetAvatarSheets(),
                gameConfigState,
                default
            )
            {
                level = 100,
            };
            var inventoryAddress = _avatarAddress.Derive(LegacyInventoryKey);
            var worldInformationAddress = _avatarAddress.Derive(LegacyWorldInformationKey);
            var questListAddress = _avatarAddress.Derive(LegacyQuestListKey);
            agentState.avatarAddresses.Add(0, _avatarAddress);

            _initialState = new World(new MockWorldState())
                .SetAgentState(_agentAddress, agentState)
                .SetAvatarState(_avatarAddress, avatarState, true, true, true, true)
                .SetLegacyState(gameConfigState.address, gameConfigState.Serialize());

            foreach (var (key, value) in sheets)
            {
                _initialState = _initialState
                    .SetLegacyState(Addresses.TableSheet.Derive(key), value.Serialize());
            }
        }

        [Fact]
        public void Execute()
        {
            var row = _tableSheets.CollectionSheet.Values.First();
            var avatarState = _initialState.GetAvatarState(_avatarAddress);
            var materials = new List<ICollectionMaterial>();
            foreach (var material in row.Materials)
            {
                var itemRow = _tableSheets.ItemSheet[material.ItemId];
                var item = ItemFactory.CreateItem(itemRow, new TestRandom());
                avatarState.inventory.AddItem(item, material.Count);
                if (item is ItemUsable itemUsable)
                {
                    materials.Add(new NonFungibleCollectionMaterial
                    {
                        ItemId = item.Id,
                        NonFungibleId = itemUsable.NonFungibleId,
                        OptionCount = material.OptionCount,
                        SkillContains = material.SkillContains,
                    });
                }
                else
                {
                    materials.Add(new FungibleCollectionMaterial
                    {
                        ItemId = item.Id,
                        ItemCount = material.Count,
                    });
                }
            }

            var inventoryAddress = _avatarAddress.Derive(LegacyInventoryKey);
            var state = _initialState.SetAvatarState(_avatarAddress, avatarState, false, true, false, false);
            IActionContext context = new ActionContext()
            {
                PreviousState = state,
                Signer = _agentAddress,
            };
            ActivateCollection activateCollection = new ActivateCollection()
            {
                AvatarAddress = _avatarAddress,
                CollectionId = row.Id,
                Materials = materials,
            };

            var nextState = activateCollection.Execute(context);
            var collectionAddress = CollectionState.Derive(_avatarAddress);
            var collectionState = nextState.GetCollectionState(collectionAddress);
            Assert.Equal(row.Id, collectionState.Ids.Single());

            var nextAvatarState = nextState.GetAvatarState(_avatarAddress);
            Assert.Empty(nextAvatarState.inventory.Items);
        }
    }
}
