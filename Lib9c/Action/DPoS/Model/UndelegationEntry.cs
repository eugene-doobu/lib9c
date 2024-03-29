#nullable enable
using System;
using Bencodex.Types;
using Libplanet.Common;
using Libplanet.Crypto;
using Libplanet.Types.Assets;
using Nekoyume.Action.DPoS.Misc;
using Nekoyume.Action.DPoS.Util;

namespace Nekoyume.Action.DPoS.Model
{
    public class UndelegationEntry : IEquatable<UndelegationEntry>
    {
        private FungibleAssetValue _unbondingConsensusToken;

        public UndelegationEntry(
            Address undelegationAddress,
            FungibleAssetValue unbondingConsensusToken,
            long index,
            long blockHeight)
        {
            Address = DeriveAddress(undelegationAddress, index);
            UndelegationAddress = undelegationAddress;
            UnbondingConsensusToken = unbondingConsensusToken;
            Index = index;
            CompletionBlockHeight = blockHeight + UnbondingSet.Period;
        }

        public UndelegationEntry(IValue serialized)
        {
            List serializedList = (List)serialized;
            Address = serializedList[0].ToAddress();
            UndelegationAddress = serializedList[1].ToAddress();
            UnbondingConsensusToken = serializedList[2].ToFungibleAssetValue();
            Index = serializedList[3].ToLong();
            CompletionBlockHeight = serializedList[4].ToLong();
        }

        public Address Address { get; set; }

        public Address UndelegationAddress { get; set; }

        public FungibleAssetValue UnbondingConsensusToken
        {
            get => _unbondingConsensusToken;
            set
            {
                if (!value.Currency.Equals(Asset.ConsensusToken))
                {
                    throw new Exception.InvalidCurrencyException(Asset.ConsensusToken, value.Currency);
                }

                _unbondingConsensusToken = value;
            }
        }

        public long Index { get; set; }

        public long CompletionBlockHeight { get; set; }

        public static bool operator ==(UndelegationEntry obj, UndelegationEntry other)
        {
            return obj.Equals(other);
        }

        public static bool operator !=(UndelegationEntry obj, UndelegationEntry other)
        {
            return !(obj == other);
        }

        public static Address DeriveAddress(Address undelegationAddress, long index)
        {
            return AddressHelper.Derive(undelegationAddress, $"UndelegationEntry{index}");
        }

        public bool IsMatured(long blockHeight) => blockHeight >= CompletionBlockHeight;

        public IValue Serialize()
        {
            return List.Empty
                .Add(Address.Serialize())
                .Add(UndelegationAddress.Serialize())
                .Add(UnbondingConsensusToken.Serialize())
                .Add(Index.Serialize())
                .Add(CompletionBlockHeight.Serialize());
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as UndelegationEntry);
        }

        public bool Equals(UndelegationEntry? other)
        {
            return !(other is null) &&
                   Address.Equals(other.Address) &&
                   UndelegationAddress.Equals(other.UndelegationAddress) &&
                   UnbondingConsensusToken.Equals(other.UnbondingConsensusToken) &&
                   Index == other.Index &&
                   CompletionBlockHeight == other.CompletionBlockHeight;
        }

        public override int GetHashCode()
        {
            return ByteUtil.CalculateHashCode(Address.ToByteArray());
        }
    }
}