namespace Lib9c.Tests.Action
{
    using System;
    using System.Linq;
    using Libplanet.Types.Assets;
    using Nekoyume.Helper;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.State;
    using Nekoyume.TableData;
    using Xunit;

    public class WorldBossHelperTest
    {
        private readonly Currency _crystalCurrency = CrystalCalculator.CRYSTAL;

        private readonly TableSheets _tableSheets = new (TableSheetsImporter.ImportSheets());

        [Theory]
        [InlineData(10, 10, 0, 10)]
        [InlineData(10, 10, 1, 20)]
        [InlineData(10, 10, 5, 60)]
        public void CalculateTicketPrice(int ticketPrice, int additionalTicketPrice, int purchaseCount, int expected)
        {
            var row = new WorldBossListSheet.Row
            {
                TicketPrice = ticketPrice,
                AdditionalTicketPrice = additionalTicketPrice,
            };
#pragma warning disable CS0618
            // Use of obsolete method Currency.Legacy(): https://github.com/planetarium/lib9c/discussions/1319
            var currency = Currency.Legacy("NCG", 2, null);
#pragma warning restore CS0618
            var raiderState = new RaiderState
            {
                PurchaseCount = purchaseCount,
            };
            Assert.Equal(expected * currency, WorldBossHelper.CalculateTicketPrice(row, raiderState, currency));
        }

        [Theory]
        [InlineData(7200L, 0L, 0L, true)]
        [InlineData(7250L, 7180L, 0L, true)]
        [InlineData(14400L, 14399L, 0L, true)]
        [InlineData(7250L, 7210L, 0L, false)]
        [InlineData(17200L, 10003L, 10000L, true)]
        [InlineData(17199L, 10003L, 10000L, false)]
        public void CanRefillTicketV1(long blockIndex, long refilledBlockIndex, long startedBlockIndex, bool expected)
        {
            Assert.Equal(expected, WorldBossHelper.CanRefillTicketV1(blockIndex, refilledBlockIndex, startedBlockIndex));
        }

        [Theory]
        [InlineData(7200L, 0L, 0L, 7200, true)]
        [InlineData(7250L, 7180L, 0L, 7200, true)]
        [InlineData(14400L, 14399L, 0L, 7200, true)]
        [InlineData(7250L, 7210L, 0L, 7200, false)]
        [InlineData(17200L, 10003L, 10000L, 7200, true)]
        [InlineData(17199L, 10003L, 10000L, 7200, false)]
        [InlineData(7300L, 5L, 0L, 7200, true)]
        [InlineData(7300L, 5L, 0L, 8400, false)]
        [InlineData(7200L, 0L, 0L, 0, false)]
        public void CanRefillTicket(long blockIndex, long refilledBlockIndex, long startedBlockIndex, int refillInterval, bool expected)
        {
            Assert.Equal(expected, WorldBossHelper.CanRefillTicket(blockIndex, refilledBlockIndex, startedBlockIndex, refillInterval));
        }

        [Theory]
        [InlineData(typeof(WorldBossRankRewardSheet))]
        [InlineData(typeof(WorldBossKillRewardSheet))]
        public void CalculateReward(Type sheetType)
        {
            var random = new TestRandom();
            IWorldBossRewardSheet sheet;
            if (sheetType == typeof(WorldBossRankRewardSheet))
            {
                sheet = _tableSheets.WorldBossRankRewardSheet;
            }
            else
            {
                sheet = _tableSheets.WorldBossKillRewardSheet;
            }

            foreach (var rewardRow in sheet.OrderedRows)
            {
                var bossId = rewardRow.BossId;
                var rank = rewardRow.Rank;
                var rewards = WorldBossHelper.CalculateReward(
                    rank,
                    bossId,
                    _tableSheets.RuneWeightSheet,
                    sheet,
                    _tableSheets.RuneSheet,
                    _tableSheets.MaterialItemSheet,
                    random
                );
                var expectedRune = rewardRow.Rune;
                var expectedCrystal = rewardRow.Crystal * _crystalCurrency;
                var expectedCircle = rewardRow.Circle;
                var crystal = rewards.assets.First(f => f.Currency.Equals(_crystalCurrency));
                var rune = rewards.assets
                    .Where(f => !f.Currency.Equals(_crystalCurrency))
                    .Sum(r => (int)r.MajorUnit);
                var circle = rewards.materials
                    .Where(kv => kv.Key.ItemSubType == ItemSubType.Circle)
                    .Sum(kv => kv.Value);

                Assert.Equal(expectedCrystal, crystal);
                Assert.Equal(expectedRune, rune);
                Assert.Equal(expectedCircle, circle);
            }
        }
    }
}
