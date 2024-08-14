using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using Libplanet.Types.Assets;
using Nekoyume.Action.DPoS.Control;
using Nekoyume.Action.DPoS.Exception;
using Nekoyume.Action.DPoS.Model;
using Nekoyume.Action.DPoS.Util;
using Nekoyume.Module;

namespace Nekoyume.Action.DPoS
{
    /// <summary>
    /// A system action for DPoS that withdraws reward tokens from given <see cref="Validator"/>.
    /// </summary>
    [ActionType(ActionTypeValue)]
    public sealed class WithdrawDelegator : ActionBase
    {
        private const string ActionTypeValue = "withdraw_delegator";

        /// <summary>
        /// Creates a new instance of <see cref="WithdrawDelegator"/> action.
        /// </summary>
        /// <param name="validator">The <see cref="Address"/> of the validator
        /// from which to withdraw the tokens.</param>
        public WithdrawDelegator(Address validator)
        {
            Validator = validator;
        }

        public WithdrawDelegator()
        {
            // Used only for deserialization.  See also class Libplanet.Action.Sys.Registry.
        }

        /// <summary>
        /// The <see cref="Address"/> of the validator to withdraw.
        /// </summary>
        public Address Validator { get; set; }

        /// <inheritdoc cref="IAction.PlainValue"/>
        public override IValue PlainValue => Bencodex.Types.Dictionary.Empty
            .Add("type_id", new Text(ActionTypeValue))
            .Add("validator", Validator.Serialize());

        /// <inheritdoc cref="IAction.LoadPlainValue(IValue)"/>
        public override void LoadPlainValue(IValue plainValue)
        {
            var dict = (Bencodex.Types.Dictionary)plainValue;
            Validator = dict["validator"].ToAddress();
        }

        /// <inheritdoc cref="IAction.Execute(IActionContext)"/>
        public override IWorld Execute(IActionContext context)
        {
            GasTracer.UseGas(1);
            IActionContext ctx = context;
            var states = ctx.PreviousState;
            var nativeTokens = states.GetNativeTokens();

            states = DelegateCtrl.Distribute(
                states,
                context,
                nativeTokens,
                Delegation.DeriveAddress(context.Signer, Validator));

            if (!(ValidatorCtrl.GetValidator(states, Validator) is { } validator))
            {
                throw new NullValidatorException(Validator);
            }

#pragma warning disable LAA1002
            foreach (var nativeToken in nativeTokens)
            {
                states = states.StartNew(ctx, nativeToken, Validator, validator.DelegatorShares);
            }

            foreach (Currency nativeToken in nativeTokens)
            {
                FungibleAssetValue reward = states.GetBalance(
                    AllocateRewardCtrl.RewardAddress(context.Signer), nativeToken);
                if (reward.Sign > 0)
                {
                    states = states.TransferAsset(
                        context,
                        AllocateRewardCtrl.RewardAddress(context.Signer),
                        context.Signer,
                        reward);
                }
            }
#pragma warning restore LAA1002

            return states;
        }
    }
}