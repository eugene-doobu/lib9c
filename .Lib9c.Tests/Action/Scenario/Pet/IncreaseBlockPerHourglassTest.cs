// Valkyrie increases block per hourglass by value.

namespace Lib9c.Tests.Action.Scenario.Pet
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Bencodex.Types;
    using Lib9c.Tests.Util;
    using Libplanet;
    using Libplanet.Action;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.Pet;
    using Nekoyume.Model.State;
    using Nekoyume.TableData;
    using Xunit;
    using static Lib9c.SerializeKeys;

    public class IncreaseBlockPerHourglassTest
    {
        private const PetOptionType PetOptionType =
            Nekoyume.Model.Pet.PetOptionType.IncreaseBlockPerHourglass;

        private readonly Address _agentAddr;
        private readonly Address _avatarAddr;
        private readonly Address _inventoryAddr;
        private readonly Address _worldInfoAddr;
        private readonly Address _recipeIdsAddr;
        private readonly IAccountStateDelta _initialStateV1;
        private readonly IAccountStateDelta _initialStateV2;
        private readonly TableSheets _tableSheets;
        private readonly int _hourglassItemId;
        private int? _petId;

        public IncreaseBlockPerHourglassTest()
        {
            (
                _tableSheets,
                _agentAddr,
                _avatarAddr,
                _initialStateV1,
                _initialStateV2
            ) = InitializeUtil.InitializeStates();
            _inventoryAddr = _avatarAddr.Derive(LegacyInventoryKey);
            _worldInfoAddr = _avatarAddr.Derive(LegacyWorldInformationKey);
            _recipeIdsAddr = _avatarAddr.Derive("recipe_ids");
            _hourglassItemId = _tableSheets.MaterialItemSheet.Values.First(
                item => item.ItemSubType == ItemSubType.Hourglass
            ).Id;
        }

        [Theory]
        [InlineData(1, 10113000, null)] // No Pet
        [InlineData(1, 10113000, 1)] // Lv.1 increases 1 block per HG: 3 -> 4
        [InlineData(1, 10113000, 30)] // Lv.30 increases 30 blocks per HG: 3 -> 33
        [InlineData(1, 10120000, 30)] // Test for min. Hourglass is 1
        public void RapidCombinationTest_Equipment(
            int randomSeed,
            int targetItemId,
            int? petLevel
        )
        {
            var random = new TestRandom(randomSeed);

            // Disable all quests to prevent contamination by quest reward
            var (stateV1, stateV2) = QuestUtil.DisableQuestList(
                _initialStateV1,
                _initialStateV2,
                _avatarAddr
            );

            // Get recipe
            var recipe =
                _tableSheets.EquipmentItemRecipeSheet.Values.First(
                    recipe => recipe.ResultEquipmentId == targetItemId
                );
            Assert.NotNull(recipe);

            // Get Materials and stages
            var materialList = recipe.GetAllMaterials(
                _tableSheets.EquipmentItemSubRecipeSheetV2
            ).ToList();

            var recipeIds = List.Empty;
            for (var i = 1; i < recipe.UnlockStage; i++)
            {
                recipeIds = recipeIds.Add(i.Serialize());
            }

            stateV2 = stateV2.SetState(_recipeIdsAddr, recipeIds);

            var expectedHourglass = (int)Math.Ceiling(
                ((double)recipe.RequiredBlockIndex
                 - stateV2.GetGameConfigState().RequiredAppraiseBlock)
                /
                stateV2.GetGameConfigState().HourglassPerBlock);

            // Get pet
            if (!(petLevel is null))
            {
                var petRow = _tableSheets.PetOptionSheet.Values.First(
                    pet => pet.LevelOptionMap[(int)petLevel!].OptionType == PetOptionType
                );
                _petId = petRow.PetId;
                stateV2 = stateV2.SetState(
                    PetState.DeriveAddress(_avatarAddr, (int)_petId),
                    new List(_petId!.Serialize(), petLevel.Serialize(), 0L.Serialize())
                );
                expectedHourglass = (int)Math.Ceiling(
                    (recipe.RequiredBlockIndex
                     - stateV2.GetGameConfigState().RequiredAppraiseBlock)
                    /
                    (stateV2.GetGameConfigState().HourglassPerBlock
                     + petRow.LevelOptionMap[(int)petLevel].OptionValue)
                );
            }

            // Give hourglass
            stateV2 = CraftUtil.AddMaterialsToInventory(
                stateV2,
                _tableSheets,
                _avatarAddr,
                new List<EquipmentItemSubRecipeSheet.MaterialInfo>
                {
                    new EquipmentItemSubRecipeSheet.MaterialInfo(
                        _hourglassItemId,
                        expectedHourglass
                    ),
                },
                random
            );

            // Prepare to combination
            stateV2 = CraftUtil.PrepareCombinationSlot(stateV2, _avatarAddr, 0);
            stateV2 = CraftUtil.AddMaterialsToInventory(
                stateV2,
                _tableSheets,
                _avatarAddr,
                materialList,
                random
            );
            stateV2 = CraftUtil.UnlockStage(
                stateV2,
                _tableSheets,
                _worldInfoAddr,
                recipe.UnlockStage
            );

            // Do combination
            var action = new CombinationEquipment
            {
                avatarAddress = _avatarAddr,
                slotIndex = 0,
                recipeId = recipe.Id,
                subRecipeId = recipe.SubRecipeIds?[0],
                petId = _petId,
            };

            stateV2 = action.Execute(new ActionContext
            {
                PreviousStates = stateV2,
                Signer = _agentAddr,
                BlockIndex = 0L,
                Random = random,
            });

            // Do rapid combination
            var rapidAction = new RapidCombination
            {
                avatarAddress = _avatarAddr,
                slotIndex = 0,
            };
            stateV2 = rapidAction.Execute(new ActionContext
            {
                PreviousStates = stateV2,
                Signer = _agentAddr,
                BlockIndex = stateV2.GetGameConfigState().RequiredAppraiseBlock,
                Random = random,
            });

            var slotState = stateV2.GetCombinationSlotState(_avatarAddr, 0);
            // TEST: Combination should be done
            Assert.Equal(
                stateV2.GetGameConfigState().RequiredAppraiseBlock,
                slotState.RequiredBlockIndex
            );

            // TEST: All Hourglasses should be used
            var inventoryState = new Inventory((List)stateV2.GetState(_inventoryAddr));
            Assert.Equal(1, inventoryState.Items.Count);
            Assert.Throws<InvalidOperationException>(() =>
                inventoryState.Items.First(item => item.item.Id == _hourglassItemId));
        }

        [Theory]
        [InlineData(0, 10114000, 1, "success")] // Lv.1 reduces 5.5%
        [InlineData(0, 10114000, 30, "success")] // Lv.30 reduces 20%
        [InlineData(14, 10114000, 1, "greatSuccess")] // Lv.1 reduces 5.5%
        [InlineData(14, 10114000, 30, "greatSuccess")] // Lv.30 reduces 20%
        [InlineData(10, 10114000, 1, "fail")] // Lv.1 reduces 5.5%
        [InlineData(10, 10114000, 30, "fail")] // Lv.30 reduces 20%
        public void RapidCombinationTest_ItemEnhancement(
            int randomSeed,
            int targetItemId,
            int petLevel,
            string resultType
        )
        {
            const int itemLevel = 4;
            var random = new TestRandom(randomSeed);
            var avatarState = _initialStateV2.GetAvatarStateV2(_avatarAddr);

            // Prepare equipments to enhance
            var equipmentRow = _tableSheets.EquipmentItemSheet.Values.First(
                item => item.Id == targetItemId
            );
            var equipment = (Equipment)ItemFactory.CreateItemUsable(
                equipmentRow,
                default,
                0,
                itemLevel
            );
            var material = (Equipment)ItemFactory.CreateItemUsable(
                equipmentRow,
                Guid.NewGuid(),
                0,
                itemLevel
            );
            avatarState.inventory.AddItem(equipment);
            avatarState.inventory.AddItem(material);
            var stateV2 =
                _initialStateV2.SetState(_inventoryAddr, avatarState.inventory.Serialize());

            // Get pet
            var petRow = _tableSheets.PetOptionSheet.Values.First(
                pet => pet.LevelOptionMap[petLevel].OptionType == PetOptionType
            );
            _petId = petRow.PetId;
            stateV2 = stateV2.SetState(
                PetState.DeriveAddress(_avatarAddr, (int)_petId),
                new List(_petId.Serialize(), petLevel.Serialize(), 0L.Serialize())
            );

            // Prepare enhancement
            var enhancementRow = _tableSheets.EnhancementCostSheetV2.Values.First(
                cost => cost.ItemSubType == ItemSubType.Weapon
                        && cost.Grade == equipment.Grade
                        && cost.Level == equipment.level + 1
            );
            var requiredBlock = resultType switch
            {
                "success" => enhancementRow.SuccessRequiredBlockIndex,
                "greatSuccess" => enhancementRow.GreatSuccessRequiredBlockIndex,
                _ => enhancementRow.FailRequiredBlockIndex
            };
            var expectedHourglass = (int)Math.Max(1, Math.Ceiling(
                (requiredBlock
                 - stateV2.GetGameConfigState().RequiredAppraiseBlock)
                /
                (stateV2.GetGameConfigState().HourglassPerBlock
                 + petRow.LevelOptionMap[petLevel].OptionValue)
            ));

            // Prepare combination slot
            stateV2 = CraftUtil.PrepareCombinationSlot(stateV2, _avatarAddr, 0);

            // Give hourglasses
            stateV2 = CraftUtil.AddMaterialsToInventory(
                stateV2,
                _tableSheets,
                _avatarAddr,
                new List<EquipmentItemSubRecipeSheet.MaterialInfo>
                {
                    new EquipmentItemSubRecipeSheet.MaterialInfo(
                        _hourglassItemId,
                        expectedHourglass
                    ),
                },
                random
            );

            // Unlock stage
            stateV2 = CraftUtil.UnlockStage(
                stateV2,
                _tableSheets,
                _worldInfoAddr,
                GameConfig.RequireClearedStageLevel.ItemEnhancementAction
            );

            // Do Enhancement
            var action = new ItemEnhancement
            {
                avatarAddress = _avatarAddr,
                itemId = equipment.ItemId,
                materialId = material.ItemId,
                slotIndex = 0,
                petId = _petId,
            };

            stateV2 = action.Execute(new ActionContext
            {
                PreviousStates = stateV2,
                Signer = _agentAddr,
                BlockIndex = 0L,
                Random = random,
            });

            // RapidCombintaion
            var rapidAction = new RapidCombination
            {
                avatarAddress = _avatarAddr,
                slotIndex = 0,
            };

            stateV2 = rapidAction.Execute(new ActionContext
            {
                PreviousStates = stateV2,
                Signer = _agentAddr,
                BlockIndex = stateV2.GetGameConfigState().RequiredAppraiseBlock,
                Random = random,
            });

            var slotState = stateV2.GetCombinationSlotState(_avatarAddr, 0);
            // TEST: Item is completed
            Assert.Equal(
                stateV2.GetGameConfigState().RequiredAppraiseBlock,
                slotState.RequiredBlockIndex
            );
            // TEST: ALl Hourglasses are used
            var inventoryState = new Inventory((List)stateV2.GetState(_inventoryAddr));
            Assert.Throws<InvalidOperationException>(
                () => inventoryState.Items.First(item => item.item.Id == _hourglassItemId)
            );
        }
    }
}