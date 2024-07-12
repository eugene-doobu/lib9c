namespace Lib9c.Tests.Action.DPoS.Control
{
    using System.Collections.Immutable;
    using Libplanet.Action.State;
    using Libplanet.Crypto;
    using Libplanet.Types.Assets;
    using Nekoyume.Action.DPoS;
    using Nekoyume.Action.DPoS.Control;
    using Nekoyume.Action.DPoS.Exception;
    using Nekoyume.Action.DPoS.Misc;
    using Nekoyume.Action.DPoS.Model;
    using Nekoyume.Action.DPoS.Util;
    using Nekoyume.Model.State;
    using Nekoyume.Module;
    using Xunit;

    public class RedelegateCtrlTest : PoSTest
    {
        private readonly PublicKey _srcOperatorPublicKey;
        private readonly PublicKey _dstOperatorPublicKey;
        private readonly Address _srcOperatorAddress;
        private readonly Address _dstOperatorAddress;
        private readonly Address _delegatorAddress;
        private readonly Address _srcValidatorAddress;
        private readonly Address _dstValidatorAddress;
        private readonly Address _redelegationAddress;
        private ImmutableHashSet<Currency> _nativeTokens;
        private IWorld _states;

        public RedelegateCtrlTest()
        {
            _srcOperatorPublicKey = new PrivateKey().PublicKey;
            _dstOperatorPublicKey = new PrivateKey().PublicKey;
            _srcOperatorAddress = _srcOperatorPublicKey.Address;
            _dstOperatorAddress = _dstOperatorPublicKey.Address;
            _delegatorAddress = CreateAddress();
            _srcValidatorAddress = Validator.DeriveAddress(_srcOperatorAddress);
            _dstValidatorAddress = Validator.DeriveAddress(_dstOperatorAddress);
            _redelegationAddress = Redelegation.DeriveAddress(
                _delegatorAddress, _srcValidatorAddress, _dstValidatorAddress);
            _states = InitialState;
            _nativeTokens = NativeTokens;
        }

        [Fact]
        public void InvalidCurrencyTest()
        {
            Initialize(500, 500, 100, 100);
            _states = _states.MintAsset(
                new ActionContext
                {
                    PreviousState = _states,
                    BlockIndex = 1,
                },
                _delegatorAddress,
                Asset.ConvertTokens(GovernanceToken * 50, Asset.ConsensusToken));
            Assert.Throws<InvalidCurrencyException>(
                    () => _states = RedelegateCtrl.Execute(
                        _states,
                        new ActionContext
                        {
                            PreviousState = _states,
                            BlockIndex = 1,
                        },
                        _delegatorAddress,
                        _srcValidatorAddress,
                        _dstValidatorAddress,
                        Asset.ConvertTokens(GovernanceToken * 30, Asset.ConsensusToken),
                        _nativeTokens));
            Assert.Throws<InvalidCurrencyException>(
                    () => _states = RedelegateCtrl.Execute(
                        _states,
                        new ActionContext
                        {
                            PreviousState = _states,
                            BlockIndex = 1,
                        },
                        _delegatorAddress,
                        _srcValidatorAddress,
                        _dstValidatorAddress,
                        GovernanceToken * 30,
                        _nativeTokens));
        }

        [Fact]
        public void InvalidValidatorTest()
        {
            Initialize(500, 500, 100, 100);
            Assert.Throws<NullValidatorException>(
                () => _states = RedelegateCtrl.Execute(
                    _states,
                    new ActionContext
                    {
                        PreviousState = _states,
                        BlockIndex = 1,
                    },
                    _delegatorAddress,
                    CreateAddress(),
                    _dstValidatorAddress,
                    ShareFromGovernance(10),
                    _nativeTokens));
            Assert.Throws<NullValidatorException>(
                () => _states = RedelegateCtrl.Execute(
                    _states,
                    new ActionContext
                    {
                        PreviousState = _states,
                        BlockIndex = 1,
                    },
                    _delegatorAddress,
                    _srcValidatorAddress,
                    CreateAddress(),
                    ShareFromGovernance(10),
                    _nativeTokens));
        }

        [Fact]
        public void MaxEntriesTest()
        {
            Initialize(500, 500, 100, 100);
            for (long i = 0; i < 10; i++)
            {
                _states = RedelegateCtrl.Execute(
                    _states,
                    new ActionContext
                    {
                        PreviousState = _states,
                        BlockIndex = i,
                    },
                    _delegatorAddress,
                    _srcValidatorAddress,
                    _dstValidatorAddress,
                    ShareFromGovernance(1),
                    _nativeTokens);
            }

            Assert.Throws<MaximumRedelegationEntriesException>(
                    () => _states = RedelegateCtrl.Execute(
                        _states,
                        new ActionContext
                        {
                            PreviousState = _states,
                            BlockIndex = 1,
                        },
                        _delegatorAddress,
                        _srcValidatorAddress,
                        _dstValidatorAddress,
                        ShareFromGovernance(1),
                        _nativeTokens));
        }

        [Fact]
        public void ExceedRedelegateTest()
        {
            Initialize(500, 500, 100, 100);
            Assert.Throws<InsufficientFungibleAssetValueException>(
                    () => _states = RedelegateCtrl.Execute(
                        _states,
                        new ActionContext
                        {
                            PreviousState = _states,
                            BlockIndex = 1,
                        },
                        _delegatorAddress,
                        _srcValidatorAddress,
                        _dstValidatorAddress,
                        ShareFromGovernance(101),
                        _nativeTokens));
        }

        [Theory]
        [InlineData(500, 500, 100, 100, 100)]
        [InlineData(500, 500, 100, 100, 50)]
        public void CompleteRedelegationTest(
            int operatorMintAmount,
            int delegatorMintAmount,
            int selfDelegateAmount,
            int delegateAmount,
            int redelegateAmount)
        {
            Initialize(operatorMintAmount, delegatorMintAmount, selfDelegateAmount, delegateAmount);
            _states = RedelegateCtrl.Execute(
                _states,
                new ActionContext
                {
                    PreviousState = _states,
                    BlockIndex = 1,
                },
                _delegatorAddress,
                _srcValidatorAddress,
                _dstValidatorAddress,
                Asset.Share * redelegateAmount,
                _nativeTokens);
            Assert.Single(
                RedelegateCtrl.GetRedelegation(_states, _redelegationAddress)!
                .RedelegationEntryAddresses);
            _states = RedelegateCtrl.Complete(
                _states,
                new ActionContext
                {
                    PreviousState = _states,
                    BlockIndex = 1000,
                },
                _redelegationAddress);
            Assert.Single(
                RedelegateCtrl.GetRedelegation(_states, _redelegationAddress)!
                .RedelegationEntryAddresses);
            _states = RedelegateCtrl.Complete(
                _states,
                new ActionContext
                {
                    PreviousState = _states,
                    BlockIndex = 50400 * 5,
                },
                _redelegationAddress);
            Assert.Empty(
                RedelegateCtrl.GetRedelegation(_states, _redelegationAddress)!
                .RedelegationEntryAddresses);
        }

        [Theory]
        [InlineData(500, 500, 100, 100, 100)]
        [InlineData(500, 500, 100, 100, 50)]
        public void BalanceTest(
            int operatorMintAmount,
            int delegatorMintAmount,
            int selfDelegateAmount,
            int delegateAmount,
            int redelegateAmount)
        {
            Initialize(operatorMintAmount, delegatorMintAmount, selfDelegateAmount, delegateAmount);
            _states = RedelegateCtrl.Execute(
                _states,
                new ActionContext
                {
                    PreviousState = _states,
                    BlockIndex = 1,
                },
                _delegatorAddress,
                _srcValidatorAddress,
                _dstValidatorAddress,
                Asset.Share * redelegateAmount,
                _nativeTokens);
            Assert.Equal(
                GovernanceToken * 0,
                _states.GetBalance(_srcValidatorAddress, GovernanceToken));
            Assert.Equal(
                GovernanceToken * 0,
                _states.GetBalance(_dstValidatorAddress, GovernanceToken));
            Assert.Equal(
                Asset.ConsensusFromGovernance(GovernanceToken * 0),
                _states.GetBalance(_srcOperatorAddress, Asset.ConsensusToken));
            Assert.Equal(
                Asset.ConsensusFromGovernance(GovernanceToken * 0),
                _states.GetBalance(_dstOperatorAddress, Asset.ConsensusToken));
            Assert.Equal(
                Asset.ConsensusFromGovernance(GovernanceToken * 0),
                _states.GetBalance(_delegatorAddress, Asset.ConsensusToken));
            Assert.Equal(
                ShareFromGovernance(0),
                _states.GetBalance(_srcOperatorAddress, Asset.Share));
            Assert.Equal(
                ShareFromGovernance(0),
                _states.GetBalance(_dstOperatorAddress, Asset.Share));
            Assert.Equal(
                ShareFromGovernance(0),
                _states.GetBalance(_delegatorAddress, Asset.Share));
            Assert.Equal(
                GovernanceToken * (operatorMintAmount - selfDelegateAmount),
                _states.GetBalance(_srcOperatorAddress, GovernanceToken));
            Assert.Equal(
                GovernanceToken * (operatorMintAmount - selfDelegateAmount),
                _states.GetBalance(_dstOperatorAddress, GovernanceToken));
            Assert.Equal(
                GovernanceToken * (delegatorMintAmount - delegateAmount),
                _states.GetBalance(_delegatorAddress, GovernanceToken));
            Assert.Equal(
                GovernanceToken * (2 * selfDelegateAmount + delegateAmount),
                _states.GetBalance(ReservedAddress.UnbondedPool, GovernanceToken));
            var balanceA = _states.GetBalance(
                Delegation.DeriveAddress(
                    _srcOperatorAddress,
                    _srcValidatorAddress),
                Asset.Share);
            var balanceB = _states.GetBalance(
                Delegation.DeriveAddress(
                    _delegatorAddress,
                    _srcValidatorAddress),
                Asset.Share);
            Assert.Equal(
                ValidatorCtrl.GetValidator(_states, _srcValidatorAddress)!.DelegatorShares,
                balanceA + balanceB);
            balanceA = _states.GetBalance(
                Delegation.DeriveAddress(
                    _dstOperatorAddress,
                    _dstValidatorAddress),
                Asset.Share);
            balanceB = _states.GetBalance(
                Delegation.DeriveAddress(
                    _delegatorAddress,
                    _dstValidatorAddress),
                Asset.Share);
            Assert.Equal(
                ValidatorCtrl.GetValidator(_states, _dstValidatorAddress)!.DelegatorShares,
                balanceA + balanceB);
            RedelegationEntry entry = new RedelegationEntry(
                _states.GetDPoSState(
                    RedelegateCtrl.GetRedelegation(_states, _redelegationAddress)!
                        .RedelegationEntryAddresses[0])!);
            Assert.Equal(
                Asset.ConsensusFromGovernance(GovernanceToken * (selfDelegateAmount + delegateAmount))
                - entry.UnbondingConsensusToken,
                _states.GetBalance(_srcValidatorAddress, Asset.ConsensusToken));
            Assert.Equal(
                Asset.ConsensusFromGovernance(GovernanceToken * selfDelegateAmount)
                + entry.UnbondingConsensusToken,
                _states.GetBalance(_dstValidatorAddress, Asset.ConsensusToken));
        }

        private void Initialize(
            int operatorMintAmount,
            int delegatorMintAmount,
            int selfDelegateAmount,
            int delegateAmount)
        {
            _states = _states.TransferAsset(
                new ActionContext(),
                GoldCurrencyState.Address,
                _srcOperatorAddress,
                GovernanceToken * operatorMintAmount);
            _states = _states.TransferAsset(
                new ActionContext(),
                GoldCurrencyState.Address,
                _dstOperatorAddress,
                GovernanceToken * operatorMintAmount);
            _states = _states.TransferAsset(
                new ActionContext(),
                GoldCurrencyState.Address,
                _delegatorAddress,
                GovernanceToken * delegatorMintAmount);
            _states = ValidatorCtrl.Create(
                _states,
                new ActionContext
                {
                    PreviousState = _states,
                    BlockIndex = 1,
                },
                _srcOperatorAddress,
                _srcOperatorPublicKey,
                GovernanceToken * selfDelegateAmount,
                _nativeTokens);
            _states = ValidatorCtrl.Create(
                _states,
                new ActionContext
                {
                    PreviousState = _states,
                    BlockIndex = 1,
                },
                _dstOperatorAddress,
                _dstOperatorPublicKey,
                GovernanceToken * selfDelegateAmount,
                _nativeTokens);
            _states = DelegateCtrl.Execute(
                _states,
                new ActionContext
                {
                    PreviousState = _states,
                    BlockIndex = 1,
                },
                _delegatorAddress,
                _srcValidatorAddress,
                GovernanceToken * delegateAmount,
                _nativeTokens);
        }
    }
}