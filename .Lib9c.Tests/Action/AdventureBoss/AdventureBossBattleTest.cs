namespace Lib9c.Tests.Action.AdventureBoss
{
    using System.Linq;
    using Libplanet.Action.State;
    using Libplanet.Crypto;
    using Libplanet.Mocks;
    using Nekoyume;
    using Nekoyume.Action.AdventureBoss;
    using Nekoyume.Model.AdventureBoss;
    using Nekoyume.Model.State;
    using Nekoyume.Module;
    using Xunit;

    public class AdventureBossBattleTest
    {
        [Fact]
        public void Execute()
        {
            var agentAddress = new PrivateKey().Address;
            var avatarAddress = Addresses.GetAvatarAddress(agentAddress, 0);
            var avatarAddress2 = Addresses.GetAvatarAddress(agentAddress, 1);
            var agentState = new AgentState(agentAddress)
            {
                avatarAddresses =
                {
                    [0] = avatarAddress,
                    [1] = avatarAddress2,
                },
            };
            var tableSheets = new TableSheets(TableSheetsImporter.ImportSheets());
            var avatarState = new AvatarState(
                avatarAddress,
                agentAddress,
                0L,
                tableSheets.GetAvatarSheets(),
                default
            );
            var avatarState2 = new AvatarState(
                avatarAddress2,
                agentAddress,
                0L,
                tableSheets.GetAvatarSheets(),
                default
            );
            var state = new World(MockUtil.MockModernWorldState)
                .SetAgentState(agentAddress, agentState)
                .SetAvatarState(avatarAddress, avatarState)
                .SetAvatarState(avatarAddress2, avatarState2);

            var seasonInfo = new SeasonInfo(1, 0L);
            var exploreBoard = new ExploreBoard(1);
            state = state.SetSeasonInfo(seasonInfo);
            state = state.SetLatestAdventureBossSeason(seasonInfo);
            state = state.SetExploreBoard(1, exploreBoard);

            var action = new AdventureBossBattle()
            {
                Season = 1,
                AvatarAddress = avatarAddress,
            };
            var nextState = action.Execute(new ActionContext
            {
                PreviousState = state,
                Signer = agentAddress,
                BlockIndex = 0L,
            });

            exploreBoard = nextState.GetExploreBoard(1);
            Assert.Single(exploreBoard.ExplorerList);
            Assert.Equal(avatarAddress, exploreBoard.ExplorerList.First().Item1);

            var adventureInfo = nextState.GetExplorer(1, avatarAddress);
            Assert.Equal(avatarAddress, adventureInfo.AvatarAddress);
            Assert.Equal(100, adventureInfo.Score);
            Assert.Equal(1, adventureInfo.Floor);

            action.AvatarAddress = avatarAddress2;
            nextState = action.Execute(new ActionContext
            {
                PreviousState = nextState,
                Signer = agentAddress,
                BlockIndex = 1L,
            });

            exploreBoard = nextState.GetExploreBoard(1);
            Assert.Equal(2, exploreBoard.ExplorerList.Count);
            var exploreAddressInfo = exploreBoard.ExplorerList.OrderBy(e => e)
                .Select(e => e.Item1);
            Assert.Contains(avatarAddress2, exploreAddressInfo);

            adventureInfo = nextState.GetExplorer(1, avatarAddress2);
            Assert.Equal(avatarAddress2, adventureInfo.AvatarAddress);
            Assert.Equal(100, adventureInfo.Score);
            Assert.Equal(1, adventureInfo.Floor);

            action.AvatarAddress = avatarAddress;
            nextState = action.Execute(new ActionContext
            {
                PreviousState = nextState,
                Signer = agentAddress,
                BlockIndex = 2L,
            });

            exploreBoard = nextState.GetExploreBoard(1);
            Assert.Equal(2, exploreBoard.ExplorerList.Count);

            adventureInfo = nextState.GetExplorer(1, avatarAddress);
            Assert.Equal(avatarAddress, adventureInfo.AvatarAddress);
            Assert.Equal(200, adventureInfo.Score);
            Assert.Equal(2, adventureInfo.Floor);
        }
    }
}
